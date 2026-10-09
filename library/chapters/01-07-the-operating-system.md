---
id: "1-7"
number: "1.7"
title: "The operating system"
part: 1
part_title: "The machine"
words: 7327
minutes: 32
---

# 1.7 The operating system

> **Quick look.** How the operating system shares the machine, and the everyday behaviour that follows from it.
>
> - Programs ask the kernel for everything through system calls, which cross a costly boundary.
> - A process has its own private memory; threads inside it share that memory.
> - Programs communicate through arguments, environment variables, standard streams and exit codes.
> - Services must handle SIGTERM to shut down cleanly; exit code 137 means the process was killed (often for memory).
> - A container is an ordinary process with isolation and limits; a VM runs a whole separate OS.
> - Container memory limits kill the process rather than slowing it down.


### The big picture

Many programs run on one computer at once, all wanting the processor, memory, disk and network. The operating system shares these out and keeps programs from interfering with each other. It is also the layer your code talks to whenever it opens a file, starts a thread or sends data over a network.

Understanding it explains a lot of everyday behaviour. It explains why a container exits with code 137, and why a service must handle shutdown signals. It shows how configuration reaches an app through environment variables, and why a busy server fails with "too many open files". This chapter ends Part 1 by connecting the hardware to the programs that run on it.

### Before you start

**Builds on:** 1.6 Storage and files, 1.4 The processor.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Thread pool** (5.2): A managed set of reusable worker threads that run queued work items.
- **Packet** (6.1): One small unit of data sent over a network, with a header and a payload.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Command injection** (10.4): Injection into an operating system command, letting an attacker run other commands.
- **Kubernetes** (11.3): The dominant container orchestrator, which reconciles declared desired state across a cluster.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

**The code uses:** variables and var, string interpolation, null operators, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between a process and a thread?**

> A process is a running program with its own private memory. Threads run inside a process. They share the process's memory, and each has its own stack.

**2. What is a system call?**

> A request from a program to the operating system kernel, such as "open this file" or "send these bytes". It is the only way a normal program can touch hardware.

**3. A container exits with code 137. What most likely happened?**

> It was killed with SIGKILL, because 137 is 128 + 9. The usual causes are the out-of-memory killer or a stop that ran past its grace period.

**4. What does docker stop actually do to your program?**

> It sends SIGTERM and waits, 10 seconds by default. If the program is still running, it sends SIGKILL, which cannot be caught.

**5. Why can reading a child process's output hang forever?**

> Pipes have a small buffer. If you wait for all of standard output while the child fills its standard error pipe, the child blocks writing and you block reading. Read both streams at the same time.

**6. Which clock should you use to measure how long something took?**

> A monotonic clock such as `Stopwatch`. Wall-clock time from `DateTime.Now` or `UtcNow` can jump when the system clock is corrected.

**7. A busy server starts failing with "Too many open files". What limit did it hit?**

> The per-process limit on file descriptors. Every open file and every network connection uses one.

**8. How do you set the .NET setting Logging:LogLevel:Default from an environment variable?**

> Name the variable `Logging__LogLevel__Default`. A double underscore stands for the colon, because colons aren't allowed in variable names everywhere.

### What the operating system is for

Many programs run on one machine at the same time. They all want the processor, memory, disk and network. The **operating system** (OS) shares these out fairly. It also protects programs from each other, so one buggy program can't corrupt another.

The core of the OS is the **kernel**. It is the one piece of software with full control of the hardware.

### User mode and kernel mode

The processor has at least two privilege levels. The kernel runs in **kernel mode**, where every instruction is allowed. Your programs run in **user mode**. There, instructions that touch hardware directly, or reach memory that isn't yours, are refused.

### System calls

A program in user mode can't read the disk or open a network connection itself. It asks the kernel with a **system call**. Examples include open a file, read bytes, write bytes, create a thread and allocate memory.

A system call switches the processor into kernel mode and back. That switch has a cost, from about a hundred nanoseconds to a few microseconds. It is small, but it adds up when you make one call per byte. That is why programs **buffer**: they collect data and make one call for many bytes. The `StreamWriter` in Storage and files (1.6) is doing exactly this.

### Processes

A **process** is a running instance of a program. The OS gives each process its own private memory, a number called the **process id** (PID), and a table of open resources. On Linux these resources are **file descriptors**. On Windows they are **handles**. Open files and network connections both appear in this table.

Processes are isolated from each other. One process can't read another's memory. If a process crashes, the others keep running.

### Virtual memory

Each process sees its own address space, starting at zero. These are **virtual addresses**. The processor and the kernel translate them into real **physical** addresses, using a **page table** kept per process.

Memory is managed in fixed-size **pages**, usually 4 KB. A virtual page can be in RAM, not yet created, or moved out to disk. Touching a page that isn't in RAM causes a **page fault**. The kernel then fetches or creates the page and lets the program continue.

