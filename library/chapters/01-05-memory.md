---
id: "1-5"
number: "1.5"
title: "Memory"
part: 1
part_title: "The machine"
words: 11946
minutes: 52
---

# 1.5 Memory

> **Quick look.** How memory is organized into stack and heap, and why layout and access order shape performance.
>
> - Memory is a row of numbered bytes; each method call gets a stack frame, freed on return.
> - Objects that outlive a call live on the heap, managed by the garbage collector.
> - Structs are copied on assignment, classes are shared by reference. A struct lives inline wherever its container lives, not always on the stack.
> - Caches load memory in 64-byte lines, so walking data in order is much faster than jumping around.
> - Every .NET object carries header overhead; boxing a value type allocates a hidden heap object.
> - Stack overflow kills the process and can't be caught; out-of-memory means something is still holding references.


### The big picture

Memory is where a running program keeps its data. The processor is extremely fast, but fetching data from main memory is slow by comparison, roughly a hundred times slower than from the processor's own small caches. That gap shapes almost everything about performance.

This chapter explains how memory is organized: numbered bytes, the stack that holds each method call's local data, and the heap that holds objects. It shows what that means in C#, where value types are copied and reference types are shared. And it shows why the order in which you walk through data can make the same work several times faster.

### Before you start

**Builds on:** 1.4 The processor.

**Ideas explained later:**

- **Performance** (12.4): How quickly a system does its work at a given load.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Operating system** (1.7): The software that shares hardware among programs and protects them from each other.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **Garbage collection** (2.7): Automatically finding and freeing memory the program can no longer use.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **LINQ** (2.5): Language Integrated Query: C# methods such as Where, Select and Count that filter and transform sequences of items.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, string interpolation, classes and properties, generic types, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between the stack and the heap?**

> The stack holds one frame per active method call, with its local values. A frame disappears when its method returns. The heap holds objects whose lifetime isn't tied to one call. In .NET, the garbage collector frees heap objects once nothing refers to them.

**2. After var b = a; you change b.X. When does a.X change too?**

> When the type is a class. Then `a` and `b` hold references to the same object. When the type is a struct, `b` is a separate copy and `a` is untouched.

**3. Why can walking a 2D array column by column be several times slower than row by row?**

> The array is laid out row after row in memory. Memory reaches the processor in 64-byte cache lines. Row order uses every value in each line. Column order jumps to a new line on every step and keeps missing the cache.

**4. Roughly how much slower is a read from main memory than from the L1 cache?**

> About 100 times. Around 1 nanosecond for L1 versus around 100 nanoseconds for main memory.

**5. How much memory does a boxed int take on 64-bit .NET?**

> 24 bytes. Every object has 16 bytes of overhead: a header and a pointer to its type. Then come the 4 bytes of the int, rounded up.

**6. Why can a struct with fields byte, long, byte take 24 bytes?**

> Alignment. The `long` must start at a multiple of 8, so 7 bytes of padding go before it, and the struct's size is rounded up to a multiple of 8. Putting the `long` first brings it down to 16.

**7. What is special about arrays of 85,000 bytes or more in .NET?**

> They go on the large object heap. It is only collected during full collections and isn't compacted by default, so churning large arrays is costly.

**8. What is false sharing?**

> Two threads writing different variables that happen to sit in the same 64-byte cache line. The line bounces between their cores, and both slow down.

### A long row of numbered bytes

Main memory, or **RAM**, is a very long row of bytes. Each byte has a number, called its **address**. The first byte is at address 0, the next at 1, and so on.

On a 64-bit machine, addresses are 64-bit numbers. In practice, operating systems use fewer of those bits, but it is still far more than any machine has.

The addresses a program sees are not the real hardware addresses. The operating system translates them, as described in The operating system (1.7). For now, think of memory as one big array of bytes.

RAM is **volatile**. It loses everything when the power goes off. Keeping data is the job of Storage and files (1.6).

### Pointers and references

An address is just a number, so it can be stored in memory like any other number. A stored address is called a **pointer**. It "points" to the data at that address.

C and C++ let you work with pointers directly. You can do arithmetic on them and read any address. That power causes many crashes and security holes.

C#, Java and JavaScript give you **references** instead. A reference is a pointer that the language manages for you. You can't do arithmetic on it or point it at random memory. It is either null or refers to a real object.

### The stack

Each thread of a running program has a region of memory called the **stack**. When a method is called, a block called a **stack frame** is pushed onto it. The frame holds the method's parameters, its local variables and the address to return to.

When the method returns, its frame is popped off. The memory is reused by the next call. Nothing needs to be cleaned up.

The stack is fast, because making and removing a frame just moves one register, the **stack pointer**. It is also small. Typical sizes are 1 MB to 8 MB per thread, depending on the operating system. Very deep or endless recursion runs out of stack.

### The heap

Some data must outlive the method that created it. A list built in one method and returned to the caller is an example. That data goes on the **heap**. The heap is a large area where blocks can be allocated and freed in any order.

Someone has to free heap memory when it is no longer needed. In C you call `free` yourself. In C#, Java, JavaScript and Go, a **garbage collector** finds objects nothing refers to and frees them. That is covered in Memory management and garbage collection (2.7).

### Value types and reference types in C#

C# splits types into two kinds.

**Value types** hold their data directly. These are `struct`s, including `int`, `double`, `bool`, `DateTime` and `decimal`. Assigning one copies all of its data.

**Reference types** live on the heap. These are `class`es, including `string`, arrays and `List<T>`. A variable of a reference type holds only a reference. Assigning one copies the reference, so both variables now refer to the same object.

A common simplification says "structs live on the stack". That is not quite right. A value type lives wherever its container lives. A local `int` lives in the stack frame. An `int` field of a class lives inside that object on the heap. An array of structs stores the structs side by side inside the array, on the heap.

**Boxing** happens when a value type is stored somewhere that expects a reference, such as an `object` variable. The runtime copies the value into a new small heap object.

```csharp
// memory.cs — run with: dotnet run -c Release memory.cs
using System.Diagnostics;

// Value type: assignment copies the data
var s1 = new PointStruct { X = 1 };
var s2 = s1;
s2.X = 99;
Console.WriteLine(s1.X);   // 1

// Reference type: assignment copies the reference
var c1 = new PointClass { X = 1 };
var c2 = c1;
c2.X = 99;
Console.WriteLine(c1.X);   // 99

// Boxing copies the value into a new heap object
int n = 42;
object boxed = n;
n = 7;
Console.WriteLine(boxed);  // 42

// Cache effects: same work, different order
const int N = 4096;                    // 4096 × 4096 ints = 64 MB
var grid = new int[N, N];
long sum = 0;
var sw = Stopwatch.StartNew();
for (int r = 0; r < N; r++)
    for (int c = 0; c < N; c++)
        sum += grid[r, c];             // walks memory in order
Console.WriteLine($"row by row:       {sw.ElapsedMilliseconds} ms");
sw.Restart();
for (int c = 0; c < N; c++)
    for (int r = 0; r < N; r++)
        sum += grid[r, c];             // jumps 16 KB between reads
Console.WriteLine($"column by column: {sw.ElapsedMilliseconds} ms");
Console.WriteLine(sum);                // using the result stops the loops being removed

struct PointStruct { public int X; }
class PointClass { public int X; }
```

### Alignment and padding

Processors read memory fastest when a value starts at an address that is a multiple of its size. A 4-byte `int` should start at a multiple of 4. An 8-byte `long` should start at a multiple of 8. This is called **alignment**.

To keep fields aligned, the compiler inserts unused bytes between them, called **padding**. Field order changes how much padding is needed. A `byte`, then a `long`, then a `byte` needs 7 bytes of padding after the first byte and 7 at the end: 24 bytes in total. Put the `long` first and the struct shrinks to 16.

For structs, C# keeps fields in the order you wrote them by default. `[StructLayout(LayoutKind.Auto)]` lets the runtime reorder them to save space. Classes are already laid out automatically.

### What an object costs in .NET

Every object on the .NET heap carries 16 bytes of overhead on a 64-bit machine. There is an 8-byte **object header**, used for locking and hashing, and an 8-byte pointer to the object's type. The smallest object is 24 bytes.

This overhead adds up. A boxed `int` holds 4 useful bytes in 24. An `int[]` of a million values uses about 4 MB. A `List<object>` holding the same million values as boxed ints uses about 8 MB for the references and 24 MB for the boxes. That is eight times the memory, spread across the heap where the cache handles it badly.

