---
id: "1-6"
number: "1.6"
title: "Storage and files"
part: 1
part_title: "The machine"
words: 7393
minutes: 32
---

# 1.6 Storage and files

> **Quick look.** How files and storage really work, and why "saved" doesn't always mean safe.
>
> - Writes pass through caches; data survives a power cut only after it is flushed to the device.
> - Replace a file safely by writing a temporary copy and renaming it over the original.
> - Stream large files instead of loading them into memory; build paths with Path.Combine.
> - Linux file names are case-sensitive; handles are a limited resource ("too many open files").
> - Block, file and object storage are different; never run a database on network file storage.
> - Replication copies mistakes instantly; only tested backups let you go back.


### The big picture

Main memory forgets everything when the power goes off. Storage, meaning hard disks and SSDs, remembers. This chapter is about how programs keep data safely: what a file really is, how the operating system holds data in memory before it reaches the disk, and why "saved" doesn't always mean safe.

These details decide whether a crash loses a user's work, whether another program can see a half-written file, and why code that works on a Windows laptop fails in a Linux container. The chapter ends with the storage you meet in the cloud, and why a copy of your data is not the same as a backup.

### Before you start

**Builds on:** 1.3 Text is numbers too, 1.1 Bits and bytes.

**Ideas explained later:**

- **Operating system** (1.7): The software that shares hardware among programs and protects them from each other.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Replication** (7.6): Keeping copies of the same data on several machines.
- **Controller** (8.3): A class whose action methods handle HTTP requests, with attributes for routing.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Copy-on-write** (4.4): Sharing data until the first change, then copying it all.
- **Transaction** (7.1): A group of database changes that happen all together or not at all.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.

**The code uses:** variables and var, methods and local functions, string interpolation, collection expressions, lambdas, switch expressions and patterns, tuples, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. When File.WriteAllText returns, is the data safe from a power cut?**

> Not necessarily. It may still sit in the operating system's cache in RAM. Only a flush to the device, called fsync on Linux, asks for it to be written for real.

**2. How do you replace a file so that readers never see it half-written?**

> Write the new content to a temporary file in the same folder. Flush it. Then rename it over the original. A rename on the same volume happens in one step.

**3. Why can File.Exists("Config.json") be true on Windows and false in a Linux container?**

> Linux file systems are case-sensitive. If the file is called `config.json`, Windows finds it and Linux does not.

**4. How do you count lines in a 20 GB log without running out of memory?**

> Stream it. `File.ReadLines` reads one line at a time. `File.ReadAllLines` loads the entire file into memory first.

**5. Why can a 1-byte file take 4 KB on disk?**

> File systems hand out space in whole blocks, usually 4 KB. The smallest file still takes a full block.

**6. Is a file's extension a reliable guide to what it contains?**

> No. The extension is just part of the name. Most formats start with a few fixed bytes, called a magic number, that identify them. Check those for anything uploaded or untrusted.

**7. Why is a replica not a backup?**

> Replication copies every change at once, including deletions and corruption. A backup keeps older versions you can go back to.

**8. Does a journaling file system protect the contents of your files in a crash?**

> Mainly it protects the file system's own structure: folders, sizes and free space. The content of a file being written can still be partly old and partly new.

### Memory forgets, storage remembers

RAM loses its contents when the power goes off. Storage keeps them. Storage is also far larger and far slower.

Two kinds of storage device are common.

A **hard disk drive** (HDD) stores data on spinning magnetic platters. An arm moves a head to the right track, then waits for the platter to turn. That wait, called a **seek**, takes 5 to 10 milliseconds. Reading data that sits next to each other is much faster than jumping around.

A **solid-state drive** (SSD) stores data in flash memory chips. It has no moving parts, so there is no seek. Random reads take tens of microseconds. Flash has its own rules. It reads and writes in pages of a few kilobytes, but it can only erase in much larger blocks. Each block survives a limited number of erase cycles. The drive's own controller hides all this and spreads wear across the chips.

### Sectors and blocks

Storage devices don't read or write single bytes. They work in fixed chunks called **sectors**, usually 512 or 4,096 bytes. Changing one byte means reading the whole sector, changing it and writing it back.

File systems hand out space in **blocks**, also called clusters, usually 4 KB. A 1-byte file still takes a whole block. That is why Windows shows both "Size" and "Size on disk" for a file.

### What a file is

A **file** is a named sequence of bytes, plus some **metadata**. The metadata includes its size, timestamps and permissions. That is all a file is. Whether the bytes are "text", an image or a database is up to the program reading it.

A **file system** is the scheme that maps names to the places on the device where the bytes live. Common ones are NTFS on Windows, ext4 and XFS on Linux, and APFS on macOS. A **directory**, or folder, is a special entry that lists other names.

A text file is just bytes that a program agrees to read as characters. Two details trip people up. The encoding, covered in Text is numbers too (1.3). And line endings. Windows traditionally ends lines with two characters, carriage return and line feed (CRLF). Linux and macOS use only line feed (LF).

### How file systems survive crashes

Creating or growing a file changes several places on the device. The file system must mark blocks as used, write the data, update the file's size and add its name to a folder. A crash between those steps could leave the file system itself broken.

Modern file systems use **journaling** to prevent this. Before changing anything, they write a short note of what they are about to do into a log called the journal. After a crash, they read the journal and finish or undo the half-done change. NTFS, ext4 and XFS work this way. APFS and ZFS use a related method called copy-on-write: they never overwrite live data, they write new copies and then switch a pointer.