This gives three useful properties. Processes are isolated, because their page tables point at different physical memory. Memory is only really used when touched, so a process can reserve far more than it uses. And files can be **memory-mapped**, making a file look like a range of memory.

When physical memory runs out, the OS can move rarely used pages to disk, called **swap**. Everything then slows down badly. On Linux, when memory is truly exhausted, the **OOM killer** (out of memory) picks a process and kills it.

### Threads

A **thread** is one path of execution through a program. A process starts with one thread and can create more. Threads in the same process share its memory. Each thread has its own stack and its own set of register values.

The OS **scheduler** decides which thread runs on which core. It gives each thread a short time slice, a few milliseconds, then may switch to another. Saving one thread's registers and loading another's is a **context switch**. It costs a few microseconds.

Threads are not free. Each one reserves stack memory and adds scheduling work. That is why .NET keeps a **thread pool** of reusable threads, and why async code, covered in Async and await (5.3), avoids holding a thread while it waits.

### Scheduling in more detail

Modern operating systems are **preemptive**. A thread doesn't have to give up the processor voluntarily. The timer interrupt described in The processor (1.4) hands control back to the kernel, and the scheduler may switch to another thread.

Threads have **priorities**. Higher-priority threads run first. A thread that never gets to run because others always go first is **starved**. You can also pin a thread or process to particular cores, which is called **affinity**. It is rarely worth doing by hand.

A thread that is waiting, for a disk read, a network packet or a lock, uses no processor time at all. The scheduler parks it until the thing it waits for happens. That is why a server can have hundreds of threads and still show low CPU use, and why hundreds of waiting threads are still a waste of memory.

In a container, `Environment.ProcessorCount` reports the container's CPU limit rather than the host's core count. .NET sizes its thread pool and garbage collector from that number.

### Talking to the outside world

Every process starts with some standard connections to the world around it.

**Arguments** are the words typed after the program name. **Environment variables** are named text values inherited from whatever started the process. They are the standard way to pass configuration into containers.

Three standard streams are always open. **stdin** is input. **stdout** is normal output. **stderr** is error output. In containers, anything written to stdout and stderr becomes the container's logs.

When a process ends, it returns an **exit code**. Zero means success. Anything else means failure. Build scripts, CI pipelines and orchestrators all decide what happened from this number.

| Exit code | Usual meaning |
|---:|---|
| 0 | Success |
| 1 | General failure chosen by the program |
| 134 | Aborted (SIGABRT). Often an unhandled .NET exception on Linux |
| 137 | Killed (SIGKILL). Often out of memory or a stop timeout |
| 139 | Crashed on invalid memory access (SIGSEGV) |
| 143 | Terminated (SIGTERM) without handling it |

Codes above 128 follow a rule: 128 plus the signal number.

### Configuration from the environment

.NET reads environment variables into its configuration system automatically. Configuration keys use colons, as in `Logging:LogLevel:Default`. Environment variable names can't contain colons on every platform, so a double underscore stands in: `Logging__LogLevel__Default=Warning`. The variable `DOTNET_ENVIRONMENT`, or `ASPNETCORE_ENVIRONMENT` for web apps, picks which `appsettings.{Environment}.json` file loads.

Environment variables are easy to leak. Child processes inherit them. `docker inspect` shows them. Crash dumps contain them. Keep real secrets in a secret store where you can, as covered in Secrets and cryptography basics (10.5).

### Shells and PATH

A **shell** is a program that reads commands and starts other programs. Bash and zsh are common on Linux and macOS. PowerShell and the older `cmd.exe` are common on Windows.

When you type a command name, the shell looks for a program with that name in each folder listed in the `PATH` environment variable, in order. The first match wins. "Command not found" means none of those folders has it.

A shell can connect programs together. `a | b` sends the standard output of `a` into the standard input of `b`, through a **pipe**. A pipe is a small in-memory buffer managed by the kernel. `> file` sends output to a file. `2>` redirects standard error.

### Starting other processes

Your program can start other programs too. The started program is a **child process**. It inherits its parent's environment variables and working folder unless you change them.

```csharp
// child.cs — run with: dotnet run child.cs
using System.Diagnostics;

var psi = new ProcessStartInfo("dotnet")
{
    ArgumentList = { "--version" },   // each argument passed separately: no quoting problems
    RedirectStandardOutput = true,    // capture what the child prints
    RedirectStandardError = true,
    UseShellExecute = false,          // start the program directly, not through a shell
};
using var child = Process.Start(psi)!;

// Read both pipes at once. Reading one to the end first can deadlock.
Task<string> stdout = child.StandardOutput.ReadToEndAsync();
Task<string> stderr = child.StandardError.ReadToEndAsync();
await Task.WhenAll(stdout, stderr);
await child.WaitForExitAsync();

Console.WriteLine($"exit code {child.ExitCode}, SDK version {stdout.Result.Trim()}");
```

