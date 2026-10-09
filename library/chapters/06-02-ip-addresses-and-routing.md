---
id: "6-2"
number: "6.2"
title: "IP addresses and routing"
part: 6
part_title: "Networks"
words: 6559
minutes: 29
---

# 6.2 IP addresses and routing

> **Quick look.** IP addresses, subnets and how packets are routed hop by hop.
>
> - IPv4 addresses are 32 bits (and ran out); IPv6 are 128 bits.
> - A prefix like /24 says how many leading bits name the network.
> - Loopback is this machine; private ranges are internal; listening on 0.0.0.0 means every interface (needed in containers).
> - Routers pick the most specific matching route; TTL stops loops.
> - NAT lets private machines share one public address; idle connections can be dropped by NAT.
> - Put only the load balancer in a public subnet; plan CIDR ranges so networks never overlap.


### The big picture

A packet needs an address, and the network needs a way to get it there. On the internet, the address is an IP address, and delivery happens hop by hop. Each router along the way reads the address and passes the packet one step closer. It is like a letter moving from sorting office to sorting office, without anyone planning its whole journey.

This chapter explains what IP addresses look like and why there are two versions. It covers how addresses are grouped into networks with the "/24" notation you see in cloud consoles and firewall rules, and which ranges are special. It then explains how routers decide where to send each packet, and how your home router lets a whole household share one public address. You meet all of this whenever you configure a cloud network, a container, a VPN or a firewall rule.

### Before you start

**Builds on:** 1.1 Bits and bytes, 6.1 Layers and packets.

**Ideas explained later:**

- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Port** (6.3): A 16-bit number that identifies which program on a machine a packet is for.
- **DNS** (6.4): Domain Name System: the distributed directory that turns names into addresses and other records.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Lease** (12.2): A lock that expires after a set time unless renewed.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **TCP** (6.3): Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **UDP** (6.3): User Datagram Protocol: independent messages with ports and no delivery guarantees.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, null operators, switch expressions and patterns. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. How many IPv4 addresses are there, and why did they run out?**

> 2³², about 4.3 billion, because an IPv4 address is 32 bits. There are far more devices than that, and many addresses are reserved. The central pool ran out in 2011.

**2. What does the /24 in 192.168.1.0/24 mean?**

> The first 24 bits identify the network, and the remaining 8 bits number the machines within it. So the network holds 2⁸ = 256 addresses, from 192.168.1.0 to 192.168.1.255.

**3. What are addresses starting with 10., 172.16 to 172.31, and 192.168. for?**

> Private networks: homes, offices and cloud networks. They aren't routed on the public internet, so any number of networks can reuse them.

**4. What does NAT do?**

> Network address translation rewrites addresses as packets pass through a router, typically letting many machines with private addresses share one public address. It is why a home network needs "port forwarding" to accept incoming connections.

**5. A service in a container listens on 127.0.0.1:8080, and nothing outside the container can reach it. Why?**

> 127.0.0.1 is the loopback address: it only accepts connections from the same machine, which here means inside the container. Listen on 0.0.0.0, meaning every interface, instead.

**6. How does a router choose where to send a packet?**

> It looks up the destination address in its routing table and picks the most specific matching entry, the one with the longest prefix. If nothing more specific matches, it uses its default route.

**7. What does traceroute show, and how does it work?**

> The list of routers a packet passes through. It sends packets with a time to live of 1, then 2, then 3, and so on. Each router that drops a packet whose time ran out sends back a message, revealing itself.

### Addresses for machines

Every device on the internet is reached through an **IP address**. Strictly, an address belongs to a network interface rather than a whole machine, so a laptop with Wi-Fi and Ethernet can have two.

The original version, **IPv4**, uses addresses of 32 bits: four bytes. They are written as four decimal numbers from 0 to 255, separated by dots, one per byte: `192.168.1.20`. With 32 bits there are 2³² possible addresses, about 4.3 billion. That seemed plenty in 1981. It wasn't. Phones, laptops, servers, cars and light bulbs all want addresses, and the central pool of unassigned IPv4 addresses ran out in 2011. IPv4 addresses are now bought and sold.

**IPv6**, the replacement, uses 128 bits, enough for about 3.4 × 10³⁸ addresses, more than could ever be used up. They are written as eight groups of four hex digits separated by colons: `2001:0db8:0000:0000:0000:0000:0000:0001`. Leading zeros in a group can be dropped, and one run of all-zero groups can be replaced by `::`, so that address is usually written `2001:db8::1`. Around half of the traffic to large websites now uses IPv6, and most devices run both versions side by side.

