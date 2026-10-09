---
id: "14-4"
number: "14.4"
title: "Production diagnostics playbook"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 2560
minutes: 11
---

# 14.4 Production diagnostics playbook

> **Quick look.** A diagnostics playbook for common production symptoms.
>
> - p99 up, p50 normal: gen 2 GC pauses, lock contention or a hot-key stampede.
> - Memory climbs until OOM-killed: unbounded static caches, captive DbContext, unsubscribed events.
> - Low CPU but requests queue: thread pool starvation from sync-over-async.
> - "Timeout obtaining a connection from the pool": pool exhaustion (default 100), not a slow database.
> - Data flips between fresh and stale on refresh: per-instance IMemoryCache across pods.
> - Database CPU spikes after a deploy: cold-cache stampede.
> - Tools: dotnet-counters, dotnet-dump, dotnet-trace, curl -v, browser network timing; on a Linux box check the boring things first (disk, memory, deleted-but-open files).


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Low CPU, rising latency: a 10-minute diagnosis

p99 is climbing, CPU is 20%, and requests are queuing.

*The commands*

```bash
dotnet-counters monitor -p <pid> --counters System.Runtime[threadpool-queue-length,threadpool-thread-count,time-in-gc]
# queue length climbing, thread count creeping up 1–2 per second: starvation
dotnet-stack report -p <pid> | grep -A3 "GetResult\|Wait("       # find the blocking frames
dotnet-dump collect -p <pid>   # keep evidence for the postmortem
```

> **Lesson:** Low CPU with queued requests means threads are blocked, not busy. Look for sync-over-async in the stacks.

### Production Diagnostics Playbook

<p class="src">From <em>.NET Backend Deep Dive</em> · Production Diagnostics Playbook</p>

<a id="dn-54"></a>

#### The toolbox

> This is what separates "knows the theory" from "has actually fought a fire." It comes up constantly in senior interviews.

```bash
dotnet tool install -g dotnet-counters dotnet-dump dotnet-gcdump dotnet-trace dotnet-stack

dotnet-counters monitor -p <pid> System.Runtime Microsoft.AspNetCore.Hosting
dotnet-gcdump collect -p <pid>            # heap snapshot, opens in Visual Studio
dotnet-dump collect -p <pid>              # full dump
dotnet-dump analyze core_dump             # then: dumpheap -stat / gcroot / clrstack
dotnet-trace collect -p <pid> --profile cpu-sampling
dotnet-stack report -p <pid>              # where every thread is stuck
```

<a id="dn-55"></a>

#### Scenario A — p99 latency rises while p50 stays normal

| Step | What to do |
|---|---|
| 1 | Open OTel traces for the slowest requests — which span dominates? |
| 2 | If it's a DB span: count DB spans in one request → **N+1**? |
| 3 | If it's one slow query: capture the SQL, read the execution plan, check indexes |
| 4 | If there are "gaps" between spans: **GC pauses** or **thread pool queuing** |
| 5 | `dotnet-counters`: is `% time in GC` > 10%? Is `gen-2-gc-count` climbing? |

**Usual causes:** a healthy p50 with a bad p99 usually means blocking gen 2 collections, lock contention, or a cache miss on a hot key (stampede).

<a id="dn-56"></a>

#### Scenario B — memory climbs steadily until OOMKilled

```bash
dotnet-counters monitor -p 1 System.Runtime
# gc-heap-size grows and never drops after a gen-2 GC -> a real leak
# gc-heap-size oscillates -> not a leak, just a high allocation rate
```

```bash
dotnet-gcdump collect -p 1
# Compare two gcdumps 30 minutes apart -> which type's instance count grew?
```

**The usual suspects in a .NET web app:**

1. A `static` Dictionary/List used as a cache with no eviction.
2. A **captive dependency**: a `DbContext` held by a singleton, with an ever-growing change tracker.
3. Event handlers never unsubscribed (`obj.Event += Handler`) — the publisher keeps the subscriber alive.
4. `HttpClient` created repeatedly (sockets rather than heap, but the symptom looks similar).
5. Large strings/arrays landing on the **LOH** → fragmentation, RSS never returned to the OS.
6. Undisposed `IDisposable`s (especially a `CancellationTokenSource` with `CancelAfter`).

```csharp
// Inside dotnet-dump analyze:
> dumpheap -stat                     // which types dominate the heap
> dumpheap -mt <MethodTable>         // list instances
> gcroot <address>                   // WHO is holding it -> that's your bug
```

<a id="dn-57"></a>

#### Scenario C — low CPU but requests queue up