Passing arguments through `ArgumentList` matters. Building one command-line string by joining user input lets that input add its own arguments or commands. That is **command injection**.

### Signals and shutdown

On Linux and macOS, the OS tells a process about events with **signals**. **SIGINT** is sent when you press Ctrl+C. **SIGTERM** is a polite request to stop. The program may clean up first. **SIGKILL** ends the process immediately. It can't be caught or ignored.

`docker stop` sends SIGTERM, waits 10 seconds by default, then sends SIGKILL. Kubernetes does the same with a 30-second default. A program that handles SIGTERM can finish in-flight work and close connections cleanly. ASP.NET Core does this for you. A plain console app must do it itself.

```csharp
// process.cs — run with: dotnet run process.cs, then press Ctrl+C
using System.Diagnostics;
using System.Runtime.InteropServices;

var me = Process.GetCurrentProcess();
Console.WriteLine($"Process id:   {Environment.ProcessId}");
Console.WriteLine($"Threads:      {me.Threads.Count}");     // more than 1: the runtime starts its own
Console.WriteLine($"Memory:       {me.WorkingSet64 / 1024 / 1024} MiB");
Console.WriteLine($"Cores:        {Environment.ProcessorCount}");
Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Arguments:    {string.Join(' ', args)}");
Console.WriteLine($"PATH entries: {Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator).Length}");

using var stop = new CancellationTokenSource();
using var onInt  = PosixSignalRegistration.Create(PosixSignal.SIGINT,  Handle);
using var onTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handle);

void Handle(PosixSignalContext ctx)
{
    ctx.Cancel = true;   // don't end at once; let the program finish up
    Console.Error.WriteLine($"Got {ctx.Signal}, shutting down");
    stop.Cancel();
}

try
{
    Console.WriteLine("Working. Press Ctrl+C to stop.");
    await Task.Delay(Timeout.Infinite, stop.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cleaned up.");
}
return 0;   // success; check it with: echo $?   or in PowerShell: $LASTEXITCODE
```

### Users and privileges

The operating system runs every process as some **user**. The user decides which files it can touch and what else it may do. On Linux the all-powerful user is **root**, with user id 0. On Windows it is an administrator.

Run programs with the least privilege they need. A bug or break-in in a program running as root owns the whole machine.

Containers run as root by default. Official .NET images since .NET 8 include a non-root user called `app`, and many teams switch to it with a `USER` line. On Linux, only privileged processes may listen on ports below 1024. That is one reason .NET 8 images changed the default port for web apps from 80 to 8080.

### Services and daemons

A program that runs in the background, without a user logged in, is a **service** on Windows or a **daemon** on Linux. The OS starts it at boot, restarts it if it crashes and collects its logs.

On most Linux systems, **systemd** manages services, configured by small unit files. Its journal collects their output. On Windows, the Service Control Manager does the same job. The .NET Generic Host, used by the Worker Service template, has packages that fit it into both.

### Containers are processes

A **container** is not a small virtual machine. It is an ordinary process on the host, with two kernel features applied.

**Namespaces** give the process its own view of the system. It sees its own process list, where it is PID 1, its own file system and its own network interfaces. **Control groups** (cgroups) limit how much CPU and memory it may use.

All containers on a host share the host's kernel. That is why they start in milliseconds and why a Linux container needs a Linux kernel underneath. Docker Desktop on Windows and macOS runs a small Linux virtual machine for this.

### Clocks

A computer has more than one idea of time.

**Wall-clock time** is the date and time of day. It is what `DateTime.Now` and `DateTimeOffset.UtcNow` return. It can jump. Network time sync corrects it, an administrator can change it, and virtual machines can drift and snap back.

A **monotonic clock** only moves forward, at a steady rate, from some arbitrary starting point. It is useless for telling the date and perfect for measuring durations. In .NET, `Stopwatch` uses it.

Timers and `Thread.Sleep` are not exact either. A sleep of 50 ms means "at least about 50 ms". On Windows it can be a dozen or more milliseconds late, depending on the system's timer resolution.

Code that reads the clock directly is hard to test. .NET 8 added `TimeProvider`, an abstraction over both clocks and timers. Tests can pass in a fake one and move time forward by hand.

```csharp
// clocks.cs — run with: dotnet run clocks.cs
using System.Diagnostics;

long start = Stopwatch.GetTimestamp();                // monotonic clock
Thread.Sleep(50);
Console.WriteLine($"{Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");  // about 50, often a bit more

Console.WriteLine(DateTimeOffset.UtcNow.ToString("O"));          // wall clock; can jump
Console.WriteLine(TimeProvider.System.GetUtcNow().ToString("O")); // the same, through a replaceable abstraction
Console.WriteLine(Stopwatch.IsHighResolution);                    // True on any modern machine
```

### Limits you will hit

The OS limits how much of each resource a process may hold.

**File descriptors.** Linux sets a per-process limit, often 1,024 by default for ordinary processes, shown by `ulimit -n`. Every open file and every network connection uses one. A busy server that leaks connections fails with "Too many open files".