### Networks and prefixes

Addresses aren't handed out one at a time. They come in blocks, and each block is a network. An address has two parts: the first bits identify the network, and the remaining bits identify one interface within it. The first part is the **network prefix**.

**CIDR notation**, from "classless inter-domain routing", writes the prefix length after a slash. `192.168.1.0/24` means the first 24 bits, `192.168.1`, identify the network. The last 8 bits number the machines in it, so it holds 2⁸ = 256 addresses, `192.168.1.0` to `192.168.1.255`. `10.0.0.0/8` is a network of 2²⁴ addresses, over 16 million. `/32` is a single address.

The same information is sometimes written as a **subnet mask**: a 32-bit number with 1s for the network bits and 0s for the rest. `/24` is `255.255.255.0`. To test whether an address is in a network, AND it with the mask and compare the result with the network's own address, using the bitwise operators from Bits and bytes (1.1). That is exactly what routers and firewalls do, millions of times a second.

In most networks, the first address names the network itself and the last is a broadcast address for "everyone here", so a `/24` has 254 usable addresses. Cloud providers reserve a few more in every subnet for their own routers and DNS.

```csharp
// addresses.cs — run with: dotnet run addresses.cs
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// 1. An IPv4 address is 4 bytes; an IPv6 address is 16
var v4 = IPAddress.Parse("192.168.1.20");
var v6 = IPAddress.Parse("2001:db8::1");
Console.WriteLine($"{v4} is {v4.GetAddressBytes().Length} bytes");   // 192.168.1.20 is 4 bytes
Console.WriteLine($"{v6} is {v6.GetAddressBytes().Length} bytes");   // 2001:db8::1 is 16 bytes

// 2. Is an address inside a network? Keep only the network bits and compare
Console.WriteLine(InNetwork("192.168.1.20", "192.168.1.0", 24));    // True
Console.WriteLine(InNetwork("192.168.2.20", "192.168.1.0", 24));    // False: the third byte differs
Console.WriteLine(InNetwork("10.42.7.1", "10.0.0.0", 8));            // True
var privateBlock = IPNetwork.Parse("172.16.0.0/12");                 // .NET 8 and later do this for you
Console.WriteLine(privateBlock.Contains(IPAddress.Parse("172.20.5.9")));   // True: covers 172.16.0.0 to 172.31.255.255

// 3. How many addresses a prefix holds
foreach (int prefix in new[] { 8, 16, 24, 30, 32 })
    Console.WriteLine($"/{prefix}: {Math.Pow(2, 32 - prefix):N0} addresses");
// /8: 16,777,216  /16: 65,536  /24: 256  /30: 4  /32: 1

// 4. Special addresses
foreach (string text in new[] { "127.0.0.1", "10.1.2.3", "8.8.8.8", "169.254.10.20", "::1" })
    Console.WriteLine($"{text,-14} {Describe(IPAddress.Parse(text))}");
// 127.0.0.1      loopback: this machine itself
// 10.1.2.3       private: not routed on the public internet
// 8.8.8.8        public
// 169.254.10.20  link-local: no address was handed out
// ::1            loopback: this machine itself

// 5. This machine's own addresses
foreach (IPAddress mine in Dns.GetHostAddresses(Dns.GetHostName()))
    Console.WriteLine($"this machine: {mine} ({mine.AddressFamily})");

static bool InNetwork(string address, string network, int prefixLength)
{
    uint a = ToNumber(IPAddress.Parse(address)), n = ToNumber(IPAddress.Parse(network));
    // /24 means 24 one-bits followed by 8 zero-bits: 255.255.255.0.
    // Shifting a 32-bit value by 32 does nothing in C# (see 1.1), so /0 needs its own case.
    uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
    return (a & mask) == (n & mask);
}

static uint ToNumber(IPAddress ip) => BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());   // network byte order

static string Describe(IPAddress ip)
{
    if (IPAddress.IsLoopback(ip)) return "loopback: this machine itself";
    if (ip.AddressFamily == AddressFamily.InterNetwork)
    {
        byte[] b = ip.GetAddressBytes();
        if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
            return "private: not routed on the public internet";
        if (b[0] == 169 && b[1] == 254) return "link-local: no address was handed out";
    }
    return "public";
}
```

### Special addresses

A few ranges have fixed meanings, and you will meet them constantly.

The **loopback address** `127.0.0.1`, or `::1` in IPv6, always means "this machine". Traffic to it never leaves the computer. The name `localhost` points to it. The whole block `127.0.0.0/8` is reserved for loopback.

