---
id: "11-9"
number: "11.9"
title: "Windows Server and IIS"
part: 11
part_title: "Shipping software"
words: 2115
minutes: 9
---

# 11.9 Windows Server and IIS

> **Quick look.** Running .NET on IIS and Windows Server, and modernizing it.
>
> - IIS hosts apps in application pools; the recycle is the state boundary.
> - Default recycling (every 29 hours, config edits, memory limits) drops in-memory state and sessions.
> - Set debug="false" in production and use Application Initialization to warm up after recycles.
> - Windows equivalents: PowerShell remoting, scheduled tasks, event logs.
> - Modernize in steps: monitoring, automated deployment and central logs first, porting later.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Sessions lost every night at 2 am

Users of a legacy app are logged out daily. The IIS app pool recycles every 1,740 minutes and sessions are in-process.

*Move state out, recycle on purpose*

```markup
<!-- web.config: session out of the worker process -->
<sessionState mode="SQLServer" sqlConnectionString="..." timeout="30" />

<!-- applicationHost / IIS Manager: replace the 29-hour timer with a fixed quiet time -->
<recycling><periodicRestart time="00:00:00"><schedule><add value="03:30:00" /></schedule></periodicRestart></recycling>
```

> **Lesson:** An app pool recycle wipes in-process state. Store session elsewhere and schedule recycles deliberately.

#### Example 2: The slow first request after a recycle

The first user after each recycle waits 20 seconds while the app compiles and warms caches.

*Application Initialization*

```markup
<system.webServer>
  <applicationInitialization doAppInitAfterRestart="true">
    <add initializationPage="/health/warmup" />
  </applicationInitialization>
</system.webServer>
<!-- and set the app pool's startMode="AlwaysRunning", site preloadEnabled="true" -->
```

> **Lesson:** Warm the application after recycles so users never pay for start-up.

### Windows Server & IIS

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Windows Server & IIS</p>

<p class="pd">The Windows side of the platform, which most infrastructure material ignores — and which matters if you run .NET Framework, IIS, or anything inherited.</p>

<a id="be-g696"></a>

#### IIS: the model, and how it differs from Linux hosting

``` text
  the IIS hosting model

  HTTP.SYS               a KERNEL-MODE driver. Listens, queues requests,
                         does kernel-mode response caching, and routes by
                         host header + port + path to the right site.
                         -> this is why two applications can share port 443
                            without a userland proxy, and why an app pool
                            crash does not drop the connection.
    |
  WAS                    Windows Process Activation Service: starts and
                         recycles worker processes on demand.
    |
  APPLICATION POOL       a boundary of isolation: identity, .NET version,
                         recycling policy, resource limits.
    |
  w3wp.exe               the worker process. One per pool (or more, in a
                         "web garden"). Your application runs here.
```

| IIS concept                     | Linux equivalent             | Note                                                       |
|---------------------------------|------------------------------|------------------------------------------------------------|
| Application pool                | systemd service / container  | The isolation and recycling boundary                       |
| `w3wp.exe`                      | Your process                 | One per pool by default                                    |
| `applicationHost.config`        | nginx.conf                   | Machine-wide server configuration                          |
| `web.config`                    | Per-app config + `.htaccess` | Per-application, and inherited down the path tree          |
| Site / binding                  | server block                 | Host header, port, IP, certificate                         |
| Virtual directory / application | location block               | A path mapped to a folder, possibly its own app            |
| HTTP.SYS                        | The kernel socket layer      | Kernel-mode queuing and caching — no direct Linux analogue |

``` sql
:: the modern command line - appcmd is legacy, PowerShell is current
Import-Module WebAdministration
Get-IISSite
Get-IISAppPool
Restart-WebAppPool -Name "MyAppPool"

:: what is actually running, and in which pool
Get-WmiObject Win32_Process -Filter "name='w3wp.exe'" |
  Select-Object ProcessId, @{n='Pool';e={($_.CommandLine -split '-ap "')[1] -split '"'}}

:: the request queue - the equivalent of "is nginx backed up?"
Get-Counter '\HTTP Service Request Queues(*)\CurrentQueueSize'
Get-Counter '\W3SVC_W3WP(*)\Requests / Sec'
```

> **\[Why it matters\]** IIS is not going away in enterprise .NET, and the mapping matters in both directions: it explains why a familiar app pool recycle has no container equivalent, and it makes a migration to Linux hosting a translation exercise rather than a rewrite.

<a id="be-g697"></a>

#### Application pools, recycling and the classic failure modes

Most IIS operational problems come from application pool behaviour, and most of that behaviour is configured by defaults that made sense in 2008.