Strings are objects too. A string costs about 20 bytes plus 2 bytes per character, rounded up to a multiple of 8.

Arrays of 85,000 bytes or more go to a separate area called the **large object heap**. Copying big arrays around is expensive, so the collector leaves them in place. That makes the large object heap prone to gaps, and it is only cleaned up in full collections. Programs that create and drop many large arrays pay for it.

```csharp
// layout.cs — run with: dotnet run layout.cs
using System.Buffers;
using System.Runtime.CompilerServices;

Console.WriteLine(Unsafe.SizeOf<Padded>());   // 24
Console.WriteLine(Unsafe.SizeOf<Packed>());   // 16

long before = GC.GetAllocatedBytesForCurrentThread();
object[] boxes = new object[1000];
for (int i = 0; i < boxes.Length; i++) boxes[i] = i;       // 1000 boxing operations
long after = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"1000 boxed ints: {after - before} bytes");  // about 32,000: an 8 KB array plus 24 KB of boxes

before = GC.GetAllocatedBytesForCurrentThread();
Span<int> squares = stackalloc int[16];                    // on the stack: nothing for the collector
for (int i = 0; i < squares.Length; i++) squares[i] = i * i;
after = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"stackalloc: {after - before} bytes, last square {squares[15]}");  // 0 bytes, 225

int[] rented = ArrayPool<int>.Shared.Rent(10_000);         // may be longer, may hold old data
try { Console.WriteLine(rented.Length >= 10_000); }        // True
finally { ArrayPool<int>.Shared.Return(rented); }          // give it back for reuse

#pragma warning disable CS0649   // these structs exist only to be measured, so their fields are never set
struct Padded { public byte A; public long B; public byte C; }
struct Packed { public long B; public byte A; public byte C; }
```

### Caches

Main memory is slow compared with the processor. A read from RAM takes around 100 nanoseconds. In that time a processor could run hundreds of instructions.

So processors keep small, fast copies of recently used memory in **caches**. There are usually three levels. **L1** is tiny and fastest, per core. **L2** is larger, usually per core. **L3** is larger still and shared between cores.

Memory moves into the cache in blocks of 64 bytes, called **cache lines**. When you read one `int`, the 15 `int`s next to it arrive too. The processor also notices steady patterns and fetches the next lines before you ask. This is called **prefetching**.

The rule that follows is **locality**. Code is fast when it uses memory that is close together and in order. Arrays are laid out side by side and are cache-friendly. Linked structures, where each item holds a reference to the next somewhere else on the heap, jump around memory and miss the cache often.

### Cost model

Typical latencies on current hardware. The right column scales them so that one L1 read takes one second.

| Where the data is | Roughly | If L1 took 1 second |
|---|---:|---:|
| L1 cache | 1 ns | 1 second |
| L2 cache | 3–5 ns | 4 seconds |
| L3 cache | 10–20 ns | 15 seconds |
| Main memory | 80–120 ns | 1.5 minutes |
| NVMe SSD read | 20–100 µs | 6 hours to a day |
| Network round trip in one data center | 0.2–0.5 ms | 2 to 6 days |
| Network round trip across an ocean | 70–150 ms | 2 to 5 years |

Two lessons follow. Memory access order matters more than instruction count inside a program. And one database or network call costs more than millions of in-memory operations, which is why ORMs and EF Core (7.7) spends so much time on avoiding extra queries.

Each .NET object costs 16 bytes of overhead, with 24 bytes as the minimum. Allocating in .NET is cheap, a few nanoseconds, because the runtime just moves a pointer forward. The real cost comes later, when the collector has to find and free the garbage. A bounds check costs about one cycle, and the JIT often removes it.

### Working without allocating

Modern .NET gives you tools to avoid heap allocations in hot code.

`Span<T>` is a view onto a run of values: part of an array, a block on the stack, or native memory. Slicing it copies nothing. It is a special kind of struct, called a `ref struct`, that can only live on the stack. The compiler stops it from escaping into a field or outliving what it points at.

`stackalloc` creates a small buffer on the stack. It disappears when the method returns, so the collector never sees it. Keep it small, a few kilobytes at most, because the stack is small.

`ArrayPool<T>` lends out arrays and takes them back for reuse. It suits large temporary buffers, which would otherwise land on the large object heap. `Memory<T>` is like `Span<T>` but can be stored in fields and used across `await`.

These tools matter in code that runs millions of times a second, such as parsers, serializers and servers. In ordinary business code, plain arrays and lists are fine.

### False sharing

Caches work in 64-byte lines, and each core has its own. When one core writes to a line, other cores' copies of that line become invalid and must be fetched again.

Now imagine two threads each updating their own counter, and the two counters sit next to each other in memory. They share a cache line. Every write by one thread invalidates the other's copy. The line bounces between the cores, and both threads slow down badly, even though they never touch the same variable. This is **false sharing**.

The fix is to keep data written by different threads at least 64 bytes apart, or to let each thread work on its own local value and combine the results at the end. The second approach is usually simpler.

Large servers add one more layer. A machine with several processor sockets attaches memory to each socket. Reading memory attached to another socket is slower. This design is called **NUMA**, non-uniform memory access. The runtime and OS handle most of it, but it shows up in benchmarks on big machines.

### Other ways to manage memory

Languages differ mainly in who frees heap memory.

C and C++ make you do it yourself. Forgetting leaks memory. Freeing twice, or using memory after freeing it, corrupts the program. Rust checks ownership at compile time, so memory is freed automatically with no collector. Swift and Python count references to each object and free it when the count reaches zero. C#, Java, JavaScript and Go use a tracing garbage collector.

### Memory safety

In C and C++, nothing stops a program from writing past the end of an array. The write lands in whatever sits next: another variable, or the return address in a stack frame. This is a **buffer overflow**. Using memory after it has been freed is another classic bug. Attackers use both to take control of programs.

Microsoft and Google have each reported that about 70% of their serious security bugs were memory-safety problems of this kind. Security agencies now recommend memory-safe languages for new code.

C# is memory-safe by default. Every array access is checked against the array's length and throws `IndexOutOfRangeException` if it is out of range. The JIT removes the check when it can prove the index is always valid, such as in a loop from 0 to `a.Length`. Code marked `unsafe` and calls into native libraries give up that protection.

### In short

Memory is a long row of numbered bytes. Each method call gets a frame on its thread's stack, freed when the call returns, while objects that outlive a call live on the heap. In C#, structs are copied on assignment and classes are shared through references. Caches move memory in 64-byte lines, so code that reads memory in order is fast and code that jumps around is slow. Every .NET object carries overhead, which is why arrays of plain values beat collections of small objects for bulk work.

### What goes wrong

**Stack overflow.** Deep or endless recursion runs out of stack. In .NET a `StackOverflowException` cannot be caught, and the whole process dies. Turn deep recursion into a loop with an explicit `Stack<T>`.

**Mutable structs.** Changing a copy instead of the original loses the update. This happens with structs returned from properties or stored in read-only fields. Make structs `readonly` and create new values instead of changing old ones.

**Large structs copied everywhere.** Every assignment and method call copies all the bytes. Keep structs small, roughly 16 bytes or less, or pass them with `in` or `ref`.

**Leaks with a garbage collector.** The collector only frees objects nothing refers to. A static list that only grows, a cache without a size limit, or an event handler that is never removed keeps objects alive forever.

**Boxing in hot loops.** Storing value types in non-generic collections or `object` variables creates a heap object each time. That adds garbage collection work.

**Pointer-chasing data structures.** Linked lists and trees of small objects scatter data across the heap. Walking them misses the cache on nearly every step.

**Trusting rented arrays.** `ArrayPool.Rent` may return a longer array than you asked for, full of data from its last user. Track the length you asked for, and never use the array after returning it.

**Large arrays in a loop.** Allocating a new 1 MB buffer per request fills the large object heap and triggers full collections. Pool or reuse large buffers.

**False sharing in counters.** A parallel loop that increments per-thread counters stored side by side in an array runs slower than a single thread. Keep a local counter per thread and add them up at the end.

**Stepping outside memory safety.** `unsafe` code, `Marshal` calls and native libraries can corrupt memory in ways that crash far from the bug. Keep such code small and well tested.

### In other languages (optional)

**JavaScript.** Objects live on the heap. V8 stores small integers directly in the value without allocating. Other numbers may be boxed on the heap. Typed arrays such as `Int32Array` store raw values side by side, like a C# `int[]`.

