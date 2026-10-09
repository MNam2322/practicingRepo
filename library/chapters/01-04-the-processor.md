---
id: "1-4"
number: "1.4"
title: "The processor"
part: 1
part_title: "The machine"
words: 6221
minutes: 27
---

# 1.4 The processor

> **Quick look.** What the CPU actually does with your code, and why memory access usually decides speed.
>
> - The processor fetches, decodes and executes simple instructions on values in registers.
> - Loops and branches are jumps; small methods get inlined by the JIT.
> - Machine code is specific to an instruction set (x64, ARM64): images and native dependencies must match.
> - Modern chips overlap, predict and vectorize (SIMD) work, and have many cores.
> - Waiting on memory costs far more than arithmetic; sequential access beats random access by about two orders of magnitude.
> - Moving to ARM can expose race conditions that x64 hid.


### The big picture

The processor, or CPU, is the part of the computer that actually runs your code. It understands only a small set of very simple instructions: move this number, add these two, compare, jump somewhere else. Everything you write, from a loop to an async web request, is turned into long sequences of those steps.

Knowing how the processor works explains why some code is fast and some slow, why a program built for one kind of chip won't run on another, and why moving a service to ARM servers can expose old bugs. You will also see the machine code .NET produces from your own C#.

### Before you start

**Builds on:** 1.3 Text is numbers too.

**Ideas explained later:**

- **Machine code** (2.2): Instructions encoded as numbers, ready for a specific processor to run.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.
- **JIT** (JIT compiler, 2.2): A compiler that turns bytecode into machine code while the program runs, just before it is needed.
- **Data race** (5.1): Two threads accessing the same memory at once, at least one writing, with nothing ordering them.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Side effect** (4.4): Anything a function does besides return a result, such as changing data or doing I/O.
- **AWS** (11.5): Amazon Web Services, the largest cloud provider, launched in 2006.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **Terminal** (1.7): A text window where you type commands for the shell to run, such as dotnet run.
- **RID** (11.2): Runtime identifier: a name for an operating system and processor combination, such as linux-x64, used when building .NET apps.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, lambdas, LINQ. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a register?**

> A tiny, very fast storage slot inside the processor. The processor does its arithmetic on values in registers. It loads values from memory into registers and stores results back.

**2. What is an instruction set architecture, and why does it matter to you?**

> It is the set of instructions a family of processors understands, such as x86-64 or ARM64. Native code built for one does not run on the other. This shows up with Docker images and native libraries.

**3. Why can an unpredictable if statement slow down a loop?**

> The processor guesses which way each branch goes so it can keep working ahead. A wrong guess throws away that work and costs roughly 15 to 20 cycles.

**4. At 4 GHz, roughly how long is one clock cycle?**

> 0.25 nanoseconds. One second divided by four billion.

**5. What does a compare instruction actually produce?**

> No stored value. It sets bits in the flags register, such as zero, sign, carry and overflow. The next conditional jump reads those bits.

**6. Why are small methods usually free in .NET?**

> The JIT inlines them. It copies the method's body into the caller, so there is no call at all.

**7. Code with a hidden data race ran fine for years on x64 servers, then broke on ARM64. Why is that plausible?**

> ARM64 allows the processor to reorder memory operations more freely than x64. Races that x64's stricter ordering happened to hide become visible.

**8. What is a vCPU in the cloud?**

> Usually one hardware thread of a physical core. With simultaneous multithreading, two vCPUs often share one core.

### What the processor does

The processor, or **CPU**, runs **instructions**. An instruction is a tiny, fixed operation. Load a value from memory. Add two values. Compare two values. Jump to another instruction. Store a value to memory.

That is almost the whole list. Every program you have written, however large, ends up as a long sequence of these small steps.

### Registers

The processor has a few dozen small storage slots of its own, called **registers**. On a 64-bit processor each one holds 64 bits. Registers are the fastest storage in the machine. Arithmetic happens on values in registers.

So a typical piece of work looks like this. Load values from memory into registers. Do the arithmetic. Store the result back to memory.

### The clock

A processor works in steps timed by a **clock**. Each tick is one **cycle**. A processor running at 4 GHz ticks four billion times a second. One cycle is 0.25 nanoseconds.