| Default                               | Effect                                                                                            | Usually should be                                                           |
|---------------------------------------|---------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------|
| **Idle Time-out: 20 min**             | The pool shuts down after 20 minutes with no requests — the next user waits for a cold start      | `0` for an API, with Always On                                              |
| **Regular Time Interval: 1740 min**   | Recycles every 29 hours, at an unpredictable time of day                                          | `0`, plus a specific scheduled time                                         |
| **Overlapped recycle: on**            | New process starts before the old finishes — good, unless the app cannot tolerate two instances   | Keep on; be aware of it for singletons and file locks                       |
| **Rapid-Fail Protection**             | 5 crashes in 5 minutes disables the pool entirely — **503 for everything until manually started** | Keep, and *alert* on it                                                     |
| **Ping enabled**                      | Kills a worker that does not respond to a health ping in 90s                                      | Raise for legitimately slow startup                                         |
| **Identity: ApplicationPoolIdentity** | A virtual per-pool account                                                                        | Correct. Grant it access, do not switch to a service account without reason |
| **Load User Profile: false**          | No user profile — breaks DPAPI, some certificate and temp-file operations                         | `true` if you use certificates or DPAPI                                     |

``` text
:: pool settings that matter, set explicitly rather than inherited
Set-ItemProperty IIS:\AppPools\MyAppPool -Name processModel.idleTimeout -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\MyAppPool -Name recycling.periodicRestart.time -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\MyAppPool -Name processModel.loadUserProfile -Value $true
Set-ItemProperty IIS:\AppPools\MyAppPool -Name recycling.periodicRestart.schedule -Value @{value="03:00:00"}

:: Application Initialization - warm the app so the first real user does not pay
Set-ItemProperty IIS:\AppPools\MyAppPool -Name startMode -Value AlwaysRunning
Set-ItemProperty "IIS:\Sites\MySite" -Name applicationDefaults.preloadEnabled -Value $true
```

| Symptom                                         | Cause                                                                    |
|-------------------------------------------------|--------------------------------------------------------------------------|
| First request after quiet period is slow        | Idle time-out shut the pool down                                         |
| Intermittent loss of in-memory session or cache | A recycle occurred — recycles are silent by default                      |
| 503 Service Unavailable, pool stopped           | Rapid-Fail Protection triggered after repeated crashes                   |
| Works for one user, fails for another           | Pool identity lacks permission on a path or certificate                  |
| Random crash with no application log            | Unhandled exception on a background thread — check the Windows Event Log |
| Memory climbs until recycle                     | A leak the recycle was masking; the recycle is not the fix               |
| Slow under load, CPU low                        | Thread-pool starvation, usually `.Result` on async code                  |

> **\[Trap\]**
>
> **Common trap.** Application pool recycling silently destroys in-process state: in-memory session, cached data, background timers and anything else held in the worker process. Teams frequently rely on recycling to mask a memory leak, which works until traffic grows enough that recycles happen during business hours. Treat a required recycle as a bug to investigate, and move session state out of process regardless.

###### Reading the request queue, and the recycle that hides a leak

``` text
# the counters that answer "is it IIS, or is it my app?"
Get-Counter '\HTTP Service Request Queues(*)\CurrentQueueSize'
Get-Counter '\HTTP Service Request Queues(*)\RejectedRequests'
Get-Counter '\W3SVC_W3WP(*)\Requests / Sec'
Get-Counter '\ASP.NET Applications(*)\Requests In Application Queue'
Get-Counter '\Process(w3wp*)\Private Bytes'
Get-Counter '\.NET CLR Memory(w3wp*)\% Time in GC'

# and the single most useful command on a hanging IIS box: what is
# actually executing RIGHT NOW, with how long each request has been running
C:\Windows\System32\inetsrv\appcmd list requests /elapsed:10000
# every request running longer than 10 seconds, with its URL and state.
# "state: ExecuteRequestHandler" for 300 seconds = your code is stuck.
```

``` text
  WHERE A REQUEST WAITS, and each queue means something different

    client
      |
    HTTP.SYS (kernel)  --> the KERNEL queue, per app pool
      |                    default length 1000. Full -> 503 immediately,
      |                    and RejectedRequests increments.
      |                    a growing kernel queue means the WORKER PROCESS
      |                    is not picking work up - it is stuck or dead.
      v
    w3wp.exe worker
      |
    CLR THREAD POOL   --> "Requests In Application Queue"
      |                    growing here means your code is BLOCKING
      |                    threads - almost always sync-over-async.
      v
    your handler

  so: kernel queue growing  -> the process is wedged, or recycling
      app queue growing     -> thread starvation inside the process
      neither growing, slow -> a downstream dependency
```

| Default               | Value             | Why it causes trouble                                                                                              |
|-----------------------|-------------------|--------------------------------------------------------------------------------------------------------------------|
| Idle timeout          | 20 min            | The pool **shuts down** when idle. The next request pays full startup — and any in-memory cache or session is gone |
| Regular time interval | 1740 min (29 h)   | A recycle at an unpredictable hour, drifting daily                                                                 |
| Overlapped recycle    | Enabled           | **Two processes run briefly** — timers double-fire, singletons exist twice                                         |
| Rapid-fail protection | 5 crashes / 5 min | The pool is **disabled** until a human re-enables it                                                               |
| Queue length          | 1000              | Silent 503s once exceeded                                                                                          |
| Max worker processes  | 1                 | A web garden breaks in-process session and in-memory cache                                                         |