**Diagnosis: thread pool starvation.**

```bash
dotnet-counters monitor -p 1 System.Runtime
# threadpool-queue-length: high and rising
# threadpool-thread-count: rising very slowly (~1-2/sec)
# cpu-usage: low
```

```bash
dotnet-stack report -p 1 | grep -c "WaitOne\|Wait\|GetResult"
# Many threads parked in Task.Result / .Wait() / .GetAwaiter().GetResult()
```

**Fix:** find and remove every sync-over-async call. `SetMinThreads` only buys time.

<a id="dn-58"></a>

#### Scenario D — "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool"

This is **not** a slow database — it's **connection pool exhaustion** (default 100 connections per connection string).

Causes:

- `DbContext`/`SqlConnection` never disposed (missing `using`, or a scope that never closes).
- `Task.WhenAll` with `IDbContextFactory` and no concurrency limit → 500 requests × 3 = 1,500 connections.
- Transactions held too long (an HTTP call inside a transaction).
- Slow queries holding connections, so the pool can't recycle fast enough.

```sql
-- From the SQL Server side
SELECT s.session_id, s.login_time, s.status, r.wait_type, r.wait_time,
       t.text, s.program_name
FROM sys.dm_exec_sessions s
LEFT JOIN sys.dm_exec_requests r ON s.session_id = r.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE s.is_user_process = 1
ORDER BY r.wait_time DESC;
```

Don't reflexively raise `Max Pool Size` — that just moves the bottleneck into the database.

<a id="dn-59"></a>

#### Scenario E — data is sometimes fresh, sometimes stale, changing on refresh

Almost certainly **`IMemoryCache` across multiple instances**. Pod A invalidated; pod B didn't.

To confirm: call the API repeatedly and log `Environment.MachineName` alongside the data — you'll see the correlation with the pod.

**Fix:** move to `IDistributedCache`/`HybridCache`, or add a Redis pub/sub backplane to invalidate every pod's L1.

<a id="dn-60"></a>

#### Scenario F — database CPU spikes after a deploy, then settles

**Cold cache stampede.** The cache is empty post-deploy and all traffic hits the database.
**Fix:** warm hot keys at startup, slow the rolling deploy, or use a distributed cache so L2 survives pod restarts.

---

### Diagnostics

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Diagnostics</p>

<a id="be-b298"></a>

#### Symptom → cause

| Symptom                                             | Likely cause                                             |
|-----------------------------------------------------|----------------------------------------------------------|
| `KeyNotFoundException` on a key you just added      | mutable key — hash changed after insertion (I.8)         |
| Dictionary lookups slow, CPU high                   | bad `GetHashCode` — everything in one bucket             |
| Struct mutation silently doesn't apply              | defensive copy from a `readonly` field or `in` parameter |
| `InvalidOperationException` during `foreach`        | collection modified while enumerating                    |
| Query fast in dev, times out in production          | `IQueryable` became `IEnumerable` — filtering in memory  |
| Gen 2 / LOH growing                                 | arrays over 85 KB, or long-lived caches                  |
| Equality fails between identical-looking values     | reference equality on a class; no `Equals` override      |
| Sort throws "inconsistent results"                  | comparer isn't transitive/antisymmetric                  |
| Equal items reorder after sorting                   | `List.Sort` is unstable; use `OrderBy`                   |
| High allocation in a tight loop                     | boxing, LINQ pipelines, or `Substring`                   |
| String comparison behaves differently on one server | culture-sensitive comparison; use `Ordinal`              |
| Concurrent factory runs several times               | `GetOrAdd` doesn't deduplicate the factory (V.8)         |
| `NullReferenceException` on an `ImmutableArray`     | `default(ImmutableArray<T>)` — check `.IsDefault`        |

<a id="be-b299"></a>

#### Tools

- **BenchmarkDotNet** — the only credible way to compare implementations.
- **`dotnet-counters`** — live GC, gen sizes, allocation rate.
- **`dotnet-dump` + `dumpheap -stat`** — what's on the heap and what's holding it.
- **Visual Studio allocation profiler / PerfView** — allocation call stacks.
- **`ObjectLayoutInspector`** — prints actual struct layout including padding.
- **SharpLab (sharplab.io)** — see the exact code the compiler generates for an iterator, a record, or `async`. The fastest way to make Part VII concrete.

------------------------------------------------------------------------

### Diagnostics

<p class="src">From <em>Backend Engineering</em> · APIs · Diagnostics</p>

<a id="be-e554"></a>

#### Symptom → cause