A simple instruction such as an addition takes about one cycle. Modern processors often finish several instructions per cycle, as you will see below.

### Fetch, decode, execute

The basic loop never changes. The processor **fetches** the next instruction from memory. It **decodes** it to work out what to do. It **executes** it. Then it moves on to the next one.

A special register called the **instruction pointer** holds the address of the next instruction. Normally it just moves forward. A jump instruction changes it, and that is how the processor goes somewhere else.

### From your code to instructions

Take a C# method that adds two numbers. The compiler and the runtime turn it into machine code. Machine code is just numbers. **Assembly language** is the human-readable way to write those numbers.

```text
; int Add(int b, int c) => b + c;
; x64, simplified. On Windows the first two int arguments arrive in ecx and edx.
mov  eax, ecx      ; copy the first argument into eax
add  eax, edx      ; add the second argument to it
ret                ; return; the caller finds the result in eax
```

The agreement about which registers hold arguments and results is called a **calling convention**. It differs between operating systems. Linux uses different registers than Windows.

### Flags: how comparisons work

The processor has a special register called the **flags register**. Many instructions set its bits as a side effect.

The **zero flag** is set when a result is zero. The **sign flag** copies the top bit of the result. The **carry flag** is set when an unsigned result didn't fit. The **overflow flag** is set when a signed result didn't fit.

A compare instruction, `cmp`, subtracts one value from another and throws the answer away. Only the flags remain. The next instruction is usually a conditional jump that reads them. `je` jumps if the zero flag is set, meaning the values were equal. `jl` jumps if the first value was less.

This is also how `checked` arithmetic in C# works. The JIT adds a `jo` instruction, "jump if overflow", after each checked addition. The jump leads to code that throws `OverflowException`. When nothing overflows, the check costs almost nothing.

### Branches and loops

An `if` statement becomes a comparison followed by a conditional jump. A loop is a jump back to an earlier instruction. Here is a simplified version of summing an array.

```text
; for (int i = 0; i < n; i++) sum += a[i];   simplified
      xor  eax, eax         ; sum = 0   (XOR a value with itself gives 0)
      xor  r8d, r8d         ; i = 0
loop: cmp  r8d, edx         ; compare i with n
      jge  done             ; if i >= n, jump to done
      add  eax, [rcx+r8*4]  ; sum += a[i]   (each int is 4 bytes)
      inc  r8d              ; i++
      jmp  loop             ; back to the top
done: ret
```

Every high-level construct, including `foreach`, `switch`, method calls and exceptions, ends up as moves, arithmetic, comparisons and jumps like these.

### Calling a method

A method call is also just a jump, with a way back. The `call` instruction pushes the address of the next instruction onto the stack, then jumps to the method. The method's `ret` instruction pops that address and jumps back to it.

```text
; the caller
mov   ecx, 19          ; first argument
mov   edx, 23          ; second argument
call  Add              ; push the return address, jump to Add
; ... Add's ret brings execution back here, with the result in eax
```

Most methods start with a short **prologue** that makes room on the stack for locals and saves registers they will change. They end with an **epilogue** that undoes it. The stack itself is covered in Memory (1.5).

A call costs a few cycles. The bigger cost is what it prevents. The compiler can't optimize across a call it can't see into. So the JIT often **inlines** small methods: it copies their body into the caller. A property getter or a one-line helper usually costs nothing at all after inlining.

**Virtual** and **interface** calls are harder. The method to run depends on the object's actual type, so the processor first loads the method's address from the object, then jumps there. That extra step blocks inlining. Modern .NET often removes it by working out the actual type. This is called **devirtualization**. Marking classes `sealed` helps it.

### Instruction sets

Each family of processors has its own list of instructions. That list is the **instruction set architecture**, or ISA.

Two matter today. **x86-64**, also called amd64 or x64, runs most Windows PCs and many servers. **ARM64**, also called aarch64, runs phones, Apple Silicon Macs and a growing share of cloud servers such as AWS Graviton.

Machine code for one does not run on the other. .NET and JavaScript mostly hide this, because they compile to machine code on the machine that runs them. You see it in three places. Docker images are built per architecture. Native libraries such as SQLite drivers or image tools ship separate files per architecture. And .NET uses **runtime identifiers** like `linux-x64` and `osx-arm64` to pick the right ones.

