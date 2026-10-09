---
id: "1-1"
number: "1.1"
title: "Bits and bytes"
part: 1
part_title: "The machine"
words: 3955
minutes: 17
---

# 1.1 Bits and bytes

> **Quick look.** Everything is stored as bits; learn how rows of 1s and 0s become numbers, flags and bytes.
>
> - A bit is on or off; 8 bits make a byte with 256 possible patterns.
> - Hex is shorthand for binary: each hex digit is exactly four bits (#FF8800 is three bytes).
> - Bitwise operators let one integer hold many yes/no flags; flag values must be powers of two.
> - Byte order (endianness) matters whenever numbers become raw bytes on disk or the wire.
> - KB (1,000) and KiB (1,024) differ by several percent, which explains file sizes that don't add up.
> - Bitsets store set membership at one bit per value, far denser than bool arrays or HashSet.


### The big picture

Everything a computer stores, whether a price, a name, a photo or this page, ends up as long rows of 1s and 0s. This chapter is about those 1s and 0s: why computers use only two states, how rows of them become numbers, and how programmers read and change them.

You meet this directly in everyday work: in colour codes like `#FF8800`, in permission flags, in file sizes that don't add up, and in network and file formats. Think of it as learning the alphabet before reading words. Nothing here is hard, and everything later builds on it.

### Before you start

**Builds on:** .

**Ideas explained later:**

- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Git commit** (11.1): A snapshot of the whole project, with its parent commits, author, date and message, named by its hash.
- **Redundancy** (12.5): Having more than one of any component whose failure would matter.
- **Cryptographic hash** (10.5): A one-way function that turns data into a fixed-size fingerprint that changes completely if the data changes.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.
- **Kubernetes** (11.3): The dominant container orchestrator, which reconciles declared desired state across a cluster.

**Tools and technologies named here:**

- **ARM64** (1.4): The 64-bit ARM processor architecture, used by Apple silicon Macs, most phones and many cloud servers.
- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.

**The code uses:** variables and var, methods and local functions, string interpolation, tuples, attributes. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is 0x2F in decimal?**

> 47. Each hex digit stands for four bits. The 2 is worth 2 × 16 = 32, and F is 15. 32 + 15 = 47.

**2. How many different values can one byte hold?**

> 256. A byte is 8 bits, and 2⁸ = 256. As an unsigned number that is 0 to 255.

**3. Why does a "1 TB" drive show up as about 931 GB in Windows?**

> The drive maker counts in powers of 1000. Windows counts in powers of 1024 but still writes "GB". 10¹² bytes ÷ 2³⁰ ≈ 931.

**4. How do you turn off one flag in a flags value, leaving the others alone?**

> AND it with the inverse of that flag: `value &= ~Flag`. The `~` flips every bit, so only the flag's own bit becomes 0.

**5. Is an x64 or ARM64 machine little-endian or big-endian, and which order do network protocols use?**

> Both are little-endian: the lowest byte of a number is stored first. Network protocols use big-endian, which is called network byte order.

**6. How can you test whether a positive number is a power of two with a single AND?**

> `(x & (x - 1)) == 0`. A power of two has exactly one bit set. Subtracting 1 clears that bit and sets all the bits below it, so nothing overlaps.

**7. How do you get the green part out of a color stored as 0xRRGGBB?**

> Shift right by 8 and keep the lowest 8 bits: `(color >> 8) & 0xFF`.

### Two states are enough

A computer is built from billions of tiny switches called transistors. Each switch is either on or off. Everything the machine stores or moves is made of these two states.

Why only two? Electricity is noisy. A signal that means "a bit more voltage" or "a bit less" is easy to misread. Two states far apart are easy to tell apart, even with noise. So hardware uses two.

One on/off value is called a **bit**. The word is short for binary digit. We write the two states as 1 and 0.

### From switches to logic

Transistors are wired together into small circuits called **logic gates**. A gate takes one or two bits in and gives one bit out. An AND gate outputs 1 only when both inputs are 1. An OR gate outputs 1 when either input is 1. A NOT gate flips its input.

Every calculation a computer does is built from gates like these. Addition is a good example. Adding two single bits gives a sum bit and a carry bit. The sum is 1 when exactly one input is 1, which is XOR. The carry is 1 when both inputs are 1, which is AND. Chain enough of these together and you can add numbers of any width.

You can see the same idea in code. This adds two numbers using only AND, XOR and shift.

```csharp
// adder.cs — run with: dotnet run adder.cs
Console.WriteLine(Add(19, 23));   // 42
Console.WriteLine(Add(-5, 3));    // -2: negative numbers work too (see 1.2)

static int Add(int a, int b)
{
    while (b != 0)
    {
        int carry = (a & b) << 1;   // where both bits are 1, carry into the next position
        a ^= b;                     // add each position, ignoring carries
        b = carry;                  // add the carries in the next round
    }
    return a;
}
```

The hardware does all positions at once, in a fraction of a nanosecond. The loop just shows the logic.

### Counting with two digits

You count in base 10. Each position is worth ten times the one to its right: ones, tens, hundreds.

Binary is base 2. It works the same way. Each position is worth twice the one to its right: 1, 2, 4, 8, 16, 32, 64, 128.

So binary `101` means one 4, no 2 and one 1. That is 5.

With *n* bits you can make 2ⁿ different patterns. Three bits give 8 patterns, 0 to 7. Eight bits give 256 patterns, 0 to 255. Every extra bit doubles the count.

| Decimal | Binary | Hex |
|---:|---:|---:|
| 0 | 0000 0000 | 00 |
| 5 | 0000 0101 | 05 |
| 10 | 0000 1010 | 0A |
| 15 | 0000 1111 | 0F |
| 16 | 0001 0000 | 10 |
| 47 | 0010 1111 | 2F |
| 255 | 1111 1111 | FF |

### Bytes

One bit holds very little, so bits are grouped. A group of 8 bits is a **byte**. The byte is the smallest unit most computers can address. When a program reads from memory, it reads at least one whole byte.

Bigger sizes come in two conventions. The decimal one uses powers of 1000: a kilobyte (KB) is 1000 bytes. The binary one uses powers of 1024: a kibibyte (KiB) is 1024 bytes. Drive makers use decimal. Windows uses binary but writes "KB", "MB" and "GB". Linux tools and most cloud providers usually write the binary units correctly as KiB, MiB and GiB.

The gap grows with size. It is 2.4% at kilo, 7.4% at giga and 10% at tera.

### Nibbles and words

Two more size names are common. A **nibble** is four bits, half a byte. One hex digit is exactly one nibble, as the next section shows.

A **word** is the amount of data a processor naturally handles in one step. On current 64-bit processors, a word is 64 bits. "A 64-bit machine" means its registers and memory addresses are 64 bits wide.

The word "word" is also used loosely, and older names stick around. In the Windows API, `WORD` means 16 bits, `DWORD` means 32 and `QWORD` means 64. These names date from 16-bit processors. So check which meaning a document uses.

### Hexadecimal

Binary is long to read. 255 is `11111111`. Hexadecimal, or **hex**, is a shorthand. It is base 16. It uses the digits 0–9 and then A–F for 10 to 15.

One hex digit covers exactly four bits. So a byte is always exactly two hex digits. `1111 1111` splits into `1111` and `1111`, which is F and F, so `FF`.

Most languages mark hex with the prefix `0x` and binary with `0b`. So `0xFF`, `0b1111_1111` and `255` are the same number. The underscore is just a visual separator. C#, JavaScript and many other languages allow it.

You meet hex constantly. A web color like `#FF8800` is three bytes: red, green and blue. Memory addresses, GUIDs, Git commit ids and hash values are all shown in hex.

### Byte order

A number bigger than one byte takes several bytes. The machine must decide which byte goes first in memory. That decision is called **endianness**.

**Little-endian** stores the lowest byte first. x64 and ARM64 processors are little-endian, so nearly every computer you use is too. **Big-endian** stores the highest byte first. It reads the way we write numbers. Network protocols use big-endian, so it is also called **network byte order**. Some file formats use one and some use the other.

Endianness only matters when numbers become raw bytes. That happens in files, network messages and hashing. Inside your program, `0x12345678` is just a number.

```csharp
// endian.cs — run with: dotnet run endian.cs
using System.Buffers.Binary;

int value = 0x12345678;
Console.WriteLine(BitConverter.IsLittleEndian);        // True on x64 and ARM64
Console.WriteLine(Convert.ToHexString(BitConverter.GetBytes(value)));  // 78563412: low byte first

Span<byte> wire = stackalloc byte[4];                  // 4 bytes on the stack (see 1.5)
BinaryPrimitives.WriteInt32BigEndian(wire, value);
Console.WriteLine(Convert.ToHexString(wire));          // 12345678: network byte order
Console.WriteLine(BinaryPrimitives.ReadInt32BigEndian(wire) == value);  // True
```

`BitConverter` uses whatever order the machine uses. `BinaryPrimitives` makes you say which order you mean. Prefer it for anything that leaves the process.

### Working with single bits

Bitwise operators work on each bit position separately.

AND (`&`) gives 1 only where both inputs are 1. OR (`|`) gives 1 where either input is 1. XOR (`^`) gives 1 where the inputs differ. NOT (`~`) flips every bit. Shift left (`<<`) slides the bits left, which multiplies by 2 per step. Shift right (`>>`) slides them right, which divides by 2 per step.

The everyday use is **flags**. You pack several yes/no settings into one number, one bit each. You set a flag with OR. You test it with AND. You clear it with AND NOT.

```csharp
// bits.cs — run with: dotnet run bits.cs   (needs the .NET 10 SDK)
int n = 47;
Console.WriteLine(n.ToString("B8"));      // 00101111  (binary, 8 digits)
Console.WriteLine(n.ToString("X2"));      // 2F        (hex, 2 digits)
Console.WriteLine(0b0010_1111 == 0x2F);   // True

var p = Permissions.Read | Permissions.Write;     // set two flags
Console.WriteLine(p);                              // Read, Write
Console.WriteLine((p & Permissions.Write) != 0);   // True: Write is set
Console.WriteLine(p.HasFlag(Permissions.Delete));  // False

p &= ~Permissions.Write;                           // clear one flag
Console.WriteLine(p);                              // Read

Console.WriteLine(1 << 3);                         // 8: shifting left 3 times multiplies by 2³

[Flags]
enum Permissions
{
    None   = 0,
    Read   = 1 << 0,   // 0001
    Write  = 1 << 1,   // 0010
    Delete = 1 << 2,   // 0100
}
```

Unix file permissions use the same idea. In `chmod 755`, each digit is three bits: read is 4, write is 2 and execute is 1. So 7 means all three, and 5 means read and execute.

### Bit tricks you will meet

A few patterns come up again and again in real code.

**Packing and unpacking fields.** Several small values can share one number. A color is the classic case: red, green and blue, one byte each. Shift right to move a field down to the bottom, then keep only the bits you want. Keeping only some bits is called **masking**, and the number that selects them is a **mask**. `0xFF` is a mask for the lowest byte.

**Powers of two.** A power of two has exactly one bit set. So `x & (x - 1)` is zero only for powers of two. Hash tables and memory allocators use this constantly, because dividing by a power of two is just a shift.

**Counting and finding bits.** How many bits are set? Where is the highest one? Modern processors answer these with a single instruction. .NET exposes them in `System.Numerics.BitOperations`.

```csharp
// tricks.cs — run with: dotnet run tricks.cs
using System.Numerics;

uint color = 0xFF8800;                  // the web color #FF8800
byte red   = (byte)(color >> 16);       // 255
byte green = (byte)(color >> 8);        // 136: the cast keeps only the lowest 8 bits
byte blue  = (byte)color;               // 0
Console.WriteLine($"{red} {green} {blue}");

uint packed = (uint)(red << 16 | green << 8 | blue);
Console.WriteLine(packed == color);                 // True

Console.WriteLine(BitOperations.IsPow2(64));        // True
Console.WriteLine((64 & (64 - 1)) == 0);            // True: the same test by hand
Console.WriteLine(BitOperations.PopCount(0b1011u)); // 3: bits that are set
Console.WriteLine(BitOperations.Log2(1000u));       // 9: position of the highest set bit
Console.WriteLine(BitOperations.RoundUpToPowerOf2(1000u)); // 1024
Console.WriteLine(-8 >> 1);                         // -4: >> keeps the sign
Console.WriteLine(-8 >>> 1);                        // 2147483644: >>> fills with zeros
```

The last two lines show the two kinds of right shift. `>>` on a signed number copies the sign bit into the gap, so negative numbers stay negative. This is an **arithmetic shift**. `>>>`, added in C# 11, always fills with zeros. This is a **logical shift**.

### Checking data with bits

Bits also protect data from damage. A **parity bit** records whether the number of 1 bits is odd or even. If one bit flips in transit, the parity no longer matches. A **checksum** extends the idea to a whole block of data. A **CRC** (cyclic redundancy check) is a stronger checksum built from shifts and XORs. Ethernet, ZIP files and PNG images all use CRCs.

These catch accidents, not attacks. Anyone can change the data and recompute a CRC. Protecting against deliberate changes needs a cryptographic hash, covered in Secrets and cryptography basics (10.5).

### In short

A bit is one on-or-off value, and eight bits make a byte, which can hold 256 different patterns. Binary counts in powers of two, and hex is a shorthand in which each digit stands for four bits. Bitwise operators work on single bits, which lets one number hold many yes-or-no flags. A number bigger than a byte can be stored lowest byte first or highest byte first, which matters whenever numbers become raw bytes. Size units come in decimal (KB) and binary (KiB) versions that differ by several percent.

### What goes wrong

**Flag values that are not powers of two.** If `Write = 2` and `Delete = 3`, then Delete also contains Write's bit. Checks then give wrong answers. Always define flags as `1 << n`.

**`HasFlag(None)` is always true.** Testing against zero asks "are all of no bits set?", and the answer is always yes.

**Shifting past the width.** In C#, a shift on an `int` uses only the lowest 5 bits of the shift count. So `1 << 32` is 1, not 0. JavaScript behaves the same way on its 32-bit bitwise operators.

**Mixing up KB and KiB.** Size limits, quotas and memory settings can be off by 7% at gigabyte scale. Container memory limits in Docker and Kubernetes are binary units: `512Mi` is 512 × 1024 × 1024 bytes.

**Byte-order mix-ups.** Reading a big-endian file or network field with `BitConverter` gives nonsense: `0x12345678` comes out as `0x78563412`. State the byte order with `BinaryPrimitives`.

**Operator precedence.** In C and JavaScript, `x & 1 == 0` means `x & (1 == 0)`, which is always zero. C# refuses to compile it, which is the better outcome. Always put bitwise expressions in parentheses: `(x & 1) == 0`.

**Sign extension.** Casting a `sbyte` or `short` to `int` copies the sign bit upward, so `(int)(sbyte)0xFF` is −1, not 255. When you want the raw bits, work with unsigned types.

### Cost model

Bitwise operations are among the cheapest things a processor does. They usually take one clock cycle, often less. Packing flags saves memory, but it costs readability. For a handful of settings on an ordinary class, plain `bool` properties are fine. Flags pay off when you store millions of records, send data over a network or match a file format.

Counting set bits or finding the highest set bit is a single instruction on current processors, and `BitOperations` uses it. `bool[]` takes one byte per value. `BitArray` packs eight values per byte, so it uses one eighth of the memory, at the cost of a shift and mask on each access.

### In other languages (optional)

**JavaScript and TypeScript.** Bitwise operators first convert numbers to 32-bit signed integers. So `(2 ** 32 + 5) | 0` is 5. `>>>` is the unsigned shift. For 64-bit bit work, use `BigInt`: `1n << 40n`.

**Python.** Integers have no fixed width, so shifting left never overflows. `~5` is −6, following two's complement rules as if there were infinitely many sign bits. `bin(47)`, `hex(47)` and `int("2f", 16)` convert between bases.

**SQL Server.** `&`, `|`, `^` and `~` work on integer columns. The `BIT` type holds 0, 1 or NULL, and up to eight `BIT` columns in one table share a single byte of storage.

**C and C++.** Shifting a negative number or shifting by the full width has historically been undefined or implementation-defined. Many real security bugs came from that.

### Key terms

- **Bit**: One binary digit, 0 or 1. The smallest unit of information.
- **Byte**: Eight bits. The smallest unit most computers can address in memory.
- **Nibble**: Four bits, half a byte. Exactly one hex digit.
- **Word**: The amount of data a processor handles in one step. 64 bits on current processors.
- **Hexadecimal**: Base 16, written with 0–9 and A–F. Two hex digits make one byte.
- **Logic gate**: A tiny circuit that combines input bits into one output bit, such as AND, OR or NOT.
- **Bitwise operator**: An operator that works on each bit position separately: AND, OR, XOR, NOT and shifts.
- **Flag**: A single bit used as a yes/no setting, often packed with others into one number.
- **Mask**: A number whose 1 bits select which bits of another number to keep or change.
- **Endianness**: The order in which the bytes of a multi-byte number are stored. Little-endian puts the lowest byte first.
- **Arithmetic shift**: A right shift that copies the sign bit into the gap, so negative numbers stay negative.
- **Checksum**: A small value computed from data, used to detect accidental damage. A CRC is a common kind.

### Where this leads

Bits become whole numbers, negative numbers and fractions in Numbers inside the machine (1.2). Bytes become characters in Text is numbers too (1.3). Hash functions mix bits to spread keys evenly in Hash tables (3.3). Network headers are packed bit by bit in Layers and packets (6.1). Cryptography works on raw bytes in Secrets and cryptography basics (10.5).

### Check yourself

1. Convert 200 to binary and to hex by hand. Then check with `200.ToString("B8")` and `200.ToString("X2")`.
2. Explain why a 32-bit number can hold about 4.3 billion different values.
3. Write a `[Flags]` enum for the days of the week. Write a method that returns true if a value includes Saturday or Sunday, using a single AND.
4. Write `IsBitSet(int value, int position)` and `SetBit(int value, int position)` using only shifts, AND and OR.
5. Print 1,000,000 as bytes in both little-endian and big-endian order. Explain why the byte `40` appears first in one and last in the other.
6. Trace `Add(6, 3)` from `adder.cs` by hand. Write down `a`, `b` and `carry` for each round of the loop.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Permission flags that silently overlapped

An admin screen stores user permissions in a [Flags] enum. Granting Export also seems to grant Delete.

**What goes wrong.** Someone added `Export = 3`. 3 is binary 011, so it equals Read | Write, and checks overlap.

*Before*

```csharp
[Flags] enum Perm { None = 0, Read = 1, Write = 2, Export = 3, Delete = 4 }   // 3 is not a single bit
```

*After*

```csharp
[Flags] enum Perm
{
    None   = 0,
    Read   = 1 << 0,   // 1
    Write  = 1 << 1,   // 2
    Export = 1 << 2,   // 4
    Delete = 1 << 3,   // 8
}

var p = Perm.Read | Perm.Export;
bool canDelete = (p & Perm.Delete) != 0;   // false
p &= ~Perm.Export;                          // revoke one flag
```

> **Lesson:** Every flag value must be a distinct power of two. Writing them as shifts makes mistakes visible.

#### Example 2: Reading a binary file header from a partner

A partner sends files whose first 8 bytes are a magic number and a big-endian record count. Your parser reads the count as 16,777,216 instead of 1.

**What goes wrong.** x64 and ARM are little-endian; the file format is big-endian.

*The fix*

```csharp
using System.Buffers.Binary;

Span<byte> header = stackalloc byte[8];
stream.ReadExactly(header);

uint magic = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
if (magic != 0x464C4954) throw new InvalidDataException("Not a FLIT file");   // "FLIT"
int count = BinaryPrimitives.ReadInt32BigEndian(header[4..]);
```

> **Lesson:** Whenever numbers cross into raw bytes, state the byte order explicitly with BinaryPrimitives instead of relying on the machine's.

### Structures you build yourself

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Structures you build yourself</p>

<a id="be-m121"></a>

#### Bitsets and bit manipulation

When the universe of values is a bounded range of integers, a bitset stores membership at one bit per value — 64× denser than `bool[]` and roughly 80× denser than `HashSet<int>`, with set operations that process 64 values per instruction.

``` csharp
var flags = new BitArray(10_000_000);      // 1.25 MB
flags[42] = true;
flags.Or(otherFlags);                       // union, 64 bits at a time
flags.And(otherFlags);                      // intersection
flags.Not();                                // complement, in place

// Or manage the words directly when you want spans and vectorisation:
ulong[] bits = new ulong[(n + 63) / 64];
void Set(int i)      => bits[i >> 6] |= 1UL << (i & 63);
bool Get(int i)      => (bits[i >> 6] & (1UL << (i & 63))) != 0;
int  Count()         { int c = 0; foreach (var w in bits) c += BitOperations.PopCount(w); return c; }
```

`System.Numerics.BitOperations` maps to single CPU instructions on modern hardware:

| Method                       | Returns                     | Typical use                     |
|------------------------------|-----------------------------|---------------------------------|
| `PopCount`                   | number of set bits          | cardinality, Hamming distance   |
| `LeadingZeroCount`           | zeros above the top set bit | `Log2`, bucket sizing           |
| `TrailingZeroCount`          | index of the lowest set bit | iterating set bits              |
| `RoundUpToPowerOf2`          | next power of two           | hash table and buffer sizing    |
| `RotateLeft` / `RotateRight` | rotation                    | hashing, checksums              |
| `IsPow2`                     | bool                        | enabling mask instead of modulo |

``` csharp
// Iterate only the set bits - O(set bits), not O(universe):
for (int w = 0; w < bits.Length; w++) {
    ulong word = bits[w];
    while (word != 0) {
        int bit = BitOperations.TrailingZeroCount(word);
        Handle((w << 6) + bit);
        word &= word - 1;                    // clear the lowest set bit
    }
}
```

> **\[Trap\]**
>
> **Sparse data defeats it.** A bitset over ten million possible ids costs 1.25 MB whether it holds 10 members or 10 million. If the set is sparse relative to its universe, `HashSet<int>` or a roaring-bitmap-style hybrid (dense blocks where dense, sorted arrays where sparse) wins. The crossover is roughly 1–2% density.

Worked example: [Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)