**Python.** Every value is an object on the heap, even a small integer. `sys.getsizeof(1)` is 28 bytes on a 64-bit machine. A list of integers is a list of references. The `array` module and NumPy store raw values side by side instead.

**Go.** The compiler decides whether each value lives on the stack or the heap, using **escape analysis**. .NET does a limited version of this too, in recent versions.

**Rust.** Values live on the stack by default. `Box`, `Vec` and similar types put data on the heap. Ownership rules checked at compile time decide when it is freed.

**SQL Server.** Data is stored in 8 KB pages. The **buffer pool** keeps recently used pages in memory, which is why a database server uses as much RAM as you give it.

### Key terms

- **RAM**: Main memory. Fast compared with storage, slow compared with caches, and lost when the power goes off.
- **Address**: The number that identifies one byte of memory.
- **Pointer**: A stored memory address. C and C++ let you work with pointers directly.
- **Reference**: A pointer managed by the language. It is either null or refers to a real object.
- **Stack**: Per-thread memory holding one frame per active method call. Freed automatically on return.
- **Stack frame**: The block on the stack for one method call: parameters, locals and the return address.
- **Heap**: A large memory area for data whose lifetime isn't tied to one method call.
- **Value type**: A type that holds its data directly and is copied on assignment. In C#, a struct.
- **Reference type**: A type that lives on the heap and is reached through references. In C#, a class.
- **Boxing**: Copying a value type into a new heap object so it can be treated as an object.
- **Cache line**: The 64-byte block in which memory moves between RAM and the processor's caches.
- **Locality**: Using memory that is close together and in order. The key to cache-friendly code.
- **Alignment**: Placing a value at an address that is a multiple of its size, so it can be read quickly.
- **Large object heap**: The area for .NET objects of 85,000 bytes or more. Collected only in full collections.
- **False sharing**: Threads slowing each other down by writing different variables in the same cache line.
- **Buffer overflow**: Writing past the end of a buffer into neighbouring memory. A classic security hole.

### Where this leads

The operating system's part in memory, virtual addresses and paging, is in The operating system (1.7). How languages treat values and references is in Values, variables and references (2.4). The collector is in Memory management and garbage collection (2.7). Why arrays usually beat linked lists is in Arrays and lists (3.2). Measuring memory-bound code is in Scaling and performance (12.4).

### Check yourself

1. Run the cache demo with N set to 256, 1024 and 4096. Explain why the gap between the two orders grows with N.
2. Write `readonly struct Money { public decimal Amount { get; } public string Currency { get; } }`. Describe where each part lives when many of them sit in a `List<Money>`.
3. Explain why summing a `List<int>` is faster than summing a `LinkedList<int>` of the same size.
4. Use `Unsafe.SizeOf` to measure three structs from a codebase you know. Reorder their fields to remove padding, if you can.
5. Measure `GC.GetAllocatedBytesForCurrentThread()` around a LINQ query and around the same logic as a plain loop. Explain the difference.
6. Write a parallel loop that counts even numbers, first with a shared counter array indexed by thread, then with local counters. Time both.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A mutable struct that never changed

A seat map stores `List<SeatState>` where SeatState is a struct. Marking a seat as taken has no effect.

*Before*

```csharp
struct SeatState { public bool Taken; }
var seats = new List<SeatState>(new SeatState[180]);
var s = seats[12];      // a COPY of the struct
s.Taken = true;         // changes the copy only
```

*After*

```csharp
readonly record struct SeatState(bool Taken);   // immutable: no silent copies to mutate
seats[12] = seats[12] with { Taken = true };      // replace the element explicitly

// or use an array, whose indexer returns a reference:
var arr = new SeatState[180];
arr[12] = new SeatState(true);
```

> **Lesson:** Structs are copied on every read from a collection. Make them immutable so the bug can't be written.

#### Example 2: Memory grows until the pod is OOM-killed

A pricing API's memory climbs steadily over two days, then Kubernetes kills it with exit code 137.

*Find who holds the memory*

```bash
dotnet-counters monitor -p <pid> System.Runtime        # is the GC heap growing, or native memory?
dotnet-gcdump collect -p <pid> -o before.gcdump         # take two snapshots 10 minutes apart
dotnet-gcdump collect -p <pid> -o after.gcdump
# Compare in Visual Studio or PerfView: sort by count growth, find the first domain type,
# then follow its path to a GC root (here: a static Dictionary used as a cache).
```

> **Lesson:** A GC does not prevent leaks: anything still referenced stays alive. Diff two heap snapshots and follow the retention path to the root.

### The Memory Model in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · The Memory Model in Depth</p>

<a id="dn-90"></a>

#### Stack vs heap — what actually decides placement

| | **Stack** | **Heap** |
|---|---|---|
| Holds | Locals of the current call — value types, plus *references* to heap objects | The objects themselves; all reference types |
| Freed by | Popping the frame on return, automatic | GC, when unreachable |
| Lifetime rule | **Cannot outlive the method call** | Lives until nothing references it |
| Size | ~1MB per thread | Gigabytes |
| Sharing | One thread, never shared | Shared across threads |
| Allocation cost | Pointer bump, effectively free | Cheap to allocate, paid later in GC |

The common mental model — "stack for short-lived, heap for long-lived" — is wrong. Gen 0 is full of short-lived heap data; that's the entire premise of generational collection. What decides placement is **type plus declaration site**:

```csharp
void Method()
{
    int x = 5;               // stack - a local value type
    var order = new Order(); // the OBJECT is on the heap;
                             // the REFERENCE is a stack slot
    Point p = default;       // struct local -> entirely on the stack
}

class Order
{
    public int Total;        // an int, but it lives on the HEAP inside the Order
    public Point Ship;       // a struct field, also on the heap
}
```

That last case is what the lifetime model can't explain. **"Struct" doesn't mean "stack"** — it means "stored inline wherever its container lives."

Four more ways a value type ends up on the heap:

```csharp
int captured = 5;
Func<int> f = () => captured;   // closure -> captured becomes a field of a heap object

async Task M()
{
    int local = 5;
    await Something();          // state machine boxed; local becomes a field
    Use(local);
}

object boxed = 42;              // boxing
var list = new List<Point>();   // the backing array is on the heap
```

The real constraint is simply: **anything that must survive past the method return, or be shared with another thread, has to be on the heap.**

<a id="dn-93"></a>

#### OutOfMemoryException vs StackOverflowException

| | **OutOfMemoryException** | **StackOverflowException** |
|---|---|---|
| Region | Heap | Thread stack |
| Means | The GC already ran and it didn't help | The call chain exceeded ~1MB |
| Catchable | Yes (rarely useful) | **No — the process dies instantly** |
| `finally` / `using` run? | Yes | **No** |
| Logs written? | Usually | Usually none |
| Fix | Find the holder: `dumpheap -stat` → `gcroot` | Recursion → explicit `Stack<T>` |

##### Why the GC "can't handle" OOM
By the time OOM is thrown the GC has **already run and failed**:

```
allocate -> no space -> gen 0 GC -> still none
         -> blocking gen 2 GC -> still none
         -> compact/decommit -> still none
         -> throw OutOfMemoryException
```

Four distinct reasons this happens, and only one is the GC's doing:

1. **Everything is still reachable.** The GC's contract is to free what nothing references. If you hold a reference, the object is *live* by definition. It can see the memory is enormous; it cannot judge that you no longer *need* it. Reachability is mechanical; usefulness is semantic. This is the majority of real OOMs — and it's a bug in your code.
2. **Fragmentation.** You need a contiguous 100MB block; 500MB is free but the largest hole is 40MB. The LOH signature, since it's swept and not compacted.
3. **Pinning.** `fixed` blocks and `GCHandleType.Pinned` make objects immovable because native code holds raw addresses. Scattered pins act as pegs that prevent compaction around them.
4. **Hard limits.** A single object can't exceed 2GB without `gcAllowVeryLargeObjects` (and `Array.Length` is still `int`); 32-bit processes cap at ~2–3GB of address space.

So **OOM doesn't always mean "no free memory" — sometimes it means "no free memory in the shape you asked for."**

Underneath all of this is a hard theoretical floor: deciding whether a reachable object will ever be used again is equivalent to the halting problem. Reachability is the only safe approximation, and it's deliberately conservative — it keeps things you'll never touch, but never frees something you might.

```csharp
// Fragmentation escape hatch - expensive, use once
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
```