### CISC and RISC

Instruction sets come in two broad styles. **CISC**, complex instruction set computing, has many instructions, some of which do a lot of work, with instructions of varying length. x86-64 is CISC. **RISC**, reduced instruction set computing, has fewer and simpler instructions of fixed length. ARM64 and RISC-V are RISC.

In practice the line has blurred. Modern x86 chips translate their complex instructions into simple internal steps. The difference that still matters to you is compatibility: code built for one runs only on processors of that family.

### How modern processors go faster

Clock speeds stopped rising much around 2005, when chips got too hot. Since then, processors have got faster by doing more per cycle. Several ideas stack up.

**Pipelining.** Fetching, decoding and executing are split into stages, like an assembly line. While one instruction executes, the next is being decoded and the one after is being fetched.

**Superscalar execution.** The processor has several execution units and can start several independent instructions in the same cycle.

**Out-of-order execution.** If one instruction is waiting for data, the processor runs later instructions that don't depend on it. It still makes the results appear in program order.

**Branch prediction.** At a conditional jump, the processor doesn't wait to find out which way it goes. It guesses, based on what happened before, and keeps working. Predictable branches cost almost nothing. A wrong guess throws away the work done ahead.

**SIMD.** Short for "single instruction, multiple data". One instruction works on several values at once, for example eight `int`s. .NET exposes this through `Vector<T>` and `Vector128<T>`, and many built-in methods use it for you.

**Multiple cores.** A modern chip contains several complete processors, called **cores**. Each runs its own stream of instructions. A single-threaded program uses only one core. Using more is the subject of Parallelism (5.5).

### Seeing it yourself

You can print the machine code .NET generates. Set the environment variable `DOTNET_JitDisasm` to a method name before running.

```csharp
// disasm.cs
// Linux or macOS:  DOTNET_JitDisasm="SumAll" dotnet run disasm.cs
// PowerShell:      $env:DOTNET_JitDisasm="SumAll"; dotnet run disasm.cs
using System.Linq;

int[] data = Enumerable.Range(1, 1000).ToArray();
long total = 0;
for (int round = 0; round < 100_000; round++)
    total += Kernels.SumAll(data);   // many calls, so the optimized version gets compiled
Console.WriteLine(total);            // 50050000000

static class Kernels
{
    public static long SumAll(int[] a)
    {
        long s = 0;
        foreach (int x in a) s += x;
        return s;
    }
}
```

The method may be printed more than once. .NET first compiles quickly with few optimizations, then recompiles hot methods properly. The last listing, marked Tier1 or FullOpts, is the optimized one. This is explained in Compilers, interpreters and JIT (2.2). For quick experiments without a terminal, sharplab.io and godbolt.org show the machine code in the browser.

### Measuring branches and SIMD

This program makes two kinds of processor behaviour visible. First, the same loop over the same numbers, once in random order and once sorted. Second, a plain sum against a SIMD sum.

```csharp
// branches.cs — run with: dotnet run -c Release branches.cs
using System.Diagnostics;
using System.Numerics;

var rng = new Random(42);
int[] data = new int[10_000_000];
for (int i = 0; i < data.Length; i++) data[i] = rng.Next(256);
int[] sorted = (int[])data.Clone();
Array.Sort(sorted);

CountBig("random order", data);
CountBig("sorted order", sorted);
Console.WriteLine(SumPlain(data) == SumSimd(data));   // True: same answer
Bench("plain sum", () => SumPlain(data));
Bench("SIMD sum ", () => SumSimd(data));
Console.WriteLine($"Vector<int> holds {Vector<int>.Count} ints here");

static void CountBig(string label, int[] a)
{
    var sw = Stopwatch.StartNew();
    long total = 0;
    for (int round = 0; round < 10; round++)
        foreach (int x in a)
            if (x >= 128) total += x;      // which way this goes depends on the data
    Console.WriteLine($"{label}: {sw.ElapsedMilliseconds} ms");
}

static void Bench(string label, Func<long> run)
{
    run();                                  // warm up first
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 10; i++) run();
    Console.WriteLine($"{label}: {sw.ElapsedMilliseconds} ms");
}

static long SumPlain(int[] a) { long s = 0; foreach (int x in a) s += x; return s; }

static long SumSimd(int[] a)
{
    var acc = Vector<int>.Zero;
    int i = 0;
    for (; i <= a.Length - Vector<int>.Count; i += Vector<int>.Count)
        acc += new Vector<int>(a, i);       // adds a whole row of ints in one instruction
    long s = 0;
    for (int k = 0; k < Vector<int>.Count; k++) s += acc[k];  // each lane stays well below int.MaxValue here
    for (; i < a.Length; i++) s += a[i];    // leftovers at the end
    return s;
}
```