**Ports.** Each outgoing connection uses a local port number. Linux hands out roughly 28,000 of these by default. A closed connection keeps its port in a waiting state called TIME_WAIT for up to a minute. Code that creates a new `HttpClient` for every request can run out of ports under load. Share one client, or use `IHttpClientFactory`. The details are in TCP and UDP (6.3).

**Memory and processes.** Containers and systemd services can cap memory, the number of processes and threads, and CPU time. Hitting a cap usually looks like a crash, not an error message.

### Virtual machines

A **virtual machine** (VM) goes further than a container. A layer of software called a **hypervisor** gives each VM its own simulated hardware. Each VM runs its own full operating system, with its own kernel.

That gives stronger isolation than containers, at the cost of slower start-up and more memory. Cloud instances are VMs. Your containers in the cloud usually run inside VMs. WSL 2 on Windows runs a real Linux kernel in a lightweight VM, which is how Docker Desktop runs Linux containers there. Some services, such as AWS Lambda, use tiny VMs that start in a fraction of a second, to get VM isolation with near-container speed.

### In short

The kernel controls the hardware, and programs ask it for everything through system calls. A process is a running program with its own private memory; threads run inside a process and share its memory. Programs talk to the world through arguments, environment variables, standard input and output, and exit codes. Signals ask a process to stop, so services must handle them to shut down cleanly. A container is an ordinary process with isolation and limits applied, while a virtual machine runs a whole separate operating system.

### What goes wrong

**Ignoring SIGTERM.** Every deployment kills in-flight requests or half-finished jobs. Handle the signal, or use a host that does, such as the .NET Generic Host.

**PID 1 without signal handling.** The kernel gives no default signal behaviour to PID 1. If your process doesn't handle SIGTERM, `docker stop` waits the full grace period, then kills it.

**Memory limits.** A container over its memory limit is killed with code 137, often with no error in your logs. Current .NET reads the container's limit and sizes its heap to it. Leave headroom anyway.

**A thread per task.** Starting thousands of threads wastes memory and time on context switches. Use the thread pool, async code or a bounded queue.

**Exit code 0 on failure.** A script that catches every error and exits normally makes a broken CI build look green. Return a non-zero code when something fails.

**Swapping.** A machine short of memory can slow down a thousand times before anything crashes. Watch memory, not just CPU.

**Deadlocked pipes.** A build tool that reads a child's output to the end, then its errors, hangs forever when the child writes a lot of errors. Read both streams concurrently.

**Command injection.** Joining user input into a command line lets it run extra commands. Use `ArgumentList` and `UseShellExecute = false`, and never pass untrusted input to a shell.

**Secrets in environment variables.** They show up in `docker inspect`, in crash dumps and in every child process. Prefer a secret store or mounted secret files.

**Timing with the wall clock.** A measurement using `DateTime.Now` goes negative when the clock is corrected. Use `Stopwatch`.

**Port exhaustion.** A new `HttpClient` per request works in testing and fails under production load with socket errors. Reuse clients.

**Running containers as root.** A single exploited bug gets full control inside the container, and a much easier path out of it. Switch to a non-root user.

### Cost model

A system call costs roughly 0.1 to a few microseconds. A context switch costs a few microseconds. Creating a thread takes tens of microseconds and reserves its stack. Creating a process takes around a millisecond or more. A page fault that finds the page in RAM costs microseconds. One that must read from disk costs a disk read. Starting a container costs roughly as much as starting its process, plus setup, typically well under a second.

Starting a .NET process takes tens to hundreds of milliseconds, most of it the runtime starting up. Native AOT compilation, covered in Compilers, interpreters and JIT (2.2), cuts that sharply. Booting a full virtual machine takes seconds to minutes. A lightweight microVM can start in about a hundred milliseconds.

### In other languages (optional)

**Node.js.** Your JavaScript runs on one thread, driven by an event loop. The libuv library underneath keeps a small thread pool, four threads by default, for file access and DNS lookups. `process.on("SIGTERM", ...)` handles shutdown. `child_process.spawn` starts child processes, with the same pipe rules.

**Python.** In the standard build, the global interpreter lock (GIL) lets only one thread run Python code at a time. The `multiprocessing` module uses separate processes to get around it. Optional free-threaded builds without the GIL exist since Python 3.13.

**Go.** Goroutines are lightweight tasks that the Go runtime schedules onto a few OS threads. Thousands of goroutines are normal.

**Java.** Virtual threads, added in Java 21, work in a similar way: many lightweight threads on few OS threads. .NET reaches a similar result with async code, covered in Async and await (5.3).

### Key terms