##### The container twist
In Kubernetes you often **never see an `OutOfMemoryException`**. The kernel's OOM killer sends SIGKILL at the cgroup limit and the process vanishes with **exit code 137** — no exception, no stack trace, no logs.

```bash
kubectl describe pod <pod>   # Last State: Terminated, Reason: OOMKilled, Exit Code: 137
```

A managed OOM in the logs means the GC gave up first; exit 137 means the kernel did. Different diagnosis, different fix.

<a id="dn-94"></a>

#### Why stack overflows happen

Each call pushes a frame: return address, saved registers, parameters, locals, and inline struct values. Frames pop only on return. Go deep enough and the stack pointer crosses a **guard page**, the OS raises an access violation, and the CLR calls `FailFast` — there's no stack left on which to run a handler.

~1MB per thread isn't a usable frame count: a trivial method might use 50–100 bytes per frame (tens of thousands deep), one holding large structs several KB (a few hundred deep).

```csharp
// 1) The accidental self-reference - still the most common real cause
public string Name { get => Name; set => Name = value; }   // meant _name

// 2) Indirect recursion you didn't notice
public override string ToString() => $"Order {this}";      // $"{this}" calls ToString()

// 3) Legitimate recursion on unexpectedly deep data
int Depth(Node n) => n.Children.Count == 0 ? 1 : n.Children.Max(Depth);
// fine on a 10-level tree, fatal on one shaped like a linked list
// (deeply nested JSON/XML is the security version of this)

// 4) stackalloc in a loop - scoped to the METHOD, not the iteration
while (true) { Span<byte> b = stackalloc byte[1024]; }
```

**C# has no tail-call elimination.** The CLR has a `.tail` IL prefix but C# essentially never emits it (F# does), so recursion always costs stack no matter how it's written.

```csharp
// Guard: throws something you CAN catch. Roslyn and System.Text.Json do this.
RuntimeHelpers.EnsureSufficientExecutionStack();   // InsufficientExecutionStackException

// Real fix: move the depth to the heap - gigabytes instead of one megabyte
var stack = new Stack<Node>();
stack.Push(root);
while (stack.TryPop(out var n))
    foreach (var c in n.Children) stack.Push(c);
```

<a id="dn-95"></a>

#### Infinite loop vs infinite recursion

| | Infinite loop | Infinite recursion |
|---|---|---|
| Stack | Constant — one frame | One frame per call |
| Result | **Hangs forever**, pegs a core | Dies in milliseconds |
| Process | Alive but stuck | Gone, `FailFast` |

A loop calls nothing, so nothing is pushed — `while(true){}` never overflows. Variants worth knowing: a loop that *allocates* gives OOM; a loop containing `stackalloc` **does** overflow; a blocking loop inside async code starves the thread pool instead.

Operationally the loop is often **worse**: the process stays alive and keeps passing health checks while one thread burns a core and one request hangs forever, so the orchestrator never intervenes. A stack overflow at least gets you a restart. This is why `while (true)` in production code should always carry a `CancellationToken` check or a bounded iteration count.

##### Interview answer
> "Stack holds call frames and is bounded at about a megabyte per thread; heap holds objects and is bounded by the process or cgroup limit. A stack overflow means the call chain got too deep and is uncatchable — the process dies with no finally blocks and usually no logs. An OOM means the GC already ran and everything left is either still reachable or unusable due to fragmentation, so the debugging question is always 'who holds the reference'. In containers you often get neither exception: the kernel kills you at the cgroup limit with exit code 137."

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a14"></a>

#### Memory, addresses, the stack and the heap

Everything in the next few sections rests on this, and most explanations assume it rather than state it. If you already know what a pointer is and where a stack frame lives, skip ahead — if not, this is the groundwork the rest of the part is built on.

##### Memory is one enormous numbered array

``` text
  a process sees memory as a flat sequence of BYTES, each with a number.
  that number is its ADDRESS.

    address    0x7FF4A0   0x7FF4A1   0x7FF4A2   0x7FF4A3   0x7FF4A4 ...
    contents   [  2A  ]   [  00  ]   [  00  ]   [  00  ]   [  ..  ]
               \_______________________________________/
                     an int (4 bytes) holding 42

  a VARIABLE is a name the compiler gives to a location.
    int x = 42;      ->  "the 4 bytes at 0x7FF4A0 contain 42"

  a POINTER (or REFERENCE) is a variable whose contents are ANOTHER ADDRESS.
    on 64-bit, an address is 8 bytes.

    address    0x7FF4B0                        0x1C2E40
    contents   [ 40 2E 1C 00 00 00 00 00 ] --> [ the actual object ]
               \___________________________/
                a reference: 8 bytes holding the address of something else

  that is the whole idea. "The variable holds a pointer" means those 8 bytes
  contain a number that says where the real data is.
```

Two things follow immediately, and they are the source of a great deal of confusion later. First, copying a reference copies the *address*, not the thing — so two variables now name the same object. Second, reaching the object costs an extra step: read the address, then go there. That second step is what “indirection” means, and it is not free.

##### The stack: automatic, fast, and small

``` text
  each THREAD gets its own stack - a contiguous block, typically 1 MB.
  it grows and shrinks automatically as methods call and return.

  calling  Main() -> Process() -> Calculate()

    high addresses
    +----------------------------+
    | Main's frame               |   locals of Main
    |   int total                |
    +----------------------------+
    | Process's frame            |   pushed when Process is CALLED
    |   int i                    |
    |   return address           |   <- where to resume in Main
    +----------------------------+
    | Calculate's frame          |   pushed when Calculate is CALLED
    |   double result            |
    |   return address           |
    +----------------------------+  <- STACK POINTER
    low addresses                     (grows downward on x86/x64)

  when Calculate RETURNS, its frame is gone. Not cleaned up - the stack
  pointer simply moves back up. That is why stack "allocation" is free:
  it is one arithmetic operation on a register.
```

| Property of the stack          | Consequence                                                   |
|--------------------------------|---------------------------------------------------------------|
| One per thread                 | No sharing, no locking, no contention                         |
| Allocation is moving a pointer | Effectively free — a single instruction                       |
| Freed automatically on return  | No cleanup, no garbage collector involvement                  |
| Strictly last-in first-out     | A frame cannot outlive its caller                             |
| **Small** — ~1 MB by default   | Deep recursion overflows it                                   |
| Hot in CPU cache               | The top of the stack was just touched, so access is very fast |

> **\[Trap\]**
>
> **Common trap.** A `StackOverflowException` cannot be caught and cannot be recovered from — the process is terminated immediately, with no `finally` blocks and no logging. Almost always the cause is unbounded recursion, and occasionally a very large `stackalloc`. This is one of the few failures in .NET where your code gets no opportunity to respond at all.

##### The heap: shared, flexible, and managed

``` text
  the heap is one region shared by the WHOLE PROCESS.
  objects live here when their lifetime is not tied to a method call.

    new Customer()  ->  the runtime finds space on the heap,
                        writes the object there,
                        and gives you back its ADDRESS.

  a heap object in .NET is not just your fields:

    0x1C2E40  +----------------------------+
              | sync block index    8 bytes|  locking, hash code
              +----------------------------+
    0x1C2E48  | method table pointer 8 bytes|  WHICH TYPE this is
              +----------------------------+  <- the reference points HERE
              | int Id               4 bytes|
              | int Age              4 bytes|
              +----------------------------+
                                    = 24 bytes for 8 bytes of data

  the 16 bytes of header are what make virtual calls, type checks,
  locking and garbage collection possible. Every heap object pays them.
  the minimum size of any object on 64-bit is 24 bytes.
```

| Property of the heap              | Consequence                                                              |
|-----------------------------------|--------------------------------------------------------------------------|
| Shared across threads             | Concurrent access needs synchronisation                                  |
| Arbitrary lifetime                | Something must decide when to free — that is the GC                      |
| 16 bytes of header per object     | Tiny objects are mostly overhead                                         |
| Objects are scattered             | Following references jumps around memory, which defeats CPU caching      |
| Large — limited by process memory | You can hold far more than the stack allows                              |
| **Objects can be moved**          | The GC compacts, so an address is not stable — hence pinning for interop |

##### What allocation actually costs