**Private addresses** are three ranges set aside for internal networks: `10.0.0.0/8`, `172.16.0.0/12` and `192.168.0.0/16`. Routers on the public internet refuse to carry them, so every home, office and cloud network can reuse the same ranges. Your laptop at home and a server in a cloud network may both be `10.0.0.5`, and never conflict, until the two networks are joined with a VPN.

**Link-local** addresses, `169.254.0.0/16` in IPv4 and addresses starting `fe80::` in IPv6, are what a machine gives itself when nothing hands it a real address. Seeing a `169.254` address usually means the network setup failed. Cloud providers also use `169.254.169.254` for a special metadata service that tells a virtual machine about itself.

`0.0.0.0` has two meanings. As a listening address for a server, it means "every interface on this machine". As a destination in a routing table, `0.0.0.0/0` means "every address": the default route.

### How a machine gets its address

When a device joins a network, it usually asks for an address using **DHCP**, the Dynamic Host Configuration Protocol. A DHCP server, often inside the home router or provided by the cloud platform, hands back an address to use for a while, a lease. It also supplies the network's prefix length, the address of the router to send outside traffic to, and the addresses of DNS servers, covered in DNS (6.4).

Servers often get fixed addresses instead, either configured by hand or reserved in DHCP, so that other machines can find them at the same address every time.

### Routing

A **router** is a device that connects several networks and forwards packets between them. Each router keeps a **routing table**: a list of network prefixes and, for each, which neighbouring router or interface to send matching packets to.

When a packet arrives, the router looks up its destination address. Several entries may match; the router picks the one with the longest prefix, the most specific match. This rule is **longest prefix match**. A packet for `10.1.2.3` matches both `10.0.0.0/8` and `10.1.2.0/24`, and goes where the `/24` entry says. Most tables end with a **default route**, `0.0.0.0/0`, for everything else.

Your own machine has a small routing table too. Its default route points at its **default gateway**, usually the home or office router, which is where every packet for another network goes first.

No router knows the whole path. Each one only chooses the next step. A packet from London to Sydney typically passes through 15 to 25 routers, each making its own decision.

To stop packets circling forever if routes loop, every IPv4 packet carries a **time to live** (TTL), set by the sender, typically to 64. Each router reduces it by one. A router that reduces it to zero drops the packet and sends back an error message. IPv6 calls the same field the hop limit.

The `traceroute` tool, `tracert` on Windows, uses this to map a path. It sends a packet with a TTL of 1, so the first router drops it and reports back, then a TTL of 2, then 3, until it reaches the destination. Each reply reveals one router on the way.

### Routing between networks

The internet is a network of networks. Each ISP, large company and cloud provider runs an **autonomous system**: a group of networks under one administration. Autonomous systems announce to their neighbours which address blocks they can reach, using the **Border Gateway Protocol**, or BGP. Each router's view of the internet is built from these announcements.

BGP relies heavily on trust and careful configuration. A mistaken announcement can make large parts of the internet send traffic to the wrong place, or nowhere. On 4 October 2021, a configuration change caused Facebook's routers to withdraw the announcements for its own networks, and Facebook, Instagram and WhatsApp vanished from the internet for about six hours.

### The last step: finding a machine on the local network

Routing gets a packet to the right network. On that final network, the packet still has to reach one specific network card, which is found by its MAC address, from Layers and packets (6.1).

IPv4 uses **ARP**, the Address Resolution Protocol, for this. A machine broadcasts "who has 192.168.1.30?", and the owner replies with its MAC address. The answer is cached for a while. IPv6 does the same job with a protocol called neighbour discovery.

### Sharing one public address

Most homes and offices have one public IPv4 address and many devices with private ones. **NAT**, network address translation, makes this work. When a device sends a packet out to the internet, the router replaces the private source address with its public one, and picks a free port number to tell the connections apart. It records the change in a table. When a reply comes back, it looks up the table and passes the reply to the right device.

NAT has side effects. Devices behind it can start connections outwards but can't receive new connections from outside, unless the router is told to forward a particular port, which is what "port forwarding" means. The translation table has a limited size, and idle entries expire, which can silently break long-lived idle connections. Mobile networks and some ISPs add another layer of NAT of their own. Cloud networks use NAT gateways so that private servers can reach the internet without being reachable from it.

### Firewalls

A **firewall** filters traffic by rules: allow or deny packets based on their addresses, ports and direction. Operating systems have one built in. Home routers have one. Cloud platforms call theirs security groups or network security groups, attached to each server or subnet.