- **Operating system**: The software that shares hardware among programs and protects them from each other.
- **Kernel**: The core of the operating system, running with full control of the hardware.
- **User mode**: The restricted processor mode ordinary programs run in.
- **System call**: A request from a program to the kernel, such as opening a file.
- **Process**: A running program with its own private memory and resources.
- **File descriptor**: A number a process uses to refer to an open file, pipe or network connection. Windows calls it a handle.
- **Virtual memory**: Each process's private view of memory, translated to physical memory by the processor and kernel.
- **Page fault**: Touching a page that isn't in RAM, which makes the kernel load or create it.
- **Thread**: One path of execution inside a process. Threads share memory and each has its own stack.
- **Scheduler**: The part of the kernel that decides which thread runs on which core, and when.
- **Context switch**: Saving one thread's state and loading another's, so a core can run a different thread.
- **Environment variable**: A named text value a process inherits from whatever started it.
- **Exit code**: The number a process returns when it ends. Zero means success.
- **Signal**: A notification from the OS to a process, such as SIGTERM asking it to stop.
- **Pipe**: A kernel buffer connecting one process's output to another's input.
- **Monotonic clock**: A clock that only moves forward at a steady rate. Used for measuring durations.
- **Container**: An ordinary process isolated with namespaces and limited with control groups.
- **Virtual machine**: A simulated computer with its own operating system, run by a hypervisor.

### Where this leads

This ends Part 1. Part 2 starts from source code, in What a programming language is (2.1). Threads are used directly in Threads and locks (5.2), and avoided while waiting in Async and await (5.3). Network connections are file descriptors too, in TCP and UDP (6.3). Containers are built and run in Containers and Docker (11.3). Watching memory and threads in production is in Observability (11.6).

### Check yourself

1. Run `process.cs`, stop it with Ctrl+C, and check the exit code. Then change the program to return 1 on shutdown and check again.
2. With Docker installed, run `docker run --rm -m 100m python:3.12-slim python -c "x = b'a' * 500_000_000"` and then `echo $?`. Explain the number you get.
3. Look at the thread count of a running ASP.NET Core app. On Windows, add the Threads column in Task Manager's Details tab. On Linux, run `ps -o nlwp -p <pid>`.
4. Change `child.cs` to run `dotnet --list-sdks`. Then make it run a command that doesn't exist, and handle the exception it throws.
5. Set `Logging__LogLevel__Default=Debug` in your shell, run an ASP.NET Core app and check that debug logging appears.
6. Run `ulimit -n` on a Linux machine or container. Then find the same limit for a running service with `cat /proc/<pid>/limits`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Every deploy drops in-flight requests

During each rolling deploy, a few hundred requests fail with connection resets.

**What goes wrong.** The container's entrypoint is a shell script, so the shell is PID 1 and doesn't forward SIGTERM; the app is killed after the grace period.

*Dockerfile: exec form, app as PID 1*

```docker
# Before: CMD ./start.sh   (sh is PID 1, SIGTERM never reaches dotnet)
ENTRYPOINT ["dotnet", "Bookings.Api.dll"]
```