``` csharp
var c = new Customer();     // what really happens

// 1. ALLOCATION - genuinely cheap in .NET
//    gen 0 is a contiguous region with a single "next free" pointer.
//    allocating is:  address = next;  next += size;
//    that is a pointer bump - a few nanoseconds. Faster than malloc.

// 2. ZEROING - the runtime clears the memory, so fields start at
//    default(T) rather than whatever was there before.

// 3. THE HEADER - method table pointer written, so the runtime knows the type.

// 4. ...and then, LATER, the cost that actually matters:
//    when gen 0 fills, the GC runs. It must find every LIVE object,
//    which means pausing threads and walking references.
//
//    so allocation is cheap and COLLECTION is not. Allocating in a hot
//    loop is not slow because `new` is slow - it is slow because it
//    forces collections to happen more often.
```

##### Putting it together

``` text
  void Process()
  {
      int count = 3;                       // value type, local
      Point origin = new Point(0, 0);      // STRUCT: a value type
      Customer c = new Customer();         // CLASS: a reference type
      int[] scores = new int[3];           // arrays are always objects
  }

  STACK (this thread, this frame)          HEAP (shared)

  +--------------------------+
  | count      : 3           |             0x1C2E40 +------------------+
  | origin.X   : 0           |  <- the             | header (16 B)    |
  | origin.Y   : 0           |     struct IS       | Id, Age ...      |
  |                          |     here            +------------------+
  | c          : 0x1C2E40 ---|---------------------^
  | scores     : 0x1C3100 ---|--------+
  +--------------------------+        |    0x1C3100 +------------------+
                                      +----------->| header + length  |
  origin lives ON THE STACK because a              | 0 | 0 | 0        |
  struct is stored WHERE IT IS DECLARED.           +------------------+

  c and scores are 8-byte ADDRESSES on the stack, pointing at objects
  on the heap. When Process returns, the stack frame vanishes instantly -
  but the two heap objects remain until the GC proves nothing references
  them any more.
```

| Where does it actually live?                    | Answer                                                       |
|-------------------------------------------------|--------------------------------------------------------------|
| A local `int`                                   | The stack frame                                              |
| A local struct                                  | The stack frame, inline                                      |
| A struct that is a *field of a class*           | **The heap**, inside that object                             |
| A struct in a `List<T>` or array                | **The heap**, inline in the backing array                    |
| Any `class` instance                            | The heap                                                     |
| A local captured by a lambda                    | **The heap** — the compiler hoists it into a generated class |
| A local in an `async` method, across an `await` | **The heap** — it moves into the state machine               |
| A `string`                                      | The heap, always — and it is immutable                       |
| A boxed value type                              | The heap, wrapped in a new object                            |

> **\[Note\]**
>
> **The statement to carry forward.** “Stack versus heap” is a useful first picture and a *bad rule*, because half the table above contradicts it. The reliable formulation is: **value types are copied; reference types are aliased** — and *where* the bytes sit follows from what contains them, not from the type alone. Every surprise in the next section is an instance of that one sentence.

###### Seeing it: how to actually measure allocations

``` csharp
// BenchmarkDotNet is the only honest way to compare two implementations
[MemoryDiagnoser]                    // <- the attribute that adds the columns
public class Bench
{
    private readonly string[] _items = Enumerable.Range(0, 100).Select(i => $"x{i}").ToArray();

    [Benchmark(Baseline = true)]
    public string Concat() { var s = ""; foreach (var i in _items) s += i; return s; }

    [Benchmark]
    public string Builder()
    { var sb = new StringBuilder(); foreach (var i in _items) sb.Append(i); return sb.ToString(); }

    [Benchmark]
    public string Join() => string.Join("", _items);
}

// |  Method  |      Mean |  Gen0  | Allocated |
// |----------|----------:|-------:|----------:|
// | Concat   | 4,812 ns  | 6.1035 |   25.6 KB |   <- 100 intermediate strings
// | Builder  |   931 ns  | 0.4578 |    1.9 KB |
// | Join     |   612 ns  | 0.2136 |    0.9 KB |
//
// read the ALLOCATED column first. Mean time varies with the machine;
// allocation does not, and it predicts how the code behaves under load.
```

| Signal                      | Read it as                                                  |
|-----------------------------|-------------------------------------------------------------|
| `Gen0` per 1,000 operations | How often you force the cheapest collection                 |
| `Gen1` non-zero             | Objects are surviving gen 0 — something holds them briefly  |
| **`Gen2` non-zero**         | **Expensive.** Long-lived objects, or the large object heap |
| `Allocated` per operation   | The number to optimise. Zero is achievable for hot paths    |

``` text
# in a running service, rather than a benchmark
dotnet-counters monitor -p <pid> System.Runtime
#   gc-heap-size            total managed heap
#   gen-0-gc-count          per interval - thousands/sec means allocation pressure
#   gen-2-gc-count          should be RARE. Frequent gen 2 is a real problem
#   alloc-rate              bytes per second allocated
#   time-in-gc              % of CPU spent collecting. Over ~10% is a smell
#   loh-size                large object heap - anything >= 85,000 bytes

# and to see WHAT is allocated
dotnet-gcdump collect -p <pid>       # then open in Visual Studio or PerfView
dotnet-counters collect -p <pid> --format json
```

The threshold worth carrying: **allocation is only a problem when it causes collection**. A service allocating 50 MB/s with a 1 GB heap and 2% time-in-GC is fine. The same service at 40% time-in-GC is spending nearly half its CPU on bookkeeping. Measure `time-in-gc` before optimising any allocation, because the allocation itself was probably never the cost.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b235"></a>

#### Stack and heap, precisely

**The stack** is per-thread, strictly last-in-first-out. A method call pushes a frame holding parameters, locals, and the return address; returning pops it. Allocation is one instruction (move the stack pointer), deallocation is free, and there is no GC involvement. Fixed size, typically 1 MB per thread.

**The heap** is shared across all threads, unordered, and garbage collected. Objects live until nothing can reach them.

The rule everyone half-remembers is wrong in a way that matters:

> "Value types go on the stack, reference types go on the heap."

The accurate version: **a value type lives wherever its container lives.**

``` csharp
void M()
{
    int a = 1;                    // stack — it's a local
    var p = new Point(1,2);       // stack — struct local
    var c = new Container();      // heap — the object
                                  // c.Count (an int field) is ON THE HEAP, inside c
    int[] arr = new int[100];     // the array is on the heap; all 100 ints are in it
}
class Container { public int Count; }
```

Reference types are always on the heap (barring escape analysis, which .NET does not currently do), and the *reference* — 8 bytes on x64 — lives wherever you put it.

<a id="be-b237"></a>

#### Object layout on the heap

A reference-type instance on x64 carries overhead before any of your data:

``` text
┌────────────────────────┐
│ sync block index  8 B  │  ← lock state, hash code; sits at a NEGATIVE offset
├────────────────────────┤
│ method table ptr  8 B  │  ← the reference points HERE
├────────────────────────┤
│ your fields...         │
└────────────────────────┘
```

Consequences:

- **Minimum object size is 24 bytes** on x64, even for an empty class. An object with a single `int` field costs 24 bytes, not 4.
- **`class` with one `int` is 24 bytes; `struct` with one `int` is 4.** At a million instances that's 24 MB versus 4 MB, plus GC tracking for the first.
- Arrays add a length field: `int[10]` is 24 + 40 = ~64 bytes.

**Field ordering and padding.** The CLR aligns fields to their size, so field order changes struct size:

``` text
struct Bad  { byte a; long b; byte c; }   // 24 bytes (1+7pad, 8, 1+7pad)
struct Good { long b; byte a; byte c; }   // 16 bytes (8, 1, 1, 6pad)
```

Classes default to `LayoutKind.Auto` — the runtime reorders fields to pack them, so you rarely worry. **Structs default to `LayoutKind.Sequential`**, preserving your declaration order, which is why struct field ordering can matter for size. Declare largest-first as a habit.

### The Foundation

<p class="src">From <em>Backend Engineering</em> · async / await · The Foundation</p>

<a id="be-c312"></a>

#### Two kinds of memory

Every process has both. They differ in lifetime and ordering, and that difference is the whole story.

**The stack** grows and shrinks as functions call each other. Each call pushes a **frame**: parameters, locals, return address, saved registers. Return pops it.

``` text
void A() { int x = 1; B(); }
void B() { int y = 2; C(); }
void C() { int z = 3; }
```

Inside `C`, the stack reads bottom-up: `A`'s frame (holding `x`), then `B`'s (holding `y`), then `C`'s (holding `z`). When `C` returns, its frame pops and that space is instantly reusable by the next call.