> **\[Note\]**
>
> **The recycle that hides a leak.** A daily recycle makes a memory leak invisible — private bytes climb for 29 hours, the pool restarts, and the graph resets before anyone notices. Teams then discover the leak only when traffic grows enough that it exhausts memory *within* the recycle window. Set a fixed recycle time so it is predictable, monitor private bytes *across* recycles, and treat a rising baseline as a bug rather than as normal.

<a id="be-g698"></a>

#### Windows Server operations

| Task                     | Windows                                  | Linux equivalent     |
|--------------------------|------------------------------------------|----------------------|
| Service management       | `Get-Service`, `sc.exe`                  | `systemctl`          |
| Logs                     | Event Viewer, `Get-WinEvent`             | `journalctl`         |
| Scheduled work           | Task Scheduler, `Register-ScheduledTask` | systemd timers, cron |
| Remote shell             | WinRM, PowerShell Remoting, SSH          | SSH                  |
| Package management       | `winget`, Chocolatey                     | `apt`, `yum`         |
| Performance counters     | `Get-Counter`, PerfMon                   | `/proc`, `iostat`    |
| Configuration management | DSC, Ansible (WinRM), Group Policy       | Ansible, Puppet      |
| Firewall                 | Windows Defender Firewall                | `nftables`           |

``` text
# PowerShell Remoting - the SSH equivalent for fleet operations
$s = New-PSSession -ComputerName web01,web02,web03
Invoke-Command -Session $s { Get-Service MyService | Select MachineName,Status }
Invoke-Command -Session $s { Restart-WebAppPool MyAppPool }

# structured event log queries - far better than clicking through Event Viewer
Get-WinEvent -FilterHashtable @{
    LogName   = 'Application'
    Level     = 2                      # 2 = Error
    StartTime = (Get-Date).AddHours(-1)
} | Select-Object TimeCreated, ProviderName, Id, Message | Format-List

# the .NET-specific ones worth knowing
Get-WinEvent -LogName 'Application' -FilterXPath "*[System/Provider/@Name='.NET Runtime']"
Get-WinEvent -LogName 'System' -FilterXPath "*[System/Provider/@Name='Microsoft-Windows-WAS']"
# WAS logs pool start, stop and rapid-fail events - the answer to
# "why did the site return 503 at 4am?"
```

``` text
# capturing a hang or a crash for real analysis
# procdump: the tool to know
procdump -ma -e -w w3wp.exe C:\dumps       # on unhandled exception
procdump -ma -h w3wp.exe C:\dumps          # on window hang
procdump -ma -c 90 -s 10 -n 3 w3wp.exe C:\dumps   # CPU >90% for 10s, 3 times

# then analyse with dotnet-dump / WinDbg
dotnet-dump analyze dump.dmp
> clrthreads          # what every thread is doing
> dumpheap -stat      # what is on the heap, by type
> gcroot <address>    # what is keeping this object alive
```

> **\[Why it matters\]** Most .NET shops run at least one Windows host, and the operational vocabulary differs enough that Linux muscle memory silently fails. Knowing the equivalents — and that PowerShell remoting, scheduled tasks and event logs are the counterparts you already understand — is what lets you support both without treating one as a mystery.

<a id="be-g699"></a>

#### Modernising a Windows deployment

``` text
  the realistic path off IIS on Windows Server, in order of effort

  1  KEEP IIS, improve operations
       PowerShell DSC or Ansible for configuration
       Web Deploy or a pipeline instead of manual copies
       proper monitoring, structured logs shipped off the box
       -> a lot of the benefit, none of the risk

  2  IIS behind a modern edge
       cloud load balancer terminating TLS, WAF at the edge
       IIS becomes an origin, not the front door
       -> certificate management and rate limiting move to a better place

  3  WINDOWS CONTAINERS
       .NET Framework apps CAN be containerised, on Windows nodes.
       large images (GB), Windows node pools, and the isolation model
       is heavier. Works, and rarely worth it as a destination.

  4  PORT TO .NET (modern) AND LINUX CONTAINERS
       the actual destination. Kestrel behind nginx or an ingress,
       Linux nodes, small images, the whole ecosystem in this guide.
       -> incremental: strangler-fig, service by service
```

| Blocker to porting              | Options                                                                          |
|---------------------------------|----------------------------------------------------------------------------------|
| System.Web dependency           | The main work. Rewrite to ASP.NET Core hosting                                   |
| WCF services                    | CoreWCF, or re-expose as gRPC/HTTP                                               |
| Windows-specific APIs           | Registry, WMI, COM — find a cross-platform path or isolate behind an interface   |
| Windows Authentication          | Negotiate is supported on Linux with a keytab; or move to OIDC                   |
| MSMQ                            | Replace with a real broker — usually the right change anyway                     |
| Legacy third-party libraries    | The most common hard blocker. Check target framework support early               |
| Crystal Reports, Office interop | Server-side Office automation is unsupported anyway; replace with a real library |

> **\[Note\]**
>
> **Step 1 is where the value is.** Most of the operational pain in a Windows estate — no monitoring, manual deployments, silent recycles, configuration drift, logs stranded on the box — is fixable without porting anything. Do that first. It is weeks rather than quarters, it makes the platform observable, and it makes any subsequent migration measurable rather than hopeful.