This protects the file system's structure. It does not, by default, protect the contents of the file you were writing. That part is still your job, using the techniques below.

### From your code to the device

A write passes through several layers before it reaches the device. Each layer can hold on to the data for a while.

First, your program's own buffer. A `FileStream` or `StreamWriter` collects small writes and hands them on in bigger chunks. Second, the operating system's **page cache**. The OS keeps recently written and read file data in spare RAM. Third, the drive's own cache. Finally, the storage chips or platters.

When `Write` returns, your data has usually only reached the first or second layer. That is why writes feel fast. It is also why a crash or power cut can lose data you "wrote".

### Durability

To make data survive a power cut, you must ask the operating system to push it all the way to the device. On Linux this call is **fsync**. In .NET it is `FileStream.Flush(flushToDisk: true)`.

It is slow, because it waits for the device. Databases call it every time a transaction commits. That is why commits have a real cost, and why Transactions and isolation (7.4) spends time on batching.

### Replacing a file safely

Overwriting a file in place is risky. If the program crashes halfway, the file holds half old and half new content. A reader can also see it mid-write.

The standard fix has three steps. Write the full new content to a temporary file in the same folder. Flush it to the device. Rename the temporary file over the original. On the same volume, a rename is a single step: readers see either the old file or the new one, never a mix.

### Reading large files

Loading a whole file into memory is fine for a config file. It is not fine for a 20 GB log. Read large files as a **stream**, a piece at a time. Memory use then stays flat no matter how big the file is.

```csharp
// files.cs — run with: dotnet run files.cs
using System.Text;

// 1. Replace a file safely
await SaveAtomicallyAsync("settings.json", "{\"theme\":\"dark\"}");
Console.WriteLine(File.ReadAllText("settings.json"));   // {"theme":"dark"}

// 2. Make a test log, then read it as a stream
await using (var writer = new StreamWriter("big.log"))
    for (int i = 0; i < 100_000; i++)
        await writer.WriteLineAsync(i % 100 == 0 ? $"{i} ERROR disk full" : $"{i} ok");

long errors = 0;
foreach (string line in File.ReadLines("big.log"))      // one line in memory at a time
    if (line.Contains("ERROR", StringComparison.Ordinal)) errors++;
Console.WriteLine($"errors: {errors}");                 // errors: 1000

// 3. Build paths portably
string path = Path.Combine("data", "flights", "today.csv");
Console.WriteLine(path);   // data/flights/today.csv on Linux, data\flights\today.csv on Windows

static async Task SaveAtomicallyAsync(string path, string content)
{
    string temp = path + ".tmp";
    await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        await fs.WriteAsync(Encoding.UTF8.GetBytes(content));
        fs.Flush(flushToDisk: true);          // push past the OS cache to the device
    }
    File.Move(temp, path, overwrite: true);   // swap in one step
}
```

For complete safety on Linux, the folder entry itself should be flushed after the rename too. Databases do this. For application settings and exports, the pattern above is enough.

### Asynchronous file access

Reading a file makes the thread wait for the device. In a server handling many requests, a blocked thread can't do other work. Asynchronous methods such as `ReadAsync` and `File.ReadAllTextAsync` let the thread go back to the thread pool while the device works. Why that matters is covered in Async and await (5.3).

For the best asynchronous behaviour on Windows, open the stream with `FileOptions.Asynchronous`. `RandomAccess`, added in .NET 6, reads and writes at a given position without a stream at all. It suits code that jumps around inside large files.

### Paths and portability

Windows separates folders with `\`. Linux and macOS use `/`. Build paths with `Path.Combine` rather than joining strings.

Windows file names are case-insensitive. Linux file names are case-sensitive. Code developed on Windows and deployed in a Linux container meets this difference sooner or later.

### File formats and magic numbers

A file extension like `.png` is only part of the name. Anyone can rename a file. The real format is in the bytes.

Most binary formats start with a fixed sequence called a **magic number**. PNG images start with `89 50 4E 47`, which includes the letters "PNG". PDF files start with `%PDF`. ZIP archives start with `PK`. Word and Excel files are ZIP archives too, so they start with `PK` as well.

### Compression

**Compression** makes data smaller by finding repetition and describing it more briefly. Text, JSON and logs often shrink five to ten times. Photos, videos and ZIP files are already compressed, and compressing them again gains almost nothing.

The common formats are **gzip**, older and supported everywhere, and **Brotli**, newer and usually smaller. Web servers use both to shrink HTTP responses. Compression trades processor time for fewer bytes to store or send.

### Checking files haven't changed

A **cryptographic hash** such as SHA-256 turns any amount of data into a 32-byte value. Changing a single bit of the input changes the hash completely. Download sites publish hashes so you can check a file arrived intact. Build systems and Git use hashes to notice what changed.

```csharp
// formats.cs — run with: dotnet run formats.cs
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

File.WriteAllBytes("sample.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
Console.WriteLine(Identify("sample.png"));        // PNG image

byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("flight LHR-JFK on time\n", 10_000)));
using (var output = File.Create("log.txt.gz"))
using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
    gzip.Write(text);
Console.WriteLine($"{text.Length} bytes became {new FileInfo("log.txt.gz").Length} bytes");
Console.WriteLine(Identify("log.txt.gz"));        // gzip data