Mechanically it's one register — the stack pointer. Allocating a frame is *subtract from the stack pointer*. Deallocating is *add back*. One instruction each. No bookkeeping, no GC, no free list. This is why stack allocation is effectively free.

The price of that speed is the constraint: **strictly last-in-first-out.** You cannot pop `B`'s frame while `C`'s sits above it. You cannot lift a frame out of the middle and set it aside. The order of destruction is forced to be the reverse of the order of creation.

The stack has a fixed maximum size, decided when the thread is created. Exceed it — usually by unbounded recursion — and you get `StackOverflowException`, which in .NET is **uncatchable** and terminates the process immediately. The frame is the reason: there's no room left to run a handler.

**The heap** holds objects. Ask for a chunk (`new`), get a **reference**, and the object lives until nobody can reach it. No ordering rules whatsoever — object \#1 can outlive object \#500 or vice versa. Allocation is a pointer bump in the gen-0 nursery; reclamation is the GC's job, on its own schedule.

One correction to a very common misreading: **the heap does not "store references." It stores objects.** The reference is the small thing, and it lives wherever you put it — a local (stack), a field of another object (heap), a static. `string sql` inside a method is an 8-byte pointer sitting in a stack frame; the characters were always on the heap.

This matters concretely in Part II. When the compiler hoists `sql` into a field of the state machine, **the string data doesn't move** — it was on the heap already. What moves is the *pointer* that had been sitting in the stack frame.

Side by side:

|                               | Stack                          | Heap                        |
|-------------------------------|--------------------------------|-----------------------------|
| Allocate                      | move a pointer, ~1 instruction | pointer bump + eventual GC  |
| Free                          | automatic on return, free      | garbage collector, deferred |
| Order                         | strict LIFO                    | any order                   |
| Lifetime                      | dies when the function returns | lives while reachable       |
| Size                          | fixed per thread (~1 MB)       | grows to available memory   |
| Ownership                     | one thread, exclusively        | shared by all threads       |
| Can be handed to someone else | **no**                         | **yes**                     |

The last two rows are the ones async is built on. **A heap object outlives the function that created it, and any thread can reach it. A stack frame does neither.**

### Foundations: how .NET stores data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Foundations: how .NET stores data</p>

<a id="be-m21"></a>

#### What memory actually is

Start below the language. When your process starts, the operating system gives it a **virtual address space**: a flat, private range of numbered byte slots. On 64-bit Windows or Linux that range is enormous — far larger than the machine's RAM — because the addresses are not physical. The OS maps the parts you actually touch onto real memory as you touch them.

Two consequences follow immediately, and they explain a lot of later behaviour:

- **An address is just a number.** A 64-bit integer identifying a byte slot. A "reference" in C# is one of these numbers.
- **Everything is bytes.** There are no `int`s or `string`s in memory — only byte patterns that your code has agreed to interpret a particular way. The *type* lives in the compiler and in a small amount of runtime bookkeeping, not in the bytes themselves.

``` csharp
int x = 42;

// Somewhere in the address space, four bytes now hold the pattern for 42.
// On a little-endian machine (x64, ARM64) the low byte comes first:
//
//   address    0x7FF8_A100   0x7FF8_A101   0x7FF8_A102   0x7FF8_A103
//   byte           2A            00            00            00
//                  ^-- 42 decimal
//
// The name 'x' does not exist at runtime. The compiler chose this location
// (or a CPU register) and rewrote every use of 'x' into "read that place".
```

**So why is memory divided into regions at all?** Not for speed, and not because of types. The dividing question is **lifetime** — when is this data no longer needed?

For some data the answer is known at compile time: a loop counter dies when the method returns. Nobody needs to think about it, so it can be managed by a rule so simple it costs nothing. For other data the answer is unknowable at compile time: an object you create in one method, store in a list, and return to a caller who keeps it for an hour. Something has to decide when that is finished.

Those two answers produce the two regions. The stack handles lifetimes that nest neatly inside a call; the heap handles everything else.

| Region      | Holds                                             | Lifetime decided by   | Scope          |
|-------------|---------------------------------------------------|-----------------------|----------------|
| **Stack**   | call frames: parameters, locals, return addresses | the call returning    | one per thread |
| **GC heap** | objects created with `new`                        | the garbage collector | whole process  |
| Loader heap | type metadata, method tables, `static` fields     | essentially forever   | whole process  |
| Native heap | unmanaged allocations, buffers from interop       | you, explicitly       | whole process  |

> **\[Why it matters\]**
>
> **Why start this low.** Almost everything later in this guide is a consequence of this one distinction. Why structs copy and classes do not, why boxing allocates, why a closure changes where a local lives, why a cache full of long-lived objects costs more than a million short-lived ones, why `Span<T>` has the restrictions it has — all of it is downstream of "who decides when this memory is finished".