| Symptom                                                 | Likely cause                                            |
|---------------------------------------------------------|---------------------------------------------------------|
| Intermittent `SocketException`, ports exhausted         | `new HttpClient()` per request                          |
| Works locally, fails after deploy, DNS recently changed | static `HttpClient`, no `PooledConnectionLifetime`      |
| Duplicate charges / records                             | non-idempotent `POST` + client retries                  |
| Outage recovers then immediately dies again             | retries without jitter (thundering herd)                |
| Slow list endpoint, fast individually                   | N+1 round trips                                         |
| Works for small accounts, times out for large           | unbounded collection, no pagination                     |
| Client sees stale data after a write                    | cached `GET`, no invalidation                           |
| 401 loops                                               | token expiry not handled; refresh flow broken           |
| Some clients break on a "non-breaking" change           | strict deserialization on the client                    |
| CORS errors only in the browser                         | preflight not handled, or credentials + wildcard origin |
| Random 502s under load                                  | upstream timeout shorter than downstream processing     |
| IDs corrupted in a JS client                            | 64-bit integers sent as JSON numbers                    |

<a id="be-e555"></a>

#### Tools

- **curl** — `-v` shows the full exchange, headers included. Still the fastest way to answer "what is actually being sent."
- **Browser devtools → Network** — timing breakdown per phase (DNS, TLS, TTFB) is diagnostic gold.
- **Postman / Bruno / .http files** — .NET's `.http` files live next to the code and belong in version control.
- **OpenTelemetry** — distributed traces are the only realistic way to debug a multi-hop call chain.
- **mitmproxy / Fiddler** — for inspecting traffic you don't control.

------------------------------------------------------------------------

### Running .NET on Linux

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Running .NET on Linux</p>

<p class="pd">The specific differences that catch Windows-trained .NET developers, and a diagnosis routine for when a service misbehaves on a machine you cannot attach a debugger to.</p>

<a id="be-k27"></a>

#### A diagnosis routine for a misbehaving service

``` text
start at the top and stop when something is wrong

1. is it running?          systemctl status myapp
                           journalctl -u myapp -n 100 --no-pager
2. is it listening?        ss -tulpn | grep dotnet
3. does it answer?         curl -sS localhost:5000/health
4. resources?              top / free -h / df -h        ← disk full is common
                           and boringly invisible
5. what is it doing?       ps -o pid,ppid,stat,%cpu,%mem,rss -p $(pgrep -f MyApp)
                           cat /proc/$(pgrep -f MyApp)/status | grep -i threads
                           ls /proc/$(pgrep -f MyApp)/fd | wc -l   ← fd leak?
6. what is it waiting on?  sudo strace -p PID -f -e trace=network -T
                           sudo lsof -p PID | tail
7. .NET-specific:          dotnet-counters monitor -p PID
                           dotnet-dump collect -p PID   → analyze → clrstacks
                           dotnet-gcdump collect -p PID
```

``` bash
# install the .NET diagnostic tools on the box (or use a sidecar container)
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-dump
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet-counters monitor -p $(pgrep -f MyApp) \
  --counters System.Runtime,Microsoft.AspNetCore.Hosting
# live: CPU, allocation rate, GC counts, thread-pool queue length,
# requests/sec, and the number currently in flight

# disk full is the most under-diagnosed outage cause on Linux
df -h                        # is a filesystem at 100%?
df -i                        # INODES can run out while space remains —
                             # millions of tiny files, and writes fail
du -sh /var/log/* | sort -h | tail
```

> **\[Trap\]**
>
> **Common trap.** An `rm` on a log file that a process still has open does not free the space — the inode survives until the process closes the descriptor, so `df` stays full while `du` shows nothing. Find them with `lsof | grep deleted`, and either restart the process or truncate instead: `: > /var/log/app.log`.

### The Linux Server

<p class="src">From <em>Backend Engineering</em> · Backend Platform · The Linux Server</p>

<p class="pd">The operating system your code actually runs on. Processes, permissions, service management, resource limits and the diagnostic commands that answer “why is this box unhealthy?”</p>

<a id="be-g659"></a>

#### Diagnosing a sick server

A method that works regardless of what the application is. Roughly the sequence a competent operator runs in the first sixty seconds.