*App: give in-flight work time to finish*

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));
// Kubernetes terminationGracePeriodSeconds must be larger, e.g. 30.
```

> **Lesson:** Services must receive and handle SIGTERM. Use the exec form so your process is PID 1, and size the shutdown timeout below the orchestrator's grace period.

#### Example 2: "Too many open files" under load

At peak, a gateway logs `IOException: Too many open files` and stops accepting connections.

*Diagnose*

```bash
ls /proc/$(pgrep -f Gateway)/fd | wc -l     # how many descriptors are open?
cat /proc/$(pgrep -f Gateway)/limits | grep "open files"
ss -tan state time-wait | wc -l              # thousands of TIME_WAIT sockets point at new HttpClient per call
```

*Fix the cause*

```csharp
// Before: using var client = new HttpClient();  inside every request
builder.Services.AddHttpClient<PartnerClient>(c => c.BaseAddress = new("https://partner.example"));
```

> **Lesson:** Files and sockets share one per-process limit. Find what is leaking descriptors before raising the limit.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · async / await · Ground Floor</p>

<a id="be-c305"></a>

#### Process, virtual memory, thread

A **process** is a running program: its own isolated address space, its own open handles, its own loaded code. Isolation is enforced by hardware — one process cannot read another's memory. That's what keeps a crashed browser tab from taking down your OS.

The address space is **virtual**. Your program sees a flat range of addresses starting at 0; the CPU's memory management unit translates those to real physical addresses via page tables, in 4 KB units called **pages**. Three consequences worth knowing:

- **Reserved vs committed.** Reserving address space costs nothing real — you're claiming numbers. Committing backs those numbers with physical memory or page file. This is why a 1 MB thread stack isn't actually 1 MB of RAM until you use it.
- **Page faults.** Touch a page that isn't resident and the CPU traps to the OS, which fetches it. A soft fault is fast; a hard fault hits disk and costs like the SSD row above.
- **Isolation is per-process, not per-thread.** Which leads directly to the next point.

A **thread** is one path of execution *inside* a process: an instruction pointer, a set of register values, and its own stack. Threads in the same process **share the heap and all global state.** That sharing is what makes threads useful and what makes them dangerous — it's the entire origin of race conditions.

State the split precisely, because it's load-bearing later:

| Per-thread                      | Shared across threads    |
|---------------------------------|--------------------------|
| Stack                           | Heap                     |
| Registers / instruction pointer | Static fields            |
| `[ThreadStatic]` fields         | Loaded code              |
| Thread-local storage            | Open file/socket handles |

Note what's in the right column: **the heap is shared.** A heap object created by thread \#7 can be read by thread \#12. That is precisely what makes the async trick possible — thread \#7 can put its state on the heap and thread \#12 can pick it up. It's also why `[ThreadStatic]` is a trap in async code: your continuation runs on a different thread, so thread-local state silently vanishes.

<a id="be-c308"></a>

#### The OS scheduler

More threads than cores means the OS must share. It keeps every thread in one of a few states:

- **Running** — currently on a core.
- **Ready** — could run, waiting for a core.
- **Blocked / Waiting** — cannot run until something happens (I/O completes, a lock frees, a timer fires). **The scheduler skips these entirely** — they consume zero CPU.

The scheduler picks a Ready thread, runs it for a **quantum** (Windows: roughly 20–120 ms depending on configuration, often much shorter in practice due to preemption), then swaps. It also preempts immediately when a higher-priority thread becomes Ready.

That swap is the **context switch**: save registers and instruction pointer, load the other thread's, switch page-table root if it's a different process. The direct cost is 1–10 microseconds. The *indirect* cost — arriving with cold caches — is usually larger and doesn't show up in any profiler as a line item.

The key fact for everything below: **a Blocked thread costs no CPU.** The scheduler doesn't even look at it. It is pure overhead in memory and bookkeeping only. That's the shape of the waste async eliminates.

<a id="be-c309"></a>

#### How I/O really works

You call a read on a socket. What actually happens:

1.  Your code makes a **system call** — a controlled trap into the kernel.
2.  The kernel hands the request to the network stack and the device driver.
3.  **The kernel writes down: "socket 42 — when data arrives, do this."** A table entry.
4.  The kernel returns.
5.  Milliseconds later, the network card receives a packet and raises a **hardware interrupt**.
6.  The CPU stops what it's doing, runs the driver's interrupt handler, which matches the packet to that table entry.
7.  Whatever was registered gets notified.

Read step 3 again. **The waiting is a table entry, not a thread.** Nothing stands guard.

The OS exposes two ways to consume this:

**Blocking (synchronous)** — the kernel puts *your thread* into the Waiting state and wakes it at step 7. Simple to program. Costs one entire thread per in-flight operation.

**Completion-based (asynchronous)** — the kernel notifies a registered callback at step 7, and your thread never waits. Windows calls this **IOCP** (I/O Completion Ports); Linux offers **epoll** and **io_uring**. .NET's async I/O is built directly on these.

The critical asymmetry: **both paths do identical work in the kernel.** The blocking version doesn't get faster service. It just additionally parks a thread. The thread buys you nothing except a convenient place to remember what you were doing — which is the exact realization Part I turns into async/await.

<a id="be-c310"></a>

#### User mode, kernel mode, and what a system call costs

The CPU runs at one of several **privilege levels**. Your code runs in **user mode**, where privileged instructions — talking to devices, editing page tables, disabling interrupts — are forbidden. The OS runs in **kernel mode**, where they're allowed.

Anything touching hardware therefore requires crossing that boundary via a **system call**: a deliberate, controlled trap (the `syscall` instruction on x64). The CPU switches privilege level, jumps to a fixed kernel entry point, the kernel validates your arguments, does the work, and returns.

The crossing costs roughly **100–500 nanoseconds** each way — cheaper than a context switch, far more expensive than a function call, and it partially disturbs caches and pipelines.

Why this matters here: **an await is not a system call.** Suspending a state machine is pure user-mode work — write some fields, register a callback, return. No kernel crossing. Whereas blocking a thread *is* a system call (you're asking the OS to change your scheduling state), and waking it is another one, plus a context switch.

So async isn't only saving a megabyte. It's replacing *syscall + context switch + syscall + context switch* with *a few field writes*. That's the per-operation overhead difference, and at high request rates it's substantial on its own.

(Related trivia with real consequences: `Thread.Sleep` blocks and syscalls; `await Task.Delay` registers a timer and returns. `SpinWait` burns CPU deliberately to avoid the syscall when a wait is expected to be nanoseconds long — occasionally the right tool, almost never in application code.)

### How a computer runs a program

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How a computer runs a program</p>

<a id="be-m5"></a>

#### The operating system: what it is for

A CPU running one program with total access to the machine needs no operating system. The OS exists because we want to run *many* programs, written by people who do not trust each other, on hardware they must share without corrupting one another.

It solves four problems:

| Problem                                        | The OS provides                                                                      |
|------------------------------------------------|--------------------------------------------------------------------------------------|
| Two programs must not read each other's memory | virtual memory and process isolation ([Processes and virtual memory](#be-m6))        |
| More programs than CPU cores                   | the scheduler ([Threads, scheduling, and context switches](05-02-threads-and-locks.md#be-m7))                  |
| Hardware differs wildly and is complicated     | drivers, and uniform abstractions — files, sockets                                   |
| A program must not do whatever it likes        | privilege levels and system calls ([System calls: crossing into the kernel](#be-m8)) |

``` text
  ┌──────────────────────────────────────────────┐
  │  USER MODE  (unprivileged, "ring 3")         │
  │  your app, the .NET runtime, the browser,    │
  │  the database engine                          │
  └───────────────────┬──────────────────────────┘
                      │  system call - the ONLY doorway
  ┌───────────────────▼──────────────────────────┐
  │  KERNEL MODE  (privileged, "ring 0")         │
  │  scheduler │ memory manager │ filesystem     │
  │  network stack │ device drivers               │
  └───────────────────┬──────────────────────────┘
                      ▼
                  hardware