With random data, the branch goes each way half the time and the predictor guesses wrong constantly. Sorted, it goes one way for the first half and the other way after, so the guesses are nearly always right.

You may see little or no difference. The JIT can replace a simple branch with a **conditional move**, an instruction that picks one of two values without jumping. There is then nothing to mispredict. That is a real optimization, and seeing it is part of the lesson.

### Interrupts

Devices need the processor's attention at unpredictable times. A network packet arrives. A disk finishes a read. A timer fires. The device sends an **interrupt**. The processor pauses what it is doing, saves its state and runs a small handler in the kernel. Then it carries on.

A timer interrupt fires many times per second. It is how the operating system takes the processor back from a running thread and gives it to another. That is covered in The operating system (1.7).

### Memory ordering

To go faster, processors and compilers reorder memory reads and writes. Within one thread you never notice, because the results come out as if the code ran in order. Another thread watching the same memory can notice. It may see a later write before an earlier one.

How much reordering is allowed is the processor's **memory ordering** model. x64 is fairly strict. ARM64 allows much more. Code that shares data between threads without locks or atomic operations may work on x64 and fail on ARM64.

The fix is always the same. Share data between threads only through locks, `Interlocked` operations, `Volatile` reads and writes, or thread-safe collections. They tell both the compiler and the processor where order matters. The details are in Threads and locks (5.2).

### Processors in the cloud

Cloud machines sell **vCPUs**. A vCPU is usually one hardware thread. With **simultaneous multithreading** (SMT, which Intel calls Hyper-Threading), one physical core runs two hardware threads. They share the core's execution units and caches, so two vCPUs don't give twice the speed of one.

Some instance types are **burstable**. They run fast while they have CPU credits and slow down sharply when the credits run out. Containers can have a CPU limit. Linux enforces it in short periods, usually 100 ms, and pauses the container for the rest of the period once it has used its share. Short bursts of work, such as startup or garbage collection, can then cause latency spikes.

Laptops add another variable. Clock speed rises when the chip is cool and falls when it gets hot or runs on battery. That makes timing measurements noisy.

### GPUs and other accelerators

A **GPU**, graphics processing unit, has thousands of simple cores. They run the same instruction on many pieces of data at once, like SIMD on a huge scale. This suits graphics, video and machine learning. It suits branchy, one-thing-at-a-time business logic very badly.

Data has to be copied to the GPU and back, which costs time. Other accelerators follow the same pattern for specific jobs, such as the neural processing units now built into many laptop and phone chips.

### In short

A processor fetches, decodes and executes simple instructions, one clock tick at a time, working on values held in its registers. Branches and loops are jumps, method calls are jumps with a way back, and small methods usually get inlined. Machine code is specific to an instruction set such as x64 or ARM64. Modern chips go faster by overlapping and predicting work, handling several values at once with SIMD, and using many cores. Memory access, not arithmetic, is usually what limits speed.

### What goes wrong

**Images for the wrong architecture.** An amd64-only Docker image on an ARM64 machine either runs slowly under emulation or fails with "exec format error". Build multi-architecture images, or set `--platform` deliberately.

**Native dependencies.** A library that works on your x64 laptop can fail on an ARM64 server because its native part was never published for that architecture.

**Optimizing the wrong thing.** Arithmetic is rarely the bottleneck. Waiting for memory, disk and network usually is. Measure before you optimize.

**Expecting more cores to help on their own.** A program that does its work on one thread gets no faster on a 32-core machine.

**Benchmarking badly.** Timing a method once, in a debug build, measures the slow first compilation, not the steady state. Use a release build and a tool like BenchmarkDotNet, which handles warm-up and statistics.