The safe pattern is "deny by default": block everything, then allow only what is needed, such as port 443 from anywhere to the web servers, and port 5432 only from the web servers to the database. A connection that times out, rather than being refused, is often a firewall silently dropping packets, as TCP and UDP (6.3) explains.

### Addresses in the cloud and in containers

Cloud platforms give each customer private networks, called VPCs on AWS and VNets on Azure, with address ranges you choose from the private blocks. You divide them into subnets, often public ones for load balancers and private ones for servers and databases.

Containers get addresses too. Docker creates a private network on each host, by default `172.17.0.0/16`, and gives each container an address in it. Inside a container, `127.0.0.1` means the container itself, not the host. That is why a service in a container must listen on `0.0.0.0` to be reachable at all.

Plan address ranges before they are needed. Two networks using overlapping ranges, such as two offices both on `192.168.1.0/24`, or a cloud network that overlaps a partner's, can't be connected directly without renumbering or extra translation.

### In short

An IP address identifies a network interface: 32 bits for IPv4, which ran out, and 128 bits for IPv6. A prefix such as `/24` says how many leading bits name the network; a bitwise AND with the mask tests membership. Loopback means this machine, private ranges are for internal networks, and `0.0.0.0` as a listening address means every interface. Routers forward packets one hop at a time, choosing the most specific matching route, and time to live stops loops. NAT lets many private machines share one public address, and firewalls allow or deny traffic by rules.

### What goes wrong

**Listening on loopback in a container.** A service bound to `127.0.0.1` works inside its container and can't be reached from anywhere else. Bind to `0.0.0.0`, or configure ASP.NET Core's URLs accordingly.

**Overlapping address ranges.** Two networks that both use `10.0.0.0/16` can't be joined by a VPN or peering without renumbering one of them. Plan ranges across all networks that might one day connect.

**Hard-coded IP addresses.** Servers get new addresses when they are replaced, scaled or moved. Refer to them by name through DNS instead.

**Too-small subnets.** A cloud subnet of `/28` holds only 16 addresses, fewer after the provider's reservations, and runs out when containers or scaling need more. Size subnets for growth.

**Idle connections dropped by NAT.** Long-lived connections that sit silent for minutes, such as database connections, break without warning when NAT or firewall tables expire them. Use keepalives or pool settings that retire idle connections, as TCP and UDP (6.3) describes.

**Firewall rules that are too open.** A database port open to `0.0.0.0/0`, meaning the whole internet, is found by scanners within minutes. Allow only the specific sources that need it.

### Cost model

Each router adds a small delay, typically tens of microseconds to a millisecond, plus queuing when links are busy. A route across the world may pass 15 to 25 routers, but most of its latency still comes from distance. An ARP lookup costs one local round trip and is then cached for minutes. A DHCP lease takes a few round trips when a device joins a network. Public IPv4 addresses now cost real money: cloud providers charge for each one, which is one more reason to use private addresses behind NAT and load balancers.

### In other languages (optional)

**JavaScript and TypeScript.** Node.js has `net.isIP`, `net.isIPv4` and `net.isIPv6`, and `net.BlockList` for checking addresses against ranges. Browsers don't expose addresses to pages at all.

**Python.** The standard `ipaddress` module parses addresses and networks and does containment checks: `ipaddress.ip_address("10.1.2.3") in ipaddress.ip_network("10.0.0.0/8")`.

**Go.** The `net/netip` package provides compact address and prefix types with the same operations.

**Command line.** `ip addr` and `ip route` on Linux, and `ipconfig` and `route print` on Windows, show a machine's addresses and routing table. `traceroute` or `tracert` shows a path.

### Key terms

- **IP address**: The address of a network interface on an IP network.
- **IPv4**: The original Internet Protocol, with 32-bit addresses written like 192.168.1.20.
- **IPv6**: The newer Internet Protocol, with 128-bit addresses written like 2001:db8::1.
- **Network prefix**: The leading bits of an address that identify its network.
- **CIDR notation**: Writing a network as an address and a prefix length, such as 192.168.1.0/24.
- **Subnet mask**: A number with 1s for the network bits, such as 255.255.255.0 for /24.
- **Loopback address**: 127.0.0.1 or ::1, which always means this machine.
- **Private address**: An address from the ranges reserved for internal networks, not routed on the public internet.
- **DHCP**: The protocol a device uses to get an address and network settings when it joins a network.
- **Router**: A device that connects networks and forwards packets between them.
- **Routing table**: A router's list of network prefixes and where to send packets for each.
- **Longest prefix match**: The rule that a router uses the most specific matching route.
- **Default route**: The route for every destination not matched by anything more specific: 0.0.0.0/0.
- **Default gateway**: The router a machine sends packets to when they are for another network.
- **Time to live**: A counter in each packet, reduced by every router, that stops packets circling forever. Often shortened to TTL.
- **Autonomous system**: A group of networks run by one organization, such as an ISP or cloud provider.
- **BGP**: Border Gateway Protocol, which autonomous systems use to announce which addresses they can reach.
- **ARP**: Address Resolution Protocol, which finds the MAC address for an IPv4 address on a local network.
- **NAT**: Network address translation: rewriting addresses at a router so private machines can share a public address.
- **Firewall**: Software or hardware that allows or blocks traffic according to rules.