```

The CPU itself enforces this: certain instructions and certain memory regions are unavailable in user mode, and attempting them faults. Your application *cannot* touch a disk, open a socket, or allocate memory from the OS directly. It can only ask.

> **\[Why it matters\]**
>
> **This is why everything expensive is a boundary crossing.** Reading a file, sending a byte, creating a thread — each is a request to the kernel, and each costs a transition ([System calls: crossing into the kernel](#be-m8)). It is also the reason a memory allocation inside .NET is cheap while the runtime's own request to the OS for more memory is not: the runtime asks for a large chunk once and sub-allocates from it ([What new actually does](01-04-the-processor.md#be-c303)).

<a id="be-m6"></a>

#### Processes and virtual memory

A **process** is a running program plus everything it owns: memory, open files, sockets, threads. Its defining property is isolation — and that isolation is an illusion the hardware maintains.

Every process is given its own **virtual address space**, numbered from zero as if it had the machine to itself. The CPU's memory management unit translates every access through *page tables* the kernel maintains, in 4 KB units called pages.

``` text
Process A                    Page tables                 Physical RAM
  address 0x1000  ────────►  A: 0x1000 → 0x8A3000  ────►  ┌──────────┐
                                                           │          │
Process B                                                  │          │
  address 0x1000  ────────►  B: 0x1000 → 0x41F000  ────►  │          │
                                                           └──────────┘
// Both processes use "address 0x1000" and land on different physical memory.
// Neither can name the other's pages. The isolation is absolute and free -
// the MMU does the translation as part of every memory access.
```

Three behaviours follow, and each has a visible consequence:

- **Memory is mapped lazily.** Reserving address space costs nothing; touching a page for the first time triggers a *page fault*, and the kernel finds physical memory then. This is why a process's "virtual size" can be far larger than its real usage.
- **Pages can be shared.** The same physical page can be mapped into many processes — how a shared library is loaded once and used by everything.
- **Pages can be backed by disk.** A memory-mapped file, or swap. An access to a swapped-out page stalls for milliseconds rather than nanoseconds — a factor of a million.

> **\[Trap\]**
>
> **Container memory limits kill, they do not slow.** A process exceeding its cgroup limit is terminated by the OOM killer with no exception, no stack trace, and nothing in your application logs — the process simply vanishes. If a service disappears under load with no error, check the exit code (137) before searching the code. .NET reads the container limit and sizes its heap accordingly, but only if the limit is set ([The GC in one page](02-07-memory-management-and-garbage-collection.md#be-b239)).

<a id="be-m8"></a>

#### System calls: crossing into the kernel

A system call is the controlled doorway from user mode into the kernel. Your code puts a number identifying the operation and its arguments into registers and executes a special instruction; the CPU switches privilege level and jumps to a fixed kernel entry point.

``` text
File.ReadAllBytes(path)
  └─► .NET's FileStream
        └─► open()   ── syscall ──► kernel: resolve the path, check permissions,
        └─► read()   ── syscall ──► kernel: copy from page cache to your buffer
        └─► close()  ── syscall ──►