**Races exposed by a new architecture.** Moving from x64 to ARM64 servers can surface threading bugs that were always there. Review any code that shares mutable data without locks or `Interlocked`.

**CPU throttling in containers.** A service with a tight CPU limit shows slow requests while average CPU looks fine. Check the container's throttling metrics, not just its CPU percentage.

**Running out of burst credits.** A burstable cloud instance slows to a fraction of its speed after a busy period, with no error anywhere.

**Virtual calls in hot loops.** Calls through interfaces in a tight loop block inlining. Seal classes that aren't meant for inheritance, and measure before and after.

### Cost model

At 3 to 5 GHz, one cycle is 0.2 to 0.33 nanoseconds. Simple integer operations take about a cycle, and several can run per cycle. Integer division takes tens of cycles. A mispredicted branch costs about 15 to 20 cycles. Reading from main memory costs hundreds of cycles, which is why the next chapter matters more for speed than this one.

A direct call costs a few cycles. An inlined call costs nothing. A virtual or interface call adds a memory load and usually blocks inlining. SIMD can make simple loops over arrays several times faster, up to about the number of values that fit in one vector. Handling an interrupt costs microseconds.

### In other languages (optional)

**JavaScript.** The V8 engine in Chrome and Node.js runs code through several tiers, from an interpreter called Ignition up to an optimizing compiler called TurboFan. Like .NET, it compiles hot code more carefully. **WebAssembly** is a portable instruction set for browsers. Blazor runs .NET in the browser on top of it.

**Python.** CPython compiles code to bytecode and runs it in an interpreter loop. Each bytecode instruction costs many machine instructions, which is why pure Python loops are slow and libraries like NumPy do the heavy work in native code. Newer versions add a specializing interpreter and an experimental JIT.

**SQL.** A database engine runs a query plan made of operators. SQL Server's batch mode processes rows in groups and uses SIMD, which is part of why columnstore indexes are fast.

**Java.** The JVM uses tiered JIT compilation, very much like .NET.

### Key terms

- **CPU**: The processor. It runs instructions one small step at a time.
- **Instruction**: One tiny operation the processor understands, such as load, add, compare or jump.
- **Register**: A small, very fast storage slot inside the processor.
- **Clock cycle**: One tick of the processor's clock. At 4 GHz, 0.25 nanoseconds.
- **Assembly language**: The human-readable form of machine code.
- **Flags register**: A register whose bits record facts about the last result, such as zero or overflow.
- **Calling convention**: The agreement about which registers and stack slots carry arguments and results.
- **Inlining**: Copying a called method's body into its caller, removing the call.
- **Devirtualization**: Replacing a virtual or interface call with a direct call once the actual type is known.
- **Instruction set architecture**: The set of instructions a processor family understands, such as x86-64 or ARM64.
- **Branch prediction**: Guessing which way a conditional jump will go so the processor can keep working ahead.
- **SIMD**: Single instruction, multiple data. One instruction that works on several values at once.
- **Interrupt**: A signal from a device or timer that makes the processor pause and run a kernel handler.
- **Memory ordering**: The rules for how much a processor may reorder memory reads and writes as seen by other threads.
- **vCPU**: A virtual CPU sold by cloud providers, usually one hardware thread of a physical core.

### Where this leads

Why memory access dominates is in Memory (1.5). How source code becomes these instructions is in Compilers, interpreters and JIT (2.2). Using several cores is in Parallelism (5.5). Building images for several architectures is in Containers and Docker (11.3). Measuring performance properly is in Scaling and performance (12.4).

### Check yourself

1. Find your machine's architecture. Run `dotnet --info` and look for the RID, or print `System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture`.
2. Run `disasm.cs`. In the optimized listing, find the loop's add instruction and the jump back to the top of the loop.
3. Explain why a 4 GHz processor can run a program more slowly than a 3 GHz one.
4. Run `branches.cs` on your machine. Then change `x >= 128` to `(x & 1) == 0` and explain why sorting no longer helps.
5. Write the same sum over a `List<int>`, an `int[]` and an `IEnumerable<int>`. Time all three and explain the order.
6. Find out whether your cloud machines or containers have CPU limits. If they run on Kubernetes, look up the pod's CPU request and limit.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The same loop, five times faster