### Where this leads

Port numbers, and the connections that NAT and firewalls track, are in TCP and UDP (6.3). Turning names into these addresses is in DNS (6.4). Cloud networks, subnets and security groups come back in Cloud basics (11.5). Container networking is in Containers and Docker (11.3).

### Check yourself

1. By hand, work out the first and last address of `10.20.30.40/20`. Then check your answer by extending `addresses.cs`.
2. Add an IPv6 version of `InNetwork` that works on the 16 address bytes, and test it with `2001:db8::/32`.
3. Run `traceroute` (or `tracert`) to a server on another continent. Count the hops and spot where the latency jumps.
4. Find your machine's default gateway and DNS servers with `ipconfig /all` or `ip route` and `resolvectl status`.
5. Design address ranges for a cloud network with two public subnets, two private subnets and room for 500 containers, without overlapping a home network on `192.168.1.0/24`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A container that listened only to itself

The API runs in Docker and `curl localhost:8080` from the host gets "connection refused".

*Bind to all interfaces*

```docker
ENV ASPNETCORE_HTTP_PORTS=8080      # binds to all interfaces (.NET 8+ default form)
# not ASPNETCORE_URLS=http://localhost:8080 — loopback inside the container is unreachable from outside
EXPOSE 8080
```

> **Lesson:** Inside a container, 127.0.0.1 means the container itself. Listen on all interfaces and publish the port.

#### Example 2: Two VPCs that couldn't be peered

After an acquisition, both companies' networks use 10.0.0.0/16 and can't be connected.

*Plan address ranges up front*

```text
Company-wide plan (no overlaps):
  10.10.0.0/16  production, region A
  10.20.0.0/16  production, region B
  10.30.0.0/16  staging
  10.40.0.0/16  shared services
  each /16 split into /20 subnets: public (load balancers only), app, data
```

> **Lesson:** Overlapping private ranges can't be routed together. Allocate CIDR ranges centrally before you need them.

### How machines talk

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How machines talk</p>

<a id="be-m13"></a>

#### IP: addressing and routing

An IP address identifies a network interface: 4 bytes in IPv4 (`10.0.3.17`), 16 in IPv6. A packet carries a source and a destination, and every router along the way makes one decision — *which of my links gets this next?* — based on its routing table.

``` text
  your app ──► host ──► switch ──► gateway ──► ISP ──► ... ──► destination
                                    │
              each hop: look up the destination, forward, decrement TTL.
              No hop knows the full path. No hop remembers the packet.
```