using (var fs = File.OpenRead("log.txt.gz"))
    Console.WriteLine(Convert.ToHexString(SHA256.HashData(fs)));   // 64 hex digits; same bytes, same hash

static string Identify(string path)
{
    Span<byte> head = stackalloc byte[8];
    using var fs = File.OpenRead(path);
    int n = fs.Read(head);
    return head[..n] switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "PNG image",
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..] => "PDF document",
        [(byte)'P', (byte)'K', 3, 4, ..] => "ZIP archive, which includes .docx and .xlsx",
        [0x1F, 0x8B, ..] => "gzip data",
        _ => "unknown",
    };
}
```

### Memory-mapped files

A **memory-mapped file** makes a file appear as a range of memory. The OS loads pages of the file only when they are touched, using the page faults described in The operating system (1.7). Reading a value is then just reading memory. This suits large files read in random order. Some databases are built on it. In .NET it is the `MemoryMappedFile` class.

### Watching for changes

`FileSystemWatcher` raises events when files change. Treat those events as hints, not facts. One save can raise several events, because many editors write a temporary file and rename it. Events arrive out of order. When changes come fast, the internal buffer overflows and events are lost. On network drives and some container mounts, events may never arrive at all.

Wait briefly after an event for things to settle, then look at the folder yourself and work out what changed.

### Permissions

Every file has an owner and a set of permissions. On Linux, permissions are read, write and execute, set separately for the owner, the owner's group and everyone else, as in the `chmod` example in Bits and bytes (1.1). Windows uses access control lists, which are more detailed lists of who may do what.

This matters in containers. The official .NET images since .NET 8 include a non-root user called `app`. Running as that user is safer, but the process can then only write where that user has permission.

### Temporary files

`Path.GetTempFileName` creates an empty file with a unique name. On Windows it fails once 65,535 such files exist in the temp folder, and many programs never delete theirs. `Directory.CreateTempSubdirectory`, added in .NET 7, gives you a private folder. Delete temporary files when you are done.

### Beyond the local disk

**Network file systems** such as SMB on Windows and NFS on Linux make a remote disk look local. They are slower, the connection can drop, and locking works differently. Code that is fast and reliable on a local disk can be neither on a share.

**Object storage**, such as Amazon S3 and Azure Blob Storage, is not a file system. You store and fetch whole objects over HTTP. There is no appending or changing part of an object. Folders are only name prefixes, so there is no atomic rename: "renaming" a folder means copying and deleting every object in it. In return it is cheap, very durable and nearly unlimited in size.

**RAID** spreads data across several disks so that one failing disk loses nothing. **Replication** keeps copies on other machines. Both copy mistakes instantly. Delete a file, and it is gone from every copy. Only **backups** keep older versions. A common rule is 3-2-1: three copies, on two kinds of media, with one off-site. A backup you have never restored is only a hope. Test restores.

### In short

A file is a named sequence of bytes, and a file system maps names to places on the device. Writes pass through several caches, so data isn't safe from a power cut until it is flushed to the device. Replace a file by writing a temporary copy and renaming it over the original. Read large files as streams, build paths with `Path.Combine`, and remember that Linux file names are case-sensitive. Replication copies mistakes instantly; only backups let you go back.

### What goes wrong

**Assuming a write is durable when the call returns.** A crash loses the last few seconds of "saved" data. Flush to disk where it matters, or let a database handle it.

**Readers seeing half-written files.** Another process picks up an export before it is complete. Use the temporary-file-and-rename pattern.

**Case sensitivity.** `Config.json` versus `config.json` works on Windows and fails in production on Linux.

**Loading huge files into memory.** `ReadAllText` or `ReadAllBytes` on a large file ends in an `OutOfMemoryException` or a container killed for using too much memory.

**Line endings.** Shell scripts saved with CRLF fail inside Linux containers with errors like `bad interpreter`. Git diffs show every line as changed. Set line-ending rules in a `.gitattributes` file.

**Locked files on Windows.** An editor, antivirus scanner or another process holding a file open makes your write fail with an `IOException`. Retry briefly with a delay.

**Temporary container storage.** Files written inside a container disappear when the container is replaced. Keep data on a mounted volume, in a database or in object storage.

**Many tiny files.** Each file has metadata to read and update. A million 1 KB files are far slower to handle than one 1 GB file.

**Trusting extensions and names in uploads.** A file called `photo.jpg` can be anything. Check the magic number, limit the size and never use the uploaded name as a path. More in Common web vulnerabilities (10.4).

**Relying on `FileSystemWatcher`.** Missed or duplicated events leave a folder half-processed. Re-scan the folder on a timer as a safety net.

**Permission errors after going non-root.** A container that used to run as root fails with `UnauthorizedAccessException` when writing to its own folder. Write to a mounted volume or a folder the `app` user owns.

**Filling the temp folder.** `GetTempFileName` starts throwing `IOException` on a busy Windows server. Clean up, or use your own temporary folder.

**Treating object storage like a disk.** Renaming a "folder" with millions of objects takes hours and costs money. Design key names so you never need to rename.

**Untested backups.** The backup job succeeded every night, and the restore fails. Restore to a test system on a schedule.

### Cost model

A random read costs 5 to 10 ms on a hard disk and 20 to 100 µs on an SSD. Sequential reads on a modern NVMe SSD run at several gigabytes per second. A flush to disk can take from well under a millisecond to tens of milliseconds, depending on the device. Each read or write call also crosses into the operating system, which costs something on its own. That is why buffering matters, as the next chapter explains.

gzip and Brotli cost processor time: fast settings compress at hundreds of megabytes per second, maximum settings far slower. SHA-256 runs at around a gigabyte per second or more per core on processors with hardware support for it. One object storage request takes tens of milliseconds and is billed per request, so many small objects cost more than a few large ones.

### In other languages (optional)

**Node.js.** `fs/promises` has asynchronous versions of every file call. `fs.createReadStream` streams large files. `fs.rename` replaces a file in one step on the same volume.

**Python.** Use `with open(path, encoding="utf-8") as f:` so the file closes even after an error. `os.replace` swaps a file in one step, and `os.fsync` flushes it to the device.

**Java.** `Files.move` with the `ATOMIC_MOVE` option does the rename swap.

**SQL Server.** A database is a data file (`.mdf`) and a transaction log file (`.ldf`). The log is written in order and flushed to disk on every commit. That makes the log's disk speed a limit on how many commits per second you can do.

### Key terms

- **HDD**: Hard disk drive. Stores data on spinning magnetic platters.
- **SSD**: Solid-state drive. Stores data in flash memory chips, with no moving parts.
- **Seek**: Moving a hard disk's head to the right track. Takes milliseconds.
- **Sector**: The fixed-size chunk a storage device reads and writes, usually 512 or 4,096 bytes.
- **Block**: The unit in which a file system hands out space, usually 4 KB.
- **File system**: The scheme that maps file names to the places on a device where their bytes live.
- **Metadata**: Data about a file, such as its size, timestamps and permissions.
- **Journaling**: Writing a note of a change before making it, so a file system can recover from a crash.
- **Page cache**: The operating system's copy of recently used file data in spare RAM.
- **fsync**: The Linux call that pushes a file's data from the OS cache to the device.
- **Durability**: The guarantee that saved data survives a crash or power cut.
- **Atomic rename**: Replacing one file with another in a single step, so readers never see a mix.
- **Stream**: Reading or writing data a piece at a time instead of all at once.
- **Magic number**: Fixed bytes at the start of a file that identify its format.
- **Memory-mapped file**: A file made to appear as a range of memory, loaded page by page on demand.
- **Object storage**: Storage for whole objects over HTTP, such as S3. Not a file system.
- **Backup**: A separate copy that keeps older versions, so mistakes can be undone.

### Where this leads

How programs ask the operating system for file access is in The operating system (1.7). Why databases exist on top of files is in Why databases exist (7.1). Commits and durability come back in Transactions and isolation (7.4). Volumes and container storage are in Containers and Docker (11.3).

### Check yourself

1. Write a large file directly with `File.WriteAllText`, and kill the program partway with Ctrl+C. Do the same with the temporary-file pattern. Compare what a reader sees each time.
2. Count the lines of a file over 1 GB with `File.ReadAllLines`, then with `File.ReadLines`. Watch the memory in Task Manager or `top`.
3. Add a `.gitattributes` file to a repository that forces LF line endings for `*.sh` files.
4. Extend `Identify` in `formats.cs` to recognize JPEG files, which start with `FF D8 FF`. Test it on a real photo.
5. Compress a JSON file and a JPEG photo with `GZipStream`. Compare how much each shrinks and explain the difference.
6. Run a `FileSystemWatcher` on a folder and save a file in your editor. Count how many events one save raises.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A config file half-written during a crash

A service writes its settings file on change. After a power cut it won't start: the JSON is truncated.

*Write, flush, then atomically replace*

```csharp
static async Task SaveAtomicAsync(string path, string json)
{
    var tmp = path + ".tmp";
    await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                         4096, FileOptions.WriteThrough))
    {
        await fs.WriteAsync(Encoding.UTF8.GetBytes(json));
        fs.Flush(flushToDisk: true);
    }
    File.Move(tmp, path, overwrite: true);   // readers see the old file or the new one, never half
}
```

> **Lesson:** "The write returned" doesn't mean the data is on disk. Write to a temp file, flush, then rename over the original.

#### Example 2: An export that loaded 3 GB into memory

Downloading a year of flight logs crashes the API with OutOfMemoryException.

*Stream instead of buffering*

```csharp
app.MapGet("/logs/{year:int}", (int year, LogStore store) =>
    Results.Stream(async output =>
    {
        await using var writer = new StreamWriter(output);
        await foreach (var line in store.ReadLinesAsync(year))   // IAsyncEnumerable<string>
            await writer.WriteLineAsync(line);
    }, "text/csv", $"logs-{year}.csv"));