A nightly job sums seat revenue across millions of bookings stored as a list of objects. Profiling shows the CPU mostly waiting on memory.

*Before: pointer chasing*

```csharp
class Seat { public decimal Price; public string Cabin = ""; /* 10 more fields */ }
decimal sum = 0;
foreach (var s in seats) sum += s.Price;   // each object is somewhere else on the heap
```

*After: contiguous values*

```csharp
decimal[] prices = LoadPrices();          // one array of values, read in order
decimal sum = 0;
foreach (var p in prices) sum += p;       // the CPU prefetches 64-byte cache lines
```

> **Lesson:** Memory access usually dominates arithmetic. For bulk work, lay data out contiguously and walk it in order.

#### Example 2: Moving to ARM servers broke a native dependency

After switching cloud instances to ARM64 to save cost, the PDF service fails at start-up with a DllNotFoundException.

**What goes wrong.** The NuGet package shipped only an x64 native library.

*Check before you switch*

```bash
# Which runtime identifiers do your native assets support?
find ~/.nuget/packages/somepdflib -path "*runtimes*" -maxdepth 4 -type d
# Build and test an image for the target architecture explicitly
docker buildx build --platform linux/arm64 -t pdf-svc:arm64 .
```

> **Lesson:** Machine code is per instruction set. Inventory native dependencies and run the test suite on the target architecture before moving.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · async / await · Ground Floor</p>

<a id="be-c303"></a>

#### What a CPU actually does

`new Order()` on a reference type expands to a short, inlined sequence. Each thread holds its own *allocation context* — a small private slice of gen 0 — so the common path needs no locking at all.

``` csharp
var order = new Order();

; 1. size is a compile-time constant for a non-array type
;    lea  rax, [rcx+0x28]        ; current alloc pointer + 40 bytes
; 2. bounds check against the end of this thread's allocation context
;    cmp  rax, [r11+0x8]
;    ja   SLOW_PATH
; 3. commit: publish the new alloc pointer
;    mov  [r11], rax
; 4. write the method table pointer into the new object
;    mov  [rcx], METHODTABLE_Order
; 5. call the constructor

; ~10 instructions, no lock, no free list, no search. This is why
; "allocation is cheap" is literally true - it is a pointer add.
```

Things the fast path gives you for free:

- **Memory arrives zeroed.** The GC hands out pre-cleared space, which is why every field starts at `0`/`null`/`false` without the constructor doing anything.
- **The sync block index starts empty.** Lock state and the identity hash code are materialised lazily, only if something asks ([Object header and the real cost of an object](01-05-memory.md#be-m30)).

The **slow path** runs when the allocation context is exhausted: the thread takes a new slice, and if gen 0 is full, that triggers a collection. So the real cost of allocation is not the pointer add — it is that allocating enough eventually forces a gen 0 GC, whose cost is proportional to what *survives* ([The GC in one page](02-07-memory-management-and-garbage-collection.md#be-b239)).

``` csharp
// Two shapes that skip the fast path entirely:
var big = new byte[100_000];        // >= 85,000 bytes -> straight to the LOH
var arr = new Order[n];             // size is not constant; computed, then checked

// And one that is not an allocation at all:
var money = new Money(500m, "VND"); // struct: initialises existing storage.
                                    // No header, no method table pointer, no GC.
```

> **\[Tip\]**
>
> **The JIT can now remove some allocations outright.** .NET 9 added *escape analysis*: if the JIT can prove an object never leaves the method — not stored in a field, not returned, not captured, not passed somewhere it could be retained — it may allocate it on the stack instead, and the GC never sees it. Small, local, short-lived objects are the ones that qualify, which is another reason to keep them that way rather than hoisting them into fields "to reuse".

> **\[Trap\]**
>
> **`new` in a generic method with `where T : new()` is not this fast.** For reference types the runtime goes through `Activator.CreateInstance`, roughly an order of magnitude slower than a direct `new`, and it shows up immediately in a loop. Pass a `Func<T>` factory instead ([Constraints and what each one unlocks](02-03-types.md#be-m59)).

<a id="be-c304"></a>

#### The memory hierarchy, and why it dominates

Rough latencies on a modern machine, in the only unit that matters — how many instructions the CPU could have executed while waiting:

| Storage                  | Latency  | Instructions wasted |
|--------------------------|----------|---------------------|
| Register                 | 0 cycles | 0                   |
| L1 cache                 | ~1 ns    | ~4                  |
| L2 cache                 | ~4 ns    | ~14                 |
| L3 cache                 | ~15 ns   | ~50                 |
| Main memory              | ~80 ns   | ~250                |
| SSD read                 | ~100 µs  | ~300,000            |
| Network, same datacenter | ~500 µs  | ~1,500,000          |
| Network, cross-continent | ~150 ms  | ~450,000,000        |

Two things fall out of this table, and they shape the entire document.

**First:** caches exist because RAM is slow. The CPU keeps recently-used memory in small fast caches near the core. Code that touches memory it just touched runs vastly faster than code that jumps around. This is why a context switch is expensive even though the switch itself is cheap — the new thread arrives to find the caches full of someone else's data.

**Second, and this is the whole reason async exists:** look at that last row. A network call is *four hundred and fifty million instructions* of doing nothing. If your program's answer to waiting is "hold a thread still," you are holding a megabyte of memory and a scheduler slot for the equivalent of a small eternity.

### How a computer runs a program

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How a computer runs a program</p>

<a id="be-m4"></a>

#### Machine code, assembly, and compilers

Instructions are just bytes in memory. The CPU reads a byte pattern and its circuitry decodes what to do. There is nothing textual about it.

``` csharp
Bytes in memory:    48 01 D0
Assembly mnemonic:  add rax, rdx        (human-readable, 1:1 with the bytes)
C#:                 a = a + b;
```

Each CPU family understands a different set of byte patterns — an **instruction set architecture**. x86-64 (Intel, AMD) and ARM64 (Apple silicon, AWS Graviton, phones) are mutually unintelligible. Machine code compiled for one will not run on the other at all.

That is the entire reason compilers exist, and it is why .NET is built the way it is:

``` text
  C# ──► IL ──► machine code
       ^      ^
       │      └── done at RUN time, by the JIT, for THIS machine
       └───────── done at BUILD time, once, machine-independent

// A .dll containing IL runs on x64 Linux, ARM64 macOS, and x86 Windows
// unchanged, because the machine-specific step is deferred until the
// machine is known (§37).
```

Higher-level languages exist because writing byte patterns by hand does not scale. Every layer — assembly, C, C#, LINQ — is a way of describing intent that some tool translates downward. LINQ compiling into SQL is the same idea one more level up ([Expression trees: how IQueryable works](03-10-collections-and-linq-in-net.md#be-m151)).

> **\[Tip\]**
>
> **You do not need to write assembly, but reading a little is useful.** It settles arguments that are otherwise unresolvable: whether a property access became a field load, whether a struct got copied, whether a bounds check was removed. Two or three lines of disassembly answer questions that no amount of reasoning about the C# will ([Seeing it yourself: measuring layout and allocation](01-05-memory.md#be-m36)).

### Foundations: how .NET stores data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Foundations: how .NET stores data</p>

<a id="be-m22"></a>

#### The memory hierarchy: registers to RAM

One layer further down. "Memory" is not one thing with one speed — it is a hierarchy, and the gaps between levels are enormous. Rough figures for a modern server core:

| Level              | Size            | Latency             | Scaled to human time |
|--------------------|-----------------|---------------------|----------------------|
| Register           | ~1 KB total     | 0 cycles            | instant              |
| L1 cache           | 32–64 KB        | ~4 cycles / 1 ns    | 1 second             |
| L2 cache           | 0.5–2 MB        | ~14 cycles / 4 ns   | 4 seconds            |
| L3 cache           | 8–64 MB, shared | ~40 cycles / 12 ns  | 12 seconds           |
| Main memory (DRAM) | GBs             | ~250 cycles / 80 ns | **1.5 minutes**      |
| NVMe SSD           | TBs             | ~100 µs             | 1 day                |
| Network round trip | —               | ~0.5–50 ms          | 6 days – 1.5 years   |

The consequence is that a modern CPU is almost never limited by arithmetic. It is limited by **waiting for data**. A cache miss costs roughly the time of 250 additions.

Two hardware behaviours follow, and both shape everything in this guide:

- **Memory moves in cache lines of 64 bytes.** Reading one `int` pulls in the 63 bytes around it whether you want them or not. If you use them, the next fifteen reads are free. If you do not, you have paid for 64 bytes to use 4.
- **The prefetcher predicts sequential access.** Walking forward through contiguous memory, the CPU fetches the next lines before you ask, and the misses disappear entirely. Jumping to unpredictable addresses defeats it completely.

``` text
// Same element count, same total work, wildly different runtime:

int[] array;                 // 10M ints = 40 MB, contiguous
// Summing: 64-byte line holds 16 ints -> 1 miss per 16 elements,
// and the prefetcher hides even those. Effectively streaming from DRAM.

LinkedList<int> list;        // 10M nodes, each ~40 bytes, scattered
// Summing: every ->Next is an unpredictable address.
// ~1 cache miss PER ELEMENT, and no prefetching is possible.

// The Big-O is identical: O(n) both ways.
// The measured difference is routinely 10-50x.
```

> **\[Why it matters\]**
>
> **This is the caveat behind the complexity table.** Asymptotics count operations and assume each costs the same. At real sizes, *where* the data sits matters more than *how many* operations you perform. It is why a linear scan of a 100-element array beats a dictionary lookup ([Complexity cheat sheet](03-01-measuring-cost-with-big-o.md#be-m111)), why `List<T>` beats `LinkedList<T>` almost always ([Queue, Stack, LinkedList, PriorityQueue](03-05-heaps-and-priority-queues.md#be-b280)), why struct arrays beat class arrays ([Object header and the real cost of an object](01-05-memory.md#be-m30)), and why the layout discussion in [Data layout: AoS vs SoA and cache behaviour](#be-m281) exists at all.

> **\[Tip\]**
>
> **The one number to keep.** A network round trip is somewhere between 6,000 and 600,000 times a cache miss. That ratio is why [Rules of thumb, ranked](12-04-scaling-and-performance.md#be-m276) puts "remove the round trip" above every other optimisation, and why a hand-tuned inner loop rarely matters in a service that talks to a database.

First principles: [What a CPU actually does](#be-c303)

### Optimization in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Optimization in practice</p>

<a id="be-m281"></a>

#### Data layout: AoS vs SoA and cache behaviour

A cache line is 64 bytes. The CPU fetches whole lines, so reading one field pulls in its neighbours whether you want them or not. Sequential access is roughly two orders of magnitude faster than random access at scale.

``` csharp
// Array of Structs - natural, good when you touch most fields of each item
struct Particle { public float X, Y, Z, VX, VY, VZ; }   // 24 bytes
Particle[] particles;
// Summing all X values reads 24 bytes per particle to use 4. ~17% cache efficiency.

// Struct of Arrays - good when you touch one field across many items
class Particles { public float[] X, Y, Z, VX, VY, VZ; }
// Summing X reads only X. 100% cache efficiency, and vectorisable.
```

``` csharp
// Vectorised sum over a contiguous span:
float Sum(ReadOnlySpan<float> xs) {
    var acc = Vector<float>.Zero;
    int i = 0, w = Vector<float>.Count;
    for (; i <= xs.Length - w; i += w) acc += new Vector<float>(xs.Slice(i, w));
    float total = Vector.Dot(acc, Vector<float>.One);
    for (; i < xs.Length; i++) total += xs[i];
    return total;
}
// Note: TensorPrimitives (.NET 8+) already provides Sum, Dot, Norm, and friends -
// prefer it over hand-rolled Vector<T> where it applies.
```

> **\[Why it matters\]**
>
> **Relevance check.** This matters for numeric loops over millions of elements: analytics, simulation, image and signal processing, tight serialisation. It does not matter for a business application processing 500 orders per request — there, an SoA refactor makes the code worse for no measurable gain. Know which regime you are in.

> **\[Trap\]**
>
> **False sharing.** Two threads writing to different variables that share a cache line will ping-pong that line between cores and destroy throughput. Pad hot per-thread counters to 64 bytes, or use `ThreadLocal<T>`/`[ThreadStatic]` and combine at the end.

Foundations: [The memory hierarchy: registers to RAM](#be-m22)