**IP promises nothing.** It is explicitly best-effort: packets may be dropped when a queue is full, may arrive out of order because two packets took different paths, may be duplicated, and may be corrupted. Any guarantee you have comes from a layer above ([TCP: reliability built on unreliability](06-03-tcp-and-udp.md#be-m14)).

``` csharp
10.0.3.17/24
        └── the /24 means: the first 24 bits are the NETWORK, the rest is the host.
            So 10.0.3.0 - 10.0.3.255 is "my local network"; anything else
            is sent to the gateway to deal with.

Private ranges (not routable on the public internet):
  10.0.0.0/8      172.16.0.0/12      192.168.0.0/16
// Which is why your container, your VPC, and your laptop can all use 10.x
// simultaneously, and why NAT exists to translate between them.
```

> **\[Trap\]**
>
> **MTU and fragmentation.** Most links carry at most ~1500 bytes per packet. A larger IP packet must be fragmented and reassembled, and if any fragment is lost the whole packet is lost. Worse, a path with a smaller MTU somewhere in the middle — common with VPNs and tunnels — causes packets to be silently dropped if the "don't fragment" flag is set. The symptom is distinctive and confusing: small requests work perfectly, large ones hang. If a query works with 10 rows and times out with 10,000, suspect path MTU before suspecting the database.

### How Networks Work

<p class="src">From <em>Backend Engineering</em> · Backend Platform · How Networks Work</p>

<p class="pd">What actually happens between your code calling a URL and bytes arriving. Addressing, routing, DNS and TCP — the layer most backend developers use daily and have never been taught.</p>

<a id="be-g634"></a>

#### IP addressing, subnets and CIDR

Every private network you deploy into is defined by CIDR blocks, and getting them wrong causes problems that surface months later when you try to peer two networks together.

``` text
  10.0.4.37 / 22        an address, and how much of it is the NETWORK

  10.0.4.37   =  00001010 00000000 00000100 00100101
  /22         =  11111111 11111111 11111100 00000000   <- first 22 bits are network

  network     =  10.0.4.0
  broadcast   =  10.0.7.255
  usable      =  10.0.4.1  ->  10.0.7.254        (1022 addresses)

  the shortcut worth memorising:
    /24 = 256 addresses     /22 = 1024      /20 = 4096
    /25 = 128               /21 = 2048      /16 = 65536
    each step DOWN in prefix DOUBLES the size.
```

| Range                         | CIDR             | Purpose                                                           |
|-------------------------------|------------------|-------------------------------------------------------------------|
| 10.0.0.0 – 10.255.255.255     | `10.0.0.0/8`     | Private. 16.7M addresses — the usual choice for cloud VPCs        |
| 172.16.0.0 – 172.31.255.255   | `172.16.0.0/12`  | Private. **Docker's default bridge lives here**                   |
| 192.168.0.0 – 192.168.255.255 | `192.168.0.0/16` | Private. Home routers                                             |
| 127.0.0.0/8                   | `127.0.0.1`      | Loopback — never leaves the host                                  |
| 169.254.0.0/16                | —                | Link-local. `169.254.169.254` is the cloud metadata endpoint      |
| 0.0.0.0                       | —                | “All interfaces” when binding; “default route” in a routing table |

``` text
# what am I, and where do packets go?
ip addr                          # interfaces and their addresses
ip route                         # the routing table
ip route get 8.8.8.8             # "which interface and gateway for THIS destination?"

# the routing decision is: longest matching prefix wins
# 10.0.4.0/22   via eth0          <- more specific, chosen for 10.0.4.37
# 0.0.0.0/0     via 10.0.0.1      <- default: everything else
```

> **\[Trap\]**
>
> **Common trap.** Overlapping CIDR ranges are the classic cloud networking mistake and they are painful to fix. If your VPC is `10.0.0.0/16` and a company you acquire also uses `10.0.0.0/16`, the two networks can never be peered — routing is ambiguous. Allocate deliberately from a documented plan before you create the first VPC, leave gaps for growth, and record it somewhere the next person will find. This decision is effectively permanent.

###### Planning an address space you will not regret

``` csharp
# a workable allocation scheme, decided ONCE, before the first VPC exists

10.0.0.0/8                    the whole organisation
  10.0.0.0/12    ->  production      (10.0.0.0  - 10.15.255.255)
  10.16.0.0/12   ->  non-production  (10.16.0.0 - 10.31.255.255)
  10.32.0.0/12   ->  shared services (DNS, CI, monitoring)
  10.240.0.0/12  ->  RESERVED - acquisitions, partners, future regions

within production, one /16 per region:
  10.0.0.0/16    eu-west-1
  10.1.0.0/16    us-east-1

within a region, per-tier subnets across three AZs:
  10.0.0.0/20    public   (LB only)      -> /24 per AZ, room to grow
  10.0.16.0/20   private  (app)
  10.0.32.0/20   data     (databases)
  10.0.48.0/20   spare
```

| Rule                                    | Reason                                                                          |
|-----------------------------------------|---------------------------------------------------------------------------------|
| Never reuse a range in two environments | You will eventually need to peer them                                           |
| Leave gaps between allocations          | Growth happens in place, not at the end                                         |
| Reserve a large block you do not touch  | Acquisitions and partner links arrive without notice                            |
| Avoid `172.17.0.0/16`                   | Docker's default bridge — a VPC there breaks container networking on every host |
| Avoid `169.254.0.0/16`                  | Link-local; the cloud metadata service lives at `169.254.169.254`               |
| Size subnets generously                 | Resizing a subnet usually means recreating everything in it                     |
| Document it where it will be found      | The next person allocates from your plan or invents their own                   |

The Kubernetes footnote worth knowing: a cluster consumes address space fast, because most CNI plugins give every *pod* a real VPC address. A node running 30 pods holds 30 addresses. A `/24` subnet supports about eight such nodes, not 250 — which is how clusters run out of addresses at surprisingly small scale.

<a id="be-g635"></a>

#### Routing, NAT and how a private server reaches the internet

``` text
  a typical cloud network

                     internet
                        |
                  [ internet gateway ]
                        |
     +------------------+-------------------+
     |                                      |
  PUBLIC SUBNET  10.0.1.0/24         PRIVATE SUBNET  10.0.10.0/24
     |                                      |
  load balancer                        app servers      database
  NAT gateway  <------- outbound -----------+---------------+
                        traffic

  public subnet   = its route table has a route to the internet gateway
  private subnet  = it does not. That is the ONLY difference.

  a private server still needs outbound access (package updates, API calls),
  so its default route points at a NAT GATEWAY in the public subnet.
  NAT rewrites the source address, so replies come back - but nothing
  outside can INITIATE a connection inward.
```

| Term                       | Means                                                                   |
|----------------------------|-------------------------------------------------------------------------|
| **NAT**                    | Rewriting addresses so many private hosts share one public IP           |
| **SNAT**                   | Source NAT — outbound. What a NAT gateway does                          |
| **DNAT** / port forwarding | Destination NAT — inbound. What `docker -p 8080:80` does                |
| **Default route**          | `0.0.0.0/0` — where anything not otherwise matched goes                 |
| **Route table**            | The set of destination → next-hop rules attached to a subnet            |
| **Ephemeral port**         | The random high port (32768–60999 on Linux) a client uses as its source |

The consequence for design: **put nothing in a public subnet that does not need to receive inbound traffic from the internet.** In practice that is the load balancer and very little else. Application servers and databases belong in private subnets, reachable only from within the network.

``` text
# NAT gateways are metered per GB in most clouds, and this surprises people:
# pulling a 2 GB container image on every deploy across 20 instances is 40 GB
# of NAT traffic. Use VPC endpoints / Private Link for cloud services so that
# traffic never leaves the provider network or touches the NAT gateway at all.
```

###### Reading a routing table, and the SNAT port limit that bites at scale

``` text
# where does a packet to 10.1.2.3 actually go? Ask, do not guess.
ip route get 10.1.2.3
# 10.1.2.3 via 10.0.0.1 dev eth0 src 10.0.1.42 uid 1000
#           ^^^^^^^^^^ next hop    ^^^^ interface  ^^^^^^^^ source IP chosen

ip route show
# default via 10.0.0.1 dev eth0          <- everything not matched below
# 10.0.0.0/16 dev eth0 proto kernel scope link src 10.0.1.42
# 172.17.0.0/16 dev docker0              <- Docker's bridge

# LONGEST PREFIX WINS, always. /32 beats /24 beats /16 beats /0.
# so a 0.0.0.0/0 default route never overrides a more specific one -
# which is why adding a VPN route can silently blackhole one subnet.
```

``` text
  SNAT PORT EXHAUSTION - the scaling limit nobody plans for

  many private instances share ONE public IP through a NAT gateway.
  each outbound connection needs a unique tuple:

      (public IP, source port, destination IP, destination port)

  the source port range is ~64,000, and it is per DESTINATION endpoint.

    calling ONE service at one IP:port  -> ~64,000 concurrent connections
    across ALL instances behind that NAT. Not per instance. Total.

  so 50 instances each holding 1,500 connections to the same API =
  75,000 > 64,000, and new connections fail with a generic timeout
  that looks like the REMOTE service is broken.

  and TIME_WAIT makes it far worse: a closed connection holds its port
  for ~120 seconds by default. A service opening a new connection per
  request burns ports at the request rate.

  the fixes, in order of effect
    1  CONNECTION REUSE - keep-alive. This is the real fix, and it
       usually removes the problem entirely.
    2  more NAT public IPs - each adds another ~64,000
    3  a PRIVATE ENDPOINT / VPC endpoint - traffic never traverses NAT
    4  raise the port range, shorten TIME_WAIT - marginal
```

| Symptom                                            | Check                                                   |
|----------------------------------------------------|---------------------------------------------------------|
| Intermittent connection timeouts at high load only | NAT port allocation metrics; `ss -s` for TIME_WAIT      |
| One subnet unreachable after a VPN change          | `ip route get <ip>` — a more specific route won         |
| Works from the host, fails in a container          | The container's own route table and `/etc/resolv.conf`  |
| Asymmetric routing, dropped packets                | Return path differs — stateful firewalls drop the reply |
| Can reach by IP, not by name                       | DNS only. The path is fine                              |

### Networking

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Networking</p>

<p class="pd">The wire. Most production incidents that look like application bugs are really DNS, TLS, timeouts, connection pools or a proxy in the middle — so this is the layer worth genuinely understanding.</p>

<a id="be-a158"></a>

#### DNS, ports, NAT and firewalls

Name resolution is the most common invisible failure. .NET caches DNS at two levels: the OS resolver, and the connection pool inside `SocketsHttpHandler`, which is why `PooledConnectionLifetime` matters — without it a long-lived client keeps talking to an IP that was decommissioned an hour ago.

| Concept         | What to know                                                               |
|-----------------|----------------------------------------------------------------------------|
| A / AAAA record | Name → IPv4 / IPv6                                                         |
| CNAME           | Alias to another name; one extra lookup                                    |
| SRV             | Name → host + port; used by some service discovery                         |
| TTL             | How long resolvers may cache. Low TTL before a migration                   |
| Ephemeral ports | ~28k–64k range for outbound; exhaustible                                   |
| NAT             | Private IPs behind one public IP; breaks inbound and idles connections out |

##### Reading a connectivity failure

1.  **Name does not resolve** → DNS, a typo, or a service that does not exist in this namespace.
2.  **Connection refused** → you reached the host, nothing is listening on that port.
3.  **Connection timed out** → a firewall or security group is dropping packets silently.
4.  **TLS handshake failure** → certificate, hostname mismatch, or protocol version.
5.  **Connects, then hangs** → the app accepted but never responded; look at thread pool and locks.

> **\[Note\]**
>
> **Kubernetes DNS.** `service.namespace.svc.cluster.local` is the full form; `service` alone works within the same namespace. Slow DNS in a cluster is usually `ndots:5` in `resolv.conf` causing five failed lookups before the right one — a trailing dot on the hostname skips the search list.

###### DNS caching in .NET, and why failovers are not followed

``` text
  the failure that catches every team once

    a database or service fails over.
    DNS is updated within seconds.
    your application keeps connecting to the OLD address for HOURS.

  why: HttpClient's handler holds POOLED CONNECTIONS. A pooled connection
  was opened to a resolved IP, and it is reused. DNS is never consulted
  again for that connection - and an idle pool can hold connections for
  as long as the handler lives.

  a static HttpClient with a default handler holds them FOREVER.

  the fix is to bound the CONNECTION LIFETIME, which forces re-resolution:

    PooledConnectionLifetime = TimeSpan.FromMinutes(2)

  IHttpClientFactory does the equivalent by rotating handlers on a timer
  (HandlerLifetime, default 2 minutes).
```

``` csharp
// the three places a DNS change must be picked up, and how

// 1. HttpClient
new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }

// 2. database connections - the pool holds sockets too
"...;Connection Lifetime=1800"          // SQL Server: recycle after 30 min
"...;Connection Idle Lifetime=300"      // Npgsql

// 3. the .NET DNS cache itself (rarely the problem, but it exists)
AppContext.SetSwitch("System.Net.DisableIPv6", false);
// .NET caches resolutions briefly; the OS resolver caches by TTL.
// unlike the JVM, .NET does NOT cache DNS indefinitely by default -
// the pooled-connection issue above is the real cause in practice.
```

| Symptom                                     | Cause                                               | Check                                                   |
|---------------------------------------------|-----------------------------------------------------|---------------------------------------------------------|
| Works by IP, fails by name                  | DNS only — the path is fine                         | `dig name` from that host                               |
| Fails from a container, works on the host   | Container DNS config, or a private zone not visible | `cat /etc/resolv.conf` inside the container             |
| First request slow, rest fast               | Cold resolution, or the TLS handshake               | `curl -w` timing breakdown                              |
| Still hitting the old server after failover | Pooled connections, not DNS                         | Bound `PooledConnectionLifetime`                        |
| `AddressNotAvailable` under load            | Ephemeral port exhaustion                           | `ss -tan state time-wait | wc -l`                       |
| Intermittent timeouts to one dependency     | Firewall dropping idle connections silently         | TCP keep-alive shorter than the firewall's idle timeout |

> **\[Trap\]**
>
> **Common trap.** Stateful firewalls silently drop idle connections after a timeout — often 5 to 60 minutes — without sending a RST. Your pooled connection looks alive, you send on it, and the request hangs until your timeout fires. The fix is TCP keep-alive shorter than the firewall's idle window, or a connection lifetime shorter still. This presents as “the first request after a quiet period always fails”, which is a very recognisable signature once you have seen it.