First principles: [Processes and virtual memory](01-07-the-operating-system.md#be-m6)

<a id="be-m23"></a>

#### The stack, frame by frame

The stack is one contiguous block per thread with a single register — the *stack pointer* — marking the boundary between used and unused. Calling a method moves that pointer to reserve space; returning moves it back. That is the entire mechanism.

``` csharp
decimal Total(Order order) {
    decimal sum = 0m;
    foreach (var line in order.Lines)
        sum += LineTotal(line);
    return sum;
}

decimal LineTotal(OrderLine line) {
    decimal gross = line.UnitPrice * line.Qty;
    return gross;
}
```

``` text
While LineTotal is executing, the stack looks roughly like this.
It grows DOWNWARD, toward lower addresses:

    higher addresses
  ┌──────────────────────────────┐
  │ ... caller of Total ...      │
  ├──────────────────────────────┤
  │ Total's frame                │
  │   order        (a reference) │──┐  8 bytes: an address into the heap
  │   sum          (decimal)     │  │  16 bytes, inline, right here
  │   line         (a reference) │──┼┐
  │   return address             │  ││ where to resume in the caller
  ├──────────────────────────────┤  ││
  │ LineTotal's frame            │  ││
  │   line         (a reference) │──┼┘
  │   gross        (decimal)     │  │
  │   return address             │  │  where to resume inside Total
  └──────────────────────────────┘  │
   ^ stack pointer                  │
    lower addresses                 │
                                    v
                              (the heap, elsewhere entirely)
```

When `LineTotal` returns, the stack pointer moves back up by the size of its frame. The bytes are not cleared, freed, or tracked — they are simply no longer considered in use, and the next call overwrites them. **Deallocation costs one register add.** That is why "stack allocation is free" is close enough to true.

Three details worth carrying forward:

- **Many locals never reach the stack at all.** The JIT keeps as much as it can in CPU registers, which are faster still. A small method may have a frame containing nothing but a return address.
- **Frame size is fixed at compile time.** The JIT knows exactly how many bytes each method needs, which is precisely why variable-sized data cannot live here — and why `stackalloc` is restricted ([Span of char and zero-allocation parsing](01-03-text-is-numbers-too.md#be-m82)).
- **The stack is per-thread and bounded.** 1 MB by default on Windows and for thread-pool threads; typically 8 MB for the main thread on Linux. Every concurrent thread reserves its own.

``` csharp
// Exceed the bound and the process dies. StackOverflowException cannot be
// caught, cannot be handled, and does not run finally blocks - by design,
// because there is no stack left to run a handler on.
int Depth(int n) => Depth(n + 1);        // ~10,000-50,000 frames, then the process ends
```

> **\[Trap\]**
>
> **This is why recursion over data of unbounded shape is dangerous.** A recursive walk of a 50,000-node category tree is a crash, not an exception you can recover from. Any traversal whose depth is driven by data rather than by code should use an explicit `Stack<T>` on the heap, where the limit is gigabytes rather than one megabyte ([Graphs: representation and topological sort](03-06-graphs.md#be-m122)).

> **\[Tip\]**
>
> **The stack is also the reason async methods are different.** An `await` can return control to the caller and resume later — possibly on a different thread. A stack frame cannot survive that, so the compiler moves the method's locals into a heap-allocated state machine object. That is the real cost of `async`, and it is why a `ref struct` like `Span<T>` cannot live across an `await` ([Managed pointers: ref, in, and ref struct](#be-m35)).

First principles: [Threads, scheduling, and context switches](05-02-threads-and-locks.md#be-m7)

<a id="be-m25"></a>

#### Stack and heap: the real mental model

With both regions understood, the rule most people are taught can be corrected. That rule — "value types live on the stack, reference types live on the heap" — is close enough to survive an interview and wrong often enough to cause bugs.

The accurate version is shorter: **a value's location is determined by where its container lives, not by its type.** Everything below follows from that one sentence.

The rule most people learn — "value types live on the stack, reference types live on the heap" — is *wrong as stated*. What is actually true:

- An object's *fields* live wherever the object lives.
- A local of value type lives on the stack *if* the JIT keeps it there. It may live in a register instead, or on the heap if it is captured by a closure, is a field of a class, or is an element of an array.
- A local of reference type is a *pointer* on the stack; the object it points to is on the heap.

``` csharp
class Order { public decimal Total; }   // Total lives on the heap, inside the Order
struct Money { public decimal Value; }  // location depends entirely on where Money is used

void M() {
    Money m;          // stack (or register)
    Order o = new();  // 'o' is a pointer on the stack; the object is on the heap
    var arr = new Money[10]; // 10 Money structs, inline, on the heap
}
```

> **\[Why it matters\]**
>
> **Why it matters.** Heap allocation is not slow — it is *deferred cost*. You pay later, in GC pauses, and the bill scales with how much you allocated and how long it survived.

<a id="be-m26"></a>

#### A worked trace: where everything actually goes

One short method, traced line by line. Nothing here is exotic — it is the shape of ordinary application code — and it touches the stack, the heap, a closure, a growth array, and a box.

``` csharp
List<string> Summarize(Order[] orders, decimal minimum) {
    var result = new List<string>();          // (1)
    var big = orders.Where(o => o.Total >= minimum);   // (2)
    int count = 0;                            // (3)
    foreach (var o in big) {
        var money = new Money(o.Total, "VND"); // (4)  readonly record struct
        result.Add($"{o.Id}: {money}");        // (5)
        count++;
    }
    Console.WriteLine("Found " + count);       // (6)
    return result;                             // (7)
}
```

``` csharp
STACK (this frame)                    HEAP
──────────────────                    ────
orders   ──────────────────────────►  Order[]  (caller allocated)
minimum  = 250000.0000  (16 bytes, inline - decimal is a struct)
result   ──────────────────────────►  (1) List, 32 bytes, _items = null
                                          └─► (1b) string[4] on first Add
big      ──────────────────────────►  (2) WhereArrayIterator
                                          ├─► (2a) display class { minimum }
                                          └─► (2b) Func<Order,bool> delegate
count    = 0            (4 bytes, or a register - never memory at all)
o        ──────────────────────────►  an existing Order (no allocation)
money    = { 250000, ptr }  (24 bytes, inline - NOT a heap object)
                            └────────► the interned literal "VND"
                                      (5) the formatted string, per iteration
                                      (6) "Found " + count  -> count is BOXED,
                                          then a new string
```

Reading that off, line by line:

|       | Allocates                    | Why                                                                                                                         |
|-------|------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| \(1\) | 1 object, then growth arrays | `List` starts empty and doubles: 4, 8, 16… ([List of T: growth, capacity, and pre-sizing](03-02-arrays-and-lists.md#be-m99))                         |
| \(2\) | 3 objects                    | the lambda captures `minimum`, so a display class *and* a delegate, plus the iterator ([LINQ allocation profile](03-10-collections-and-linq-in-net.md#be-m148)) |
| \(3\) | 0                            | an `int` local; the JIT keeps it in a register                                                                              |
| \(4\) | **0**                        | `new` on a struct is not a heap allocation — it initialises stack bytes ([What new actually does](01-04-the-processor.md#be-c303))                |
| \(5\) | 1 per iteration              | the interpolated result; the handler itself is stack-based on .NET 6+                                                       |
| \(6\) | 2                            | `int` boxed to `object` for `string.Concat`, then the string ([Boxing and unboxing](#be-m33))                               |
| \(7\) | 0                            | returning a reference copies 8 bytes                                                                                        |

Three observations worth generalising:

- **The struct is free and the closure is not.** Most people assume the opposite — that `new` is the expensive word. `new Money(...)` allocates nothing; `o => o.Total >= minimum` allocates twice, invisibly, because it captures a local.
- **Nothing here survives the method except `result`.** Every other allocation dies in gen 0 and costs almost nothing to reclaim ([The heap, and why it needs a collector](02-07-memory-management-and-garbage-collection.md#be-m24)). This method is not a performance problem, and rewriting it to be allocation-free would be wasted effort.
- **The one avoidable cost is line (6).** `"Found " + count` boxes. `$"Found {count}"` does not, on .NET 6 and later.

> **\[Tip\]**
>
> **Do this trace on your own hot path once.** Not as a habit — as a calibration exercise. Most developers discover their intuition is miscalibrated in both directions: they worry about allocations that die instantly, and miss the closure or the box that fires a million times a second. After doing it once against a profiler, the intuition tends to stick ([Seeing it yourself: measuring layout and allocation](#be-m36)).

<a id="be-m28"></a>

#### What a reference actually is

A reference is a machine pointer — 8 bytes on x64 — into the GC heap. It is not a handle, not an index into a table. Two consequences follow.

**The GC moves objects.** During a compacting collection, live objects are slid down to remove gaps, and every reference to them is rewritten. This is why you cannot cache a raw address without pinning (`fixed`, `GCHandle.Alloc(..., Pinned)`), and why pinning too much causes heap fragmentation.

**Reference equality is pointer equality.** `ReferenceEquals(a, b)` compares the pointers. This stays correct across a compaction because both pointers get rewritten together.

``` csharp
object a = new object();
object b = a;
Console.WriteLine(ReferenceEquals(a, b)); // True - same pointer

// A null reference is literally the zero pointer.
// NullReferenceException is the hardware access violation at address 0,
// caught by the runtime and translated. That is why it is cheap to *not* check
// for null: the CPU checks for you.
```

<a id="be-m29"></a>

#### Following a reference: dereference and dispatch

What does `order.Total` actually cost? The field's offset within the object is fixed when the type is loaded, so the JIT emits a constant — there is no lookup, no name, no dictionary.

``` csharp
decimal t = order.Total;

; order is a pointer, currently in rcx
; Total sits at a known offset from the object's start
    mov  rax, [rcx+0x18]        ; two instructions, one dependent load
    mov  rdx, [rcx+0x20]        ; (decimal is 16 bytes, so two moves)

; Field access is ~1 instruction. The cost is whether [rcx+0x18] is in cache.
```

**The null check is free.** There is no comparison. If `rcx` is zero, the CPU faults reading address `0x18`, the OS raises an access violation, and the runtime translates it into `NullReferenceException`. You pay nothing on the path where the reference is valid — which is why defensive null checks in hot code buy nothing the hardware was not already doing.

``` csharp
// Virtual and interface calls cost more, because the target is not known:
processor.Handle(order);

;   mov  rax, [rcx]             ; load the method table pointer from the object
;   call qword ptr [rax+0x40]   ; indirect call through the vtable slot
;
; Three dependent loads instead of one direct call, and - critically -
; the CPU cannot inline through it or predict it as reliably.
```

The JIT fights back with **devirtualization**: if it can prove the concrete type — the class is `sealed`, the method is not overridden anywhere loaded, or the object was just constructed locally — it emits a direct call and can inline the body. Dynamic PGO (on by default since .NET 8) goes further, observing which type actually shows up at runtime and emitting a *guarded* fast path for it.

``` csharp
public sealed class OrderProcessor { ... }   // sealed helps the JIT devirtualize
// Also: a generic constrained to a struct is specialised per type, so calls
// through the constraint are direct and inlinable (§58).
```

> **\[Why it matters\]**
>
> **Keep the proportions straight.** A virtual call costs a few extra cycles. A cache miss on the object it points to costs ~250. Chasing pointers through scattered objects — a linked list, a deep object graph, an array of classes — is far more expensive than the dispatch mechanism itself. Optimising away an interface while leaving the layout scattered is usually optimising the wrong term ([The memory hierarchy: registers to RAM](01-04-the-processor.md#be-m22)).

> **\[Trap\]**
>
> **Which is why `x?.y?.z` is not the cost you think.** Each `?.` adds a real compare-and-branch, unlike an ordinary dereference — but branches are cheap and well predicted. The genuine cost of a long navigation chain is that each hop is a separate object, likely on a separate cache line, likely a separate miss. The fix is flattening the data, not removing the null checks ([Projection: Select before materialize](07-07-orms-and-ef-core.md#be-m157)).

Runtime context: [Inlining and what stops it](02-02-compilers-interpreters-and-jit.md#be-m41)

<a id="be-m30"></a>

#### Object header and the real cost of an object

On 64-bit .NET, every heap object carries:

- **8 bytes** — sync block index / object header word (used for lock state, hash code, GC bits).
- **8 bytes** — method table pointer (the type identity; what `GetType()` and virtual dispatch read).
- then the fields, padded to 8-byte alignment;
- minimum object size is **24 bytes**.

``` csharp
class Tiny { }                 // 24 bytes on the heap
class OneInt { int x; }        // 24 bytes (16 header + 4 + 4 padding)
class TwoInts { int a, b; }    // 24 bytes - the second int is free
class ThreeInts { int a,b,c; } // 32 bytes

// Arrays add 8 more bytes for the length:
new int[0]   // 32 bytes total for zero elements
```

So a `List<Point>` holding one million 8-byte points costs ~8 MB. A `List<PointClass>` holding the same data costs ~8 MB of pointers *plus* ~24 MB of objects, spread across the heap with no locality. That is the entire argument for structs in hot data.

> **\[Tip\]**
>
> **Measure it.** `System.Runtime.InteropServices.Marshal.SizeOf<T>()` lies for managed types (it reports the marshalling layout). Use `Unsafe.SizeOf<T>()` for the CLR view of a struct, and a memory profiler or `GC.GetTotalAllocatedBytes()` for real objects.

<a id="be-m31"></a>

#### Struct layout, padding, and alignment

The CLR is free to reorder struct fields (`LayoutKind.Auto` is the default for structs unless they contain only blittable types, where it is effectively sequential). It aligns each field to its natural size, which introduces padding.

``` text
struct Bad  { byte a; long b; byte c; }  // 24 bytes if laid out sequentially
struct Good { long b; byte a; byte c; }  // 16 bytes

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Packed { byte a; long b; byte c; } // 10 bytes, but misaligned reads are slower
```

Order fields largest-to-smallest when you control layout and size matters. Use `[StructLayout(LayoutKind.Explicit)]` with `[FieldOffset]` only for interop or deliberate unions.

> **\[Trap\]**
>
> **Trap.** Large structs are expensive to copy. Past roughly 16–24 bytes, passing by value starts to lose to passing a reference. Use `in` parameters (`in Matrix4x4 m`) to pass a readonly reference, and mark the struct `readonly struct` so the compiler does not create defensive copies on every member access.

<a id="be-m33"></a>

#### Boxing and unboxing

Boxing wraps a value type in a heap object so it can be treated as `object` or as an interface. It allocates 24+ bytes and copies the value in. Unboxing copies it back out, with a type check.

``` csharp
int i = 42;
object o = i;        // box: allocation + copy
int j = (int)o;      // unbox: type check + copy
```

**Where boxing hides:**

- `ArrayList`, `Hashtable`, and any non-generic collection.
- Calling a non-overridden `ValueType` method: `myStruct.ToString()` when the struct does not override it.
- String interpolation and `string.Format` of value types on older TFMs (fixed in .NET 6+ by interpolated string handlers, but `string.Format(…, (object)x)` still boxes).
- Storing a struct in a variable typed as an interface: `IComparable c = 5;`
- `Enum.HasFlag` before .NET Core 2.1 (now boxing-free).
- LINQ over a struct sequence through a non-generic path.

``` text
// Generic constraints avoid boxing because the JIT specializes for each value type:
void Log<T>(T value) where T : IFormattable  // no box
void Log(IFormattable value)                  // boxes when called with a struct
```

> **\[Tip\]**
>
> **Detecting it.** Boxing shows up as `box` in IL. In BenchmarkDotNet, turn on `[MemoryDiagnoser]` and look for unexpected `Allocated` bytes on a method that should be allocation-free.

<a id="be-m35"></a>

#### Managed pointers: ref, in, and ref struct

A *managed pointer* points at a location (a field, an array slot, a local) rather than at an object. The GC knows about it and updates it when things move. This is what `ref` compiles to.

``` csharp
ref int slot = ref array[5];   // ref local: an alias, not a copy
slot = 99;                     // writes array[5]

void Add(in Matrix4x4 m)       // readonly reference: no copy, cannot assign
static ref T Find<T>(T[] a, int i) => ref a[i];  // ref return
```

`ref struct` types (`Span<T>`, `ReadOnlySpan<T>`, `Utf8JsonReader`) are stack-only: they can hold a managed pointer, so they must never end up on the heap where the GC could lose track of what they point into. The compiler enforces this — a `ref struct` cannot be a field of a class, boxed, captured in a lambda, or used in an `async` method across an `await`.

> **\[Tip\]**
>
> **Practical use.** `CollectionsMarshal.GetValueRefOrNullRef` gives you a `ref` to a dictionary's value slot, letting you update in place without a second lookup — the fastest way to do read-modify-write on a `Dictionary<K,V>` with a struct value.

<a id="be-m36"></a>

#### Seeing it yourself: measuring layout and allocation

Everything in this part is checkable in a few lines. Verifying it once beats trusting a diagram — including this guide's diagrams.

``` csharp
// How many bytes did this actually allocate? Exact, cheap, no profiler needed.
static long Allocations(Action work) {
    work();                                              // warm up: JIT, statics, caches
    var before = GC.GetAllocatedBytesForCurrentThread();
    work();
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

Console.WriteLine(Allocations(() => { var m = new Money(1m, "VND"); }));   // 0
Console.WriteLine(Allocations(() => { object o = 42; }));                  // 24
Console.WriteLine(Allocations(() => { var l = new List<int>(); }));       // 32
```

``` csharp
// How big is a struct, really - including padding? (§31)
Unsafe.SizeOf<Money>();          // the CLR's view. Use this.
Marshal.SizeOf<Money>();         // the INTEROP marshalling view - often different, often wrong
                                 // for this purpose. A common source of confusion.

// Field-by-field layout, including where the padding went
// (ObjectLayoutInspector, a small NuGet package):
TypeLayout.PrintLayout<Money>();
//  Size: 24 bytes. Paddings: 7 bytes (%29 of empty space)
//  |   0-15: decimal Amount   (16 bytes)
//  |     16: byte   Flag      ( 1 byte)
//  |  17-23: PADDING          ( 7 bytes)   <- reorder the fields to reclaim this
```

``` csharp
// Is the GC actually working hard, or does it just feel like it?
Console.WriteLine($"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");

var info = GC.GetGCMemoryInfo();
Console.WriteLine($"heap={info.HeapSizeBytes:N0}  fragmented={info.FragmentedBytes:N0}");
Console.WriteLine($"pause total={info.PauseDurations[0]}  concurrent={info.Concurrent}");

// The ratio that matters: promotions, not allocations.
// Many gen0 collections with few gen2 collections is a HEALTHY signal
// (§34).
```

| Question                            | Tool                                                                             |
|-------------------------------------|----------------------------------------------------------------------------------|
| What did this method allocate?      | `GC.GetAllocatedBytesForCurrentThread`, or BenchmarkDotNet's `[MemoryDiagnoser]` |
| What is the layout of this struct?  | `Unsafe.SizeOf<T>`, ObjectLayoutInspector                                        |
| What is on the heap right now?      | `dotnet-gcdump collect -p <pid>`                                                 |
| Is the GC a problem in production?  | `dotnet-counters monitor System.Runtime` — % time in GC, gen 2 count             |
| What is keeping this object alive?  | a heap snapshot, then the retention path in dotMemory / VS                       |
| What machine code did the JIT emit? | sharplab.io, or `DOTNET_JitDisasm`                                               |

> **\[Trap\]**
>
> **Two measurement traps.** `GC.GetTotalMemory(forceFullCollection: true)` triggers a blocking full collection — useful in a diagnostic console app, never in a running service. And measuring anything without a warm-up run measures JIT compilation and tiered-compilation transitions rather than your code; the `work()` call before the baseline above exists precisely for that ([Measure first: tools and method](12-04-scaling-and-performance.md#be-m275)).

> **\[Why it matters\]**
>
> **The habit worth forming** is not measuring constantly — it is measuring *once, early*, when you are forming a belief about how something behaves. A five-line allocation check settles an argument that would otherwise be resolved by whoever is most confident.

Runtime context: [Source to machine code: the whole pipeline](02-02-compilers-interpreters-and-jit.md#be-m37)