```

> **Lesson:** Read and write large files as streams, so memory stays constant whatever the file size.

### How a computer runs a program

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How a computer runs a program</p>

<a id="be-m9"></a>

#### Files, handles, and the page cache

A **file** is a named sequence of bytes the OS knows how to find on a device. When you open one, the kernel returns a **handle** (Windows) or **file descriptor** (Unix): a small integer indexing a per-process table of open things.

``` text
Your process                Kernel
 fd 0  stdin       ──────►  ┌─ open file table ─┐
 fd 1  stdout      ──────►  │ position, mode,   │ ──► inode / file record
 fd 2  stderr      ──────►  │ flags, refcount   │      on the actual device
 fd 7  orders.csv  ──────►  └───────────────────┘
 fd 8  a TCP socket ─────►  (sockets use the same table - hence "everything
                             is a file" on Unix)
```

**Handles are a scarce, process-wide resource**, and unlike memory they are not reclaimed by the garbage collector. This is precisely why `IDisposable` exists as a concept separate from memory management ([The heap, and why it needs a collector](02-07-memory-management-and-garbage-collection.md#be-m24), [Handles and lifetimes across the boundary](02-02-compilers-interpreters-and-jit.md#be-m96)).

The kernel keeps recently-used file pages in the **page cache** — unused RAM used as a disk cache. A read that hits it never touches the disk.

``` text
// This is why the second run of a benchmark is faster, and why
// "the database is fast this morning" is often just a warm cache.

File.WriteAllBytes(path, data);       // returns once the data is in the PAGE CACHE
// It is NOT on the disk yet. A power loss here loses it.
fileStream.Flush(flushToDisk: true);  // forces fsync - now it is durable, and slow

// A database's durability guarantee rests entirely on this distinction:
// COMMIT does not return until the write-ahead log has been fsynced
// (§168).
```

> **\[Trap\]**
>
> **Handle exhaustion looks like something else.** Leaking file or socket handles produces "too many open files" or a failure to connect — often reported far from the leak, in unrelated code that simply happened to need a handle next. `lsof -p` or Process Explorer counts them; a steadily climbing count under load is the signature ([Connection pooling and exhaustion](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462)).

### Text, bytes, and streams

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Text, bytes, and streams</p>

<a id="be-m88"></a>

#### Binary layout: endianness and BinaryPrimitives

Multi-byte numbers have a byte order. x86, x64 and ARM in normal configuration are **little-endian**; network protocols and most file formats are **big-endian**. Ignoring this produces code that works on your machine and corrupts data on someone else's.

``` csharp
// BitConverter uses the platform's order - fine for round-tripping within one
// process, wrong for anything that crosses a boundary.
BitConverter.IsLittleEndian;                 // true on x64/ARM64
BitConverter.GetBytes(1)                     // 01 00 00 00 here, 00 00 00 01 elsewhere

// BinaryPrimitives is explicit. Always use it for wire and file formats.
Span<byte> buf = stackalloc byte[16];
BinaryPrimitives.WriteInt32BigEndian(buf, length);           // network byte order
BinaryPrimitives.WriteUInt64LittleEndian(buf[4..], id);
int len = BinaryPrimitives.ReadInt32BigEndian(buf);
BinaryPrimitives.TryReadInt64LittleEndian(buf[4..], out long v);

BinaryPrimitives.ReverseEndianness(value);   // single bswap instruction
```

``` csharp
// A framed message header - the shape almost every binary protocol has.
readonly record struct Header(int Length, ushort Type, ushort Flags, long Timestamp) {
    public const int Size = 16;

    public void WriteTo(Span<byte> d) {
        BinaryPrimitives.WriteInt32BigEndian(d[..4], Length);
        BinaryPrimitives.WriteUInt16BigEndian(d[4..6], Type);
        BinaryPrimitives.WriteUInt16BigEndian(d[6..8], Flags);
        BinaryPrimitives.WriteInt64BigEndian(d[8..16], Timestamp);
    }

    public static bool TryRead(ReadOnlySpan<byte> d, out Header h) {
        h = default;
        if (d.Length < Size) return false;
        h = new Header(
            BinaryPrimitives.ReadInt32BigEndian(d[..4]),
            BinaryPrimitives.ReadUInt16BigEndian(d[4..6]),
            BinaryPrimitives.ReadUInt16BigEndian(d[6..8]),
            BinaryPrimitives.ReadInt64BigEndian(d[8..16]));
        return true;
    }
}
```

> **\[Trap\]**
>
> **Do not blit a struct to the wire.** `MemoryMarshal.AsBytes` over a struct is fast and correct only when both ends share the same architecture, the same padding, and the same field order — and the CLR does not guarantee field order for managed structs ([Struct layout, padding, and alignment](01-05-memory.md#be-m31)). Write each field explicitly. It is a few more lines and it survives a change of platform, compiler, or field ordering.

> **\[Tip\]**
>
> **Hex and Base64 have first-class APIs now.** `Convert.ToHexString`/`FromHexString` (.NET 5) and `Base64Url` (.NET 9) beat hand-rolled loops and `BitConverter.ToString(b).Replace("-","")` on both speed and clarity. Remember Base64 costs 33% in size and hex costs 100% — neither belongs in a hot storage path if raw bytes are an option.

Goes deeper: [Marshalling to native code](02-02-compilers-interpreters-and-jit.md#be-m95)

First principles: [What a network actually is](06-01-layers-and-packets.md#be-e490)

<a id="be-m89"></a>

#### Streams, buffering, and file I/O

The most common I/O performance bug is reading or writing in the wrong unit — byte-at-a-time through an unbuffered stream, or the whole file into memory when a window would do.

``` csharp
// Options that actually change behaviour:
new FileStream(path, new FileStreamOptions {
    Mode = FileMode.Open, Access = FileAccess.Read,
    Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
    BufferSize = 64 * 1024                       // 0 disables FileStream's own buffering
});
// SequentialScan  - tells the OS cache not to retain pages; big win on large scans
// RandomAccess    - the opposite hint
// Asynchronous    - true async I/O; without it, async methods block a thread
// WriteThrough    - bypass the OS write cache (durability over throughput)
```

``` csharp
// Positional reads without a stream at all (.NET 6+) - thread-safe, no seek state:
using var handle = File.OpenHandle(path, options: FileOptions.RandomAccess);
long size = RandomAccess.GetLength(handle);
int read = await RandomAccess.ReadAsync(handle, buffer, fileOffset: 4096);

// Memory-mapped, for random access over a file larger than you want to buffer:
using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
```

| Task                                   | Use                                                        |
|----------------------------------------|------------------------------------------------------------|
| read a small config file               | `File.ReadAllTextAsync`                                    |
| process a large text file line by line | `File.ReadLinesAsync` / `StreamReader` with a 64 KB buffer |
| copy between streams                   | `source.CopyToAsync(dest, 81920)`                          |
| parse a binary protocol                | `PipeReader` ([System.IO.Pipelines](#be-m90))              |
| random access into a large file        | `RandomAccess` or a memory-mapped view                     |
| build a response body                  | write directly to `Response.Body`, never buffer            |

> **\[Trap\]**
>
> **`FileOptions.Asynchronous` is not the default,** and without it `ReadAsync` on a `FileStream` performs synchronous I/O on a thread pool thread. Under load that is thread pool starvation with an async-looking call stack ([Async data access: starvation and deadlock](05-03-async-and-await.md#be-m136)). If you await file I/O, open the handle asynchronously.

> **\[Tip\]**
>
> **Wrap once, dispose in order.** `StreamWriter` over `GZipStream` over `FileStream` must be disposed outermost-first, or the compressor's trailer never gets flushed and the file is silently truncated. `await using` with declaration statements gets the order right automatically; manual `Dispose()` calls frequently do not.

Worked example: [Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299)

First principles: [System calls: crossing into the kernel](01-07-the-operating-system.md#be-m8)

<a id="be-m90"></a>

#### System.IO.Pipelines

Streams push the buffer-management problem onto you: how big should the buffer be, what happens when a message spans two reads, who owns the array. `Pipelines` solves those once. It is the machinery Kestrel is built on.

``` csharp
static async Task ReadFramesAsync(PipeReader reader, CancellationToken ct) {
    while (true) {
        ReadResult result = await reader.ReadAsync(ct);
        ReadOnlySequence<byte> buffer = result.Buffer;

        while (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
            Process(frame);

        // consumed = what we took; examined = how far we looked.
        // Getting this wrong is the classic Pipelines bug: if examined does not
        // advance, ReadAsync returns the same data forever - an infinite loop.
        reader.AdvanceTo(buffer.Start, buffer.End);

        if (result.IsCompleted) break;
    }
    await reader.CompleteAsync();
}

static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame) {
    frame = default;
    if (buffer.Length < 4) return false;                  // header not here yet

    Span<byte> lenBytes = stackalloc byte[4];
    buffer.Slice(0, 4).CopyTo(lenBytes);
    int length = BinaryPrimitives.ReadInt32BigEndian(lenBytes);

    if (buffer.Length < 4 + length) return false;         // body incomplete - wait
    frame  = buffer.Slice(4, length);
    buffer = buffer.Slice(4 + length);
    return true;
}
```

`ReadOnlySequence<byte>` is the key type: a possibly-discontiguous view over several buffer segments. It lets a message span buffer boundaries without a copy, at the cost of not being a single `Span`.

``` csharp
// Handle both shapes rather than assuming contiguity:
static int ReadInt32(in ReadOnlySequence<byte> seq) {
    if (seq.FirstSpan.Length >= 4)
        return BinaryPrimitives.ReadInt32BigEndian(seq.FirstSpan);   // fast path
    Span<byte> tmp = stackalloc byte[4];
    seq.Slice(0, 4).CopyTo(tmp);
    return BinaryPrimitives.ReadInt32BigEndian(tmp);
}
// SequenceReader<byte> wraps this pattern for you and is usually the better choice.
```

> **\[Why it matters\]**
>
> **When it is worth the complexity.** A custom wire protocol, a high-throughput proxy, a log or event ingester — anywhere you are parsing a byte stream at scale and copies dominate. For reading a CSV or an HTTP body that a framework already hands you, a `StreamReader` or `Utf8JsonReader` is the right tool and Pipelines is over-engineering ([Span and Memory](02-07-memory-management-and-garbage-collection.md#be-b273)).

First principles: [TCP: reliability built on unreliability](06-03-tcp-and-udp.md#be-m14)

<a id="be-m91"></a>

#### Compression: when and what

| Algorithm              | Ratio                     | Speed                            | Use for                                      |
|------------------------|---------------------------|----------------------------------|----------------------------------------------|
| `DeflateStream`        | good                      | fast                             | raw deflate, no framing                      |
| `GZipStream`           | good                      | fast                             | files, HTTP — universally supported          |
| `ZLibStream` (.NET 6+) | good                      | fast                             | zlib framing, protocol interop               |
| `BrotliStream`         | **best** (~15–25% better) | slower to compress, fast to read | HTTP responses, cached values, static assets |

``` csharp
// Compression level matters more than the algorithm choice.
// Brotli's default (level 4 in .NET) is a reasonable balance;
// SmallestSize (level 11) can be an order of magnitude slower to compress.
using var output = new MemoryStream();
await using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
    await JsonSerializer.SerializeAsync(brotli, payload, ct);
var bytes = output.ToArray();

// Decompress straight into the deserializer - no intermediate byte[] or string:
await using var input = new BrotliStream(stream, CompressionMode.Decompress);
var payload = await JsonSerializer.DeserializeAsync<T>(input, cancellationToken: ct);
```

> **\[Tip\]**
>
> **Compressing distributed cache values is usually a win.** A JSON payload typically compresses 5–10×, and you pay that saving twice — Redis memory and network transfer both shrink. Below roughly 1 KB the framing overhead and CPU are not worth it, so compress conditionally on size and record which entries are compressed (a flag byte, or a suffix in the key version) ([Cache key design and value format](07-06-non-relational-stores.md#be-m207)).

> **\[Trap\]** **Two things not to do.** Do not compress data that is already compressed — images, video, archives, encrypted blobs — you spend CPU to grow the payload slightly. And do not decompress untrusted input without a size limit: a *zip bomb* is a few kilobytes that expands to gigabytes and OOMs the process. Cap the decompressed length as you read.
>
> ``` csharp
> var limited = new BrotliStream(input, CompressionMode.Decompress);
> await limited.ReadExactlyAsync(buffer.AsMemory(0, MaxBytes), ct);   // bound it explicitly
> ```

### Storage & Filesystems

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Storage & Filesystems</p>

<p class="pd">Where the bytes actually live. Storage types, what disk performance numbers mean, and how to reason about durability without confusing it with availability.</p>

<a id="be-g667"></a>

#### Block, file and object storage

| Type       | Interface                           | Looks like                     | Use for                                                             |
|------------|-------------------------------------|--------------------------------|---------------------------------------------------------------------|
| **Block**  | Read/write blocks at offsets        | A raw disk you format yourself | Databases, VM boot disks — anything needing a filesystem            |
| **File**   | POSIX filesystem over a network     | A shared mounted directory     | Shared config, legacy apps expecting a filesystem, home directories |
| **Object** | HTTP: PUT/GET a whole object by key | A key-value store for blobs    | Backups, media, static assets, data lakes, logs                     |

``` text
  the practical differences

  BLOCK        attached to ONE machine at a time (usually)
               lowest latency, highest IOPS
               you manage the filesystem, growth, and backup
               EBS, Azure Managed Disks, Persistent Disk

  FILE         mounted by MANY machines simultaneously
               network latency on every operation - much slower than local
               convenient, and frequently the wrong answer for performance
               EFS, Azure Files, Filestore, NFS

  OBJECT       accessed by anything with credentials, from anywhere
               no partial writes: you replace the whole object
               effectively unlimited, cheap, extremely durable
               NO filesystem semantics - "directories" are just key prefixes
               S3, Azure Blob, GCS
```

| Requirement                         | Choose                                          |
|-------------------------------------|-------------------------------------------------|
| Database data files                 | Block, always. Never network file storage       |
| Uploaded user files                 | Object — and serve via CDN                      |
| Shared configuration across servers | A config service, not shared file storage       |
| Backups and archives                | Object, with lifecycle rules to colder tiers    |
| Container images                    | A registry (object storage underneath)          |
| Application logs                    | Ship to a log system; do not accumulate on disk |
| Legacy app expecting a shared drive | File storage — the reason it still exists       |

> **\[Trap\]**
>
> **Common trap.** Running a database on network file storage (NFS, EFS, Azure Files) is one of the more damaging infrastructure mistakes available. The latency per operation is many times that of block storage, locking semantics over NFS are unreliable, and a network hiccup can corrupt data files. Every database vendor advises against it. Use block storage, and put your *backups* in object storage.

<a id="be-g668"></a>

#### Disk performance: IOPS, throughput and latency

Three different numbers that people conflate constantly. Which one limits you depends entirely on the workload.

``` text
  IOPS         operations per second. Matters for MANY SMALL random reads/writes.
               -> a transactional database is IOPS-bound.

  THROUGHPUT   MB/s. Matters for FEW LARGE sequential transfers.
               -> a backup, a table scan, a video stream is throughput-bound.

  LATENCY      time for ONE operation to complete.
               -> what a user actually experiences. The number that matters most,
                  and the one dashboards most often omit.

  they trade off: queueing more operations raises throughput and RAISES latency.
  a disk at 100% utilisation has maximum throughput and terrible latency.
```

| Storage                        | Latency      | IOPS                      | Note                                                      |
|--------------------------------|--------------|---------------------------|-----------------------------------------------------------|
| Spinning disk (HDD)            | ~5–10 ms     | ~100–200                  | Sequential only; effectively obsolete for databases       |
| Network SSD (gp3, Premium SSD) | ~0.5–2 ms    | 3,000–16,000 configurable | The usual cloud default                                   |
| Provisioned IOPS (io2)         | \<1 ms       | Up to 256,000             | For demanding databases; expensive                        |
| Local NVMe                     | ~0.05–0.2 ms | 100,000s                  | Fastest, and **ephemeral** — lost when the instance stops |
| Network file (EFS)             | ~2–10 ms     | Varies                    | Not for databases                                         |

``` text
# measure, do not assume
iostat -xz 5
#   r_await / w_await : LATENCY per operation, in ms.  <1ms good, >10ms bad
#   %util             : how busy. Misleading on SSDs, which handle parallelism
#   aqu-sz            : queue depth. Consistently high = saturated

fio --name=randread --ioengine=libaio --direct=1 --rw=randread \
    --bs=8k --numjobs=4 --iodepth=32 --size=4G --runtime=60 --group_reporting
# 8k random read is a reasonable proxy for a database's page reads.
# --direct=1 bypasses the page cache, so you measure the DEVICE.
```

| Cloud storage trap          | Detail                                                                                                                                     |
|-----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| **Burst credits**           | Small gp2 volumes burst, then collapse to a low baseline when credits are exhausted — performance falls off a cliff hours into a load test |
| **IOPS scale with size**    | On many tiers, a bigger disk is a faster disk. Sometimes you provision size purely for IOPS                                                |
| **Instance-level limits**   | The VM has its own EBS bandwidth cap, independent of the volume's. A fast disk on a small instance is throttled by the instance            |
| **Local NVMe is ephemeral** | Stopping the instance loses the data. Excellent for caches and scratch, never for the only copy                                            |
| **First read is slow**      | Volumes restored from a snapshot lazily fetch blocks on first access — pre-warm before a benchmark or a cutover                            |

###### Reading iostat, and what the numbers mean together

``` bash
$ iostat -xz 5
Device   r/s     w/s   rkB/s   wkB/s  r_await  w_await  aqu-sz  %util
nvme0n1  1240    380   9920    3040     0.31     0.44    0.62   28.4
                 ^^^^^                  ^^^^^^          ^^^^^^  ^^^^^
                 IOPS                   LATENCY         QUEUE   BUSY

  r_await / w_await   milliseconds per operation. THE NUMBER THAT MATTERS.
                        <1ms   healthy NVMe/SSD
                        1-5ms  acceptable network SSD
                        >10ms  saturated, or a spinning disk
                        >50ms  something is badly wrong

  aqu-sz              average queue depth. Consistently >2-3 with rising
                      await means requests are waiting, not being served.

  %util               MISLEADING ON SSDs. A device that handles 32 parallel
                      requests shows 100% util while barely working, because
                      the metric was designed for single-queue spinning disks.
                      Judge saturation by AWAIT, not by %util.
```

| Pattern                          | Diagnosis                                               |
|----------------------------------|---------------------------------------------------------|
| High `r/s`, low `await`          | Healthy, working hard. Nothing to do                    |
| Low `r/s`, high `await`          | Throttled — burst credits exhausted, or an IOPS cap     |
| `await` rising over time         | Queue building; you are past the knee                   |
| High `w_await`, low `r_await`    | Write-heavy: checkpoint, WAL flush, or a backup running |
| Sudden `await` spike, periodic   | Checkpoint, snapshot, or a neighbour on shared storage  |
| `%util` 100%, `await` under 1 ms | Fine. Ignore `%util` on SSD                             |

<a id="be-g669"></a>

#### Durability, redundancy and snapshots

Three distinct properties routinely treated as one. Confusing them is how teams end up believing they have backups when they have replicas.

| Property           | Question it answers                                 | Provided by                                      |
|--------------------|-----------------------------------------------------|--------------------------------------------------|
| **Durability**     | Will the bytes still be there?                      | Replication within the storage system, checksums |
| **Availability**   | Can I reach them right now?                         | Redundant paths, failover, multiple zones        |
| **Recoverability** | Can I get back the version from before the mistake? | **Backups and snapshots — nothing else**         |

``` text
  what each protects against

                          disk    host    zone    region   HUMAN
                          fails   fails   fails   fails    ERROR
  RAID / replicated disk    Y       -       -       -        -
  multi-AZ replica          Y       Y       Y       -        -
  cross-region replica      Y       Y       Y       Y        -
  snapshots                 Y       Y       Y       Y*       Y
  offsite immutable backup  Y       Y       Y       Y        Y

  the last column is the point. Replication faithfully replicates
  DROP TABLE within milliseconds. Only a point-in-time copy saves you,
  and only if it is far enough back and cannot itself be deleted.
```

| RAID   | Does                 | Note                                                                    |
|--------|----------------------|-------------------------------------------------------------------------|
| **0**  | Stripes across disks | Faster, and *any* disk failing loses everything                         |
| **1**  | Mirrors              | Survives one failure; halves usable capacity                            |
| **5**  | Stripe + one parity  | Rebuild is slow and stressful; a second failure during rebuild is fatal |
| **6**  | Stripe + two parity  | Survives two failures — preferred over 5 on large disks                 |
| **10** | Mirrored stripes     | Best performance and rebuild behaviour; the usual database choice       |

- **Snapshots are usually incremental** — only changed blocks. Fast and cheap, but chained, so retention is not simply “delete the old ones”.
- **A snapshot is crash-consistent, not application-consistent**, unless you quiesce or use a database-aware mechanism. For a database, prefer the engine's own backup.
- **Store backups in a different account or subscription**, with delete protection. Ransomware and compromised credentials target backups first.
- **Test restores on a schedule**, and time them. That duration is your real RTO, whatever the plan claims.
- **Encrypt at rest, and manage the key separately.** A backup is a complete copy of your data with no access control on it.

> **\[Note\]**
>
> **The sentence worth repeating.** **A replica is not a backup.** It is a copy that faithfully reproduces every mistake you make, within milliseconds. High availability and recoverability are different problems requiring different mechanisms, and having one provides no protection whatsoever against the failure mode of the other.