``` csharp
uptime                    # load averages: 1, 5, 15 minutes
                          # compare to core count. Load 8 on 4 cores = queueing.
                          # NOTE: on Linux, load includes UNINTERRUPTIBLE (disk) waits,
                          # so high load with low CPU means I/O, not compute.

top    # or htop
       # %us user  %sy system  %wa IO-WAIT  %id idle  %st STEAL
       #   high %wa  -> disk bound
       #   high %sy  -> syscall/context-switch heavy
       #   high %st  -> the HYPERVISOR is taking your CPU (noisy neighbour)

free -h                   # 'available' is the number that matters, not 'free'
                          # Linux uses spare RAM for page cache - that is correct

df -h                     # disk space
df -i                     # INODES - can exhaust with space remaining
du -sh /var/log/*         # what is consuming it

iostat -xz 5              # per-device: %util, await (latency), r/s w/s
                          # await climbing = storage is the bottleneck

vmstat 1                  # si/so non-zero = SWAPPING. Very bad for a database.
ss -s                     # socket summary
```

| Observation                      | Likely cause                                | Next step                                           |
|----------------------------------|---------------------------------------------|-----------------------------------------------------|
| High load, low CPU               | I/O wait or lock contention                 | `iostat -xz`, check `await`                         |
| High `%st` (steal)               | Noisy neighbour on shared hardware          | Move instance, or use dedicated capacity            |
| Memory ‘full’ but available high | Page cache — normal and healthy             | Nothing                                             |
| Swapping (`si`/`so` non-zero)    | Genuine memory pressure                     | Add RAM or reduce usage; disable swap for databases |
| Disk full with files deleted     | A process still holds the deleted file open | `lsof +L1`, then restart it                         |
| Inodes exhausted                 | Millions of small files                     | Find and clear — often a session or cache directory |
| Process gone, no logs            | OOM kill                                    | `dmesg -T | grep -i oom`                            |
| Latency high, CPU under limit    | cgroup CPU throttling                       | `cat /sys/fs/cgroup/cpu.stat`                       |

``` csharp
# going deeper on one process
lsof -p <pid>              # every open file and socket
lsof -i :5432              # who is using this port
strace -p <pid> -f -e trace=network    # syscalls, live (slows the process)
cat /proc/<pid>/status     # memory, threads, context switches
pidstat -p <pid> 1         # per-process CPU/IO over time

# find the actual disk hog quickly
du -x --max-depth=1 / | sort -rh | head
ncdu /var                  # interactive, if available
```

> **\[Note\]**
>
> **Check the boring things first.** Disk full, certificate expired, out of memory, out of file descriptors, and clock skew account for a large share of “the server is broken” incidents. All five take seconds to rule out and are frequently skipped in favour of something more interesting.

###### The sixty-second triage, as a script

``` text
#!/usr/bin/env bash
# what a competent operator checks first, in order. Run this before theorising.
echo "=== load / uptime ==="; uptime; nproc
echo "=== memory ===";        free -h
echo "=== swap activity ==="; vmstat 1 3 | tail -2      # si/so must be 0
echo "=== disk space ===";    df -h | grep -vE 'tmpfs|udev'
echo "=== inodes ===";        df -i | grep -vE 'tmpfs|udev'
echo "=== disk latency ==="   ; iostat -xz 2 2 | tail -20
echo "=== top CPU ===";       ps aux --sort=-%cpu | head -6
echo "=== top MEM ===";       ps aux --sort=-%mem | head -6
echo "=== recent OOM ===";    dmesg -T | grep -i -E 'oom|killed process' | tail -5
echo "=== failed units ===";  systemctl list-units --failed --no-legend
echo "=== sockets ===";       ss -s
echo "=== time sync ===";     timedatectl | grep -E 'synchronized|NTP'
echo "=== certs ===";         # expiry on anything this host serves
```

| Check                    | Why it is in the list                                                                                              |
|--------------------------|--------------------------------------------------------------------------------------------------------------------|
| Load vs core count       | Distinguishes queueing from busy                                                                                   |
| Swap activity            | Any swapping on a database host is a serious problem                                                               |
| Inodes as well as space  | Millions of small files exhaust inodes with space remaining                                                        |
| Disk `await`             | The number that reveals storage as the bottleneck                                                                  |
| Recent OOM kills         | Explains a restart with no application logs                                                                        |
| Failed systemd units     | Something crashed that nobody noticed                                                                              |
| **Time synchronisation** | Clock skew breaks TLS, JWT validation, TOTP, log correlation and distributed tracing — and is almost never checked |

Clock skew deserves its place on that list. A host whose clock has drifted by more than a few minutes will reject valid certificates, fail JWT `nbf`/`exp` checks, produce logs that cannot be correlated with other hosts, and break any signature scheme with a timestamp. It presents as a dozen unrelated application bugs.