// Every one of those crossings costs a privilege transition:
//   roughly 100ns-1us each, higher on kernels with Spectre/Meltdown mitigations.
// Compare: a normal function call is ~1ns.
```

The cost is why buffering exists everywhere. Reading a 1 MB file one byte at a time is a million system calls; reading it in 64 KB chunks is sixteen. That is the same 1000× difference for identical logical work, and it is why `StreamReader` has a buffer size parameter that is worth setting ([Streams, buffering, and file I/O](01-06-storage-and-files.md#be-m89)).

| Operation                        | Underlying syscalls                          |
|----------------------------------|----------------------------------------------|
| opening a file                   | `open` / `CreateFile`                        |
| allocating a big chunk of memory | `mmap` / `VirtualAlloc`                      |
| creating a thread                | `clone` / `CreateThread`                     |
| connecting to a database         | `socket`, `connect`, then `send`/`recv`      |
| taking a contended lock          | `futex` / `WaitForSingleObject`              |
| reading the wall clock           | often *none* — vDSO reads it from user space |

> **\[Tip\]**
>
> **An uncontended lock does not enter the kernel.** .NET spins briefly in user mode first, and only makes a syscall if it must actually park the thread. This is why lock cost is bimodal: nanoseconds when uncontended, microseconds when contended. Reducing contention matters far more than choosing a cheaper primitive ([Choosing a synchronization primitive](05-02-threads-and-locks.md#be-m133)).

<a id="be-m10"></a>

#### Devices, interrupts, and why I/O is different

Devices are enormously slower than the CPU and operate independently of it. A disk or network card is told what to do, then works on its own and **interrupts** the CPU when finished.

``` yaml
  CPU: "read 4 KB from sector 12345 into address 0x8A3000"   ──► disk controller
  CPU: ... free to run something else entirely ...
  disk: (works for ~100 microseconds, writes the data itself via DMA)
  disk: ──── INTERRUPT ────► CPU stops, runs the driver's handler,
                             marks the waiting thread runnable
```

The device writes into memory directly (DMA) without the CPU copying byte by byte. So the CPU genuinely has nothing to do during an I/O operation — which raises the question of what the requesting thread should do.

| Model            | The thread                         | Cost of 10,000 concurrent operations           |
|------------------|------------------------------------|------------------------------------------------|
| **Blocking**     | parked until the interrupt arrives | 10,000 threads — ~10 GB of stacks              |
| **Asynchronous** | registers interest and moves on    | a handful of threads, 10,000 pending callbacks |

``` csharp
// The kernel mechanisms that make async I/O possible:
//   Linux    epoll, and io_uring on modern kernels
//   Windows  I/O completion ports (IOCP)
//   macOS    kqueue
//
// .NET's async I/O sits directly on these. 'await stream.ReadAsync(...)'
// registers the operation and RETURNS THE THREAD to the pool; the continuation
// runs on a pool thread when the completion event arrives.
```

> **\[Why it matters\]**
>
> **This is the hardware reason async exists** — not a language fashion. The device was always going to work independently; the only question is whether you keep a megabyte of stack parked while it does. Everything in [Async data access: starvation and deadlock](05-03-async-and-await.md#be-m136) follows from this one fact, including why blocking on an async call is worse than never having gone async at all.

> **\[Tip\]**
>
> **It also explains why async does not make anything faster.** A single database query takes exactly as long either way — the disk and the network are unchanged. Async improves *how many* operations one machine can have in flight. If your service handles ten requests a minute, async buys you nothing; at ten thousand a second it is the difference between working and not.

<a id="be-m11"></a>

#### Putting it together: running a .NET application

Every layer so far, in the order it engages when you type `dotnet MyApp.dll`:

``` csharp
 1. The shell asks the kernel to run the 'dotnet' executable.
    └─ syscalls: fork/exec (§8)

 2. The kernel creates a PROCESS: a fresh virtual address space, a page table,
    a file descriptor table with stdin/stdout/stderr (§6)

 3. The loader maps the executable and its shared libraries into that space.
    Nothing is read from disk yet - the pages are mapped, faulted in on use.

 4. The MAIN THREAD begins executing at the entry point, with a fresh stack
    (§7, §23)

 5. The host resolves which runtime to use, from runtimeconfig.json,
    and loads coreclr (§39)

 6. The CLR initialises: it asks the OS for a large region of memory (mmap /
    VirtualAlloc) and carves the GC HEAP out of it (§24)

 7. MyApp.dll is loaded; its metadata is read (§38)

 8. The entry point method is JIT-COMPILED to machine code for THIS cpu
    (§40) - and only then does a single instruction of your
    program actually execute on the processor (§3)

 9. Your Main runs. Each new method is JIT-compiled on first call.
    Each 'new' bumps a pointer in the GC heap (§32).
    Each file or socket operation is a syscall (§9).
    Each 'await' on I/O returns the thread to the pool (§10).
```

> **\[Why it matters\]**
>
> **Why walk through this.** Almost every performance question resolves to "which of those layers is this actually touching?" A slow first request is step 8. A memory limit kill is step 2. A handle leak is step 3's descriptor table. A thread pool starvation is step 4 multiplied by blocked I/O. Knowing the layers turns "it's slow" into a question with a finite set of candidate answers ([Rules of thumb, ranked](12-04-scaling-and-performance.md#be-m276)).

> **\[Tip\]**
>
> **The remaining piece is the network** — because in a real application the expensive step is not any of the above, it is step 9 talking to a database on another machine. That is the next part, and it is where the largest numbers in this whole guide live.
