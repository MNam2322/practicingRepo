---
id: "1-3"
number: "1.3"
title: "Text is numbers too"
part: 1
part_title: "The machine"
words: 10074
minutes: 44
---

# 1.3 Text is numbers too

> **Quick look.** Text is numbers in an agreed table plus an encoding, and most text bugs come from forgetting one of the two.
>
> - Unicode gives every character a code point; UTF-8 and UTF-16 turn code points into bytes.
> - C# strings are UTF-16, so string.Length counts 16-bit units, not visible characters (emoji take two).
> - Compare identifiers and keys with ordinal comparison; use culture-aware comparison only for text shown to people (the Turkish-I bug).
> - Always pass a culture when parsing or formatting; Linux containers may run with invariant globalization.
> - Use StringBuilder in loops, spans for allocation-free parsing, and normalize text before searching.
> - Escape for the exact destination (URL, HTML, JSON) and use parameters for SQL.
> - Put a timeout on any regex run on user input to avoid ReDoS; never build a regex from user input.


### The big picture

Text looks simple until it breaks. A name shows up as "cafÃ©", an emoji counts as two characters, or a search misses "LHR" because someone typed "lhr". All of these come from one fact: computers store text as numbers, using an agreed table that gives each character a number, plus a rule for turning those numbers into bytes.

This chapter walks from the early ASCII table to Unicode, which covers every writing system, and explains why "one character" can mean three different things. Then it covers the practical side: comparing, sorting, searching and escaping text safely in .NET and in databases.

### Before you start

**Builds on:** 1.2 Numbers inside the machine.

**Ideas explained later:**

- **Protocol** (6.1): An agreed set of rules for the format and meaning of messages between machines.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **Encryption** (6.5): Turning plaintext into ciphertext using a key, so only key holders can read it.
- **URL** (6.6): Uniform resource locator: a scheme, host, optional port, path, query and fragment naming a resource.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **SQL Server** (7.2): Microsoft's relational database product.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, collection expressions, LINQ. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. How many bytes does "é" take in UTF-8?**

> Usually 2, as the single code point U+00E9. It can also be written as a plain "e" plus a combining accent, which takes 3 bytes and looks identical.

**2. What does string.Length count in C#?**

> UTF-16 code units, not characters. An emoji like 👍 has a length of 2. A family emoji can have a length of 8 and still look like one symbol.

**3. Why can "file".ToUpper() return "FİLE"?**

> In Turkish, uppercase "i" is "İ" with a dot. `ToUpper()` uses the current culture. For identifiers, keys and protocol text, use `ToUpperInvariant()` or ordinal comparisons.

**4. You read a file and see "cafÃ©" where it should say "café". What happened?**

> The file was written as UTF-8 and read with a single-byte encoding such as Latin-1. The two bytes of "é" were shown as two separate characters. This is called mojibake.

**5. Why is building a long string with += in a loop slow?**

> C# strings can't be changed after they are created. Each `+=` makes a new string and copies everything so far. The total work grows with the square of the length. `StringBuilder` avoids the copying.

**6. Does \d in a .NET regular expression match only 0 to 9?**

> No. It matches any Unicode decimal digit, including Arabic-Indic and Devanagari digits. Use `[0-9]` when you mean ASCII digits.

**7. How much bigger does Base64 make binary data?**

> About a third. Every 3 bytes become 4 characters. Hex doubles the size, because every byte becomes 2 characters.

### Characters are numbers

The machine only stores numbers. To store text, we agree on a table that gives every character a number. A table like this plus a rule for turning the numbers into bytes is called an **encoding**.

### ASCII and the code-page mess

The first widely used table was **ASCII**, from the 1960s. It uses 7 bits, so it has 128 slots. That covers English letters, digits, punctuation and some control codes like newline. "A" is 65. "a" is 97. "0" is 48.

Other languages needed more characters. So vendors used the eighth bit to add 128 more. Each vendor and region filled those slots differently. These variants were called **code pages**. Windows-1252 was common in Western Europe. Other code pages covered Cyrillic, Greek and so on.

The same byte meant different characters on different machines. Text moved between systems came out garbled. Some older databases and file formats still carry this problem today.

### Control characters and escapes

The first 32 ASCII codes are not printable. They are **control characters**, and they tell a device what to do. A few still matter every day.

Line feed (LF, code 10) ends a line on Linux and macOS. Carriage return (CR, code 13) comes before it on Windows. Tab is code 9. The null character, code 0, marks the end of a string in C.

Source code writes these with **escape sequences**: a backslash followed by a letter. `\n` is a line feed, `\r` a carriage return, `\t` a tab and `\0` the null character. `\u00E9` writes any character by its code number. In C#, a verbatim string `@"C:\temp"` or a raw string literal `"""..."""` turns escapes off.

C strings end at the first null character. C# strings store their length separately and can contain nulls. Passing a C# string with an embedded null to a native library can cut it short.

### Unicode

**Unicode** fixed this with one table for every writing system. Each character gets a number called a **code point**. Code points are written as U+ followed by hex. "A" is U+0041. "é" is U+00E9. "👍" is U+1F44D.

The range runs from U+0000 to U+10FFFF. That is over a million slots. Around 150,000 are assigned today, including emoji.

Unicode is only the table. It does not say how to store code points as bytes. That is the job of an encoding.

### UTF-8, UTF-16 and UTF-32

Three encodings matter.

**UTF-8** uses 1 to 4 bytes per code point. Plain ASCII stays exactly 1 byte, so old ASCII text is already valid UTF-8. It is the default for the web, JSON, Linux, source files and most modern APIs.

**UTF-16** uses 2 bytes for most code points and 4 bytes for the rest. Each 2-byte piece is a **code unit**. Code points above U+FFFF, like most emoji, need two code units called a **surrogate pair**. C# strings, Java strings, JavaScript strings and the Windows API all use UTF-16 internally.

**UTF-32** uses 4 bytes for everything. It is simple but wasteful, and rarely used for storage.

| Code points | Examples | UTF-8 bytes | UTF-16 bytes |
|---|---|---:|---:|
| U+0000 to U+007F | A, 7, ? | 1 | 2 |
| U+0080 to U+07FF | é, ß, Ж | 2 | 2 |
| U+0800 to U+FFFF | €, 中, あ | 3 | 2 |
| U+10000 to U+10FFFF | 👍, 𝄞 | 4 | 4 |

### What counts as one character

"Character" means three different things. It helps to name them.

A **code unit** is one piece of an encoding: one byte in UTF-8 or two bytes in UTF-16. C#'s `string.Length` counts UTF-16 code units.

A **code point** is one entry in the Unicode table. C# calls it a `Rune`.

A **grapheme** is what a person sees as one character. It can be several code points. "é" can be one code point, or "e" followed by a combining accent. The family emoji 👨‍👩‍👧 is five code points: three people joined by two invisible "zero width joiner" characters. C# calls a grapheme a **text element**.

```csharp
// text.cs — run with: dotnet run text.cs
using System.Globalization;
using System.Linq;
using System.Text;

Show("héllo 👍");   // units 8, bytes 11, points 7, seen 7
Show("e\u0301");    // units 2, bytes 3,  points 2, seen 1  (e + combining accent)
Show("👨‍👩‍👧");       // units 8, bytes 18, points 5, seen 1

static void Show(string s) => Console.WriteLine(
    $"UTF-16 units {s.Length}, UTF-8 bytes {Encoding.UTF8.GetByteCount(s)}, " +
    $"code points {s.EnumerateRunes().Count()}, seen {new StringInfo(s).LengthInTextElements}");

// Same look, different code points
string composed = "\u00E9";      // é as one code point
string decomposed = "e\u0301";   // e plus a combining accent
Console.WriteLine(composed == decomposed);                         // False
Console.WriteLine(composed.Normalize() == decomposed.Normalize()); // True

// Wrong decoder, garbled text
byte[] utf8 = Encoding.UTF8.GetBytes("café");
Console.WriteLine(Convert.ToHexString(utf8));            // 636166C3A9
Console.WriteLine(Encoding.Latin1.GetString(utf8));      // cafÃ©

// Culture-sensitive vs ordinal
Console.WriteLine(string.Equals("FILE", "file", StringComparison.OrdinalIgnoreCase)); // True
```

### Strings in .NET

A C# `string` is **immutable**. Once created, it never changes. Methods like `Replace` and `ToUpper` return a new string and leave the old one alone.

This makes strings safe to share between threads and to use as dictionary keys. It also means that building a string piece by piece creates many throwaway copies. `StringBuilder` keeps a growable buffer instead, and makes one string at the end.

Modern .NET can also look at part of a string without copying it. A `ReadOnlySpan<char>` is a view onto existing characters. Slicing a span costs nothing. Parsing code in .NET itself uses spans heavily to avoid allocations.

Because most data arrives as UTF-8, C# 11 added `u8` string literals. `"OK"u8` is a `ReadOnlySpan<byte>` holding the UTF-8 bytes, built at compile time.

```csharp
// strings.cs — run with: dotnet run strings.cs
using System.Diagnostics;
using System.Text;

var sw = Stopwatch.StartNew();
string s = "";
for (int i = 0; i < 20_000; i++) s += "x";      // copies the whole string every time
Console.WriteLine($"+= in a loop:  {sw.ElapsedMilliseconds} ms");

sw.Restart();
var sb = new StringBuilder();
for (int i = 0; i < 20_000; i++) sb.Append('x');  // appends into one buffer
string built = sb.ToString();
Console.WriteLine($"StringBuilder: {sw.ElapsedMilliseconds} ms");
Console.WriteLine(s == built);                    // True

ReadOnlySpan<char> line = "2026-09-28,LHR,JFK";
ReadOnlySpan<char> date = line[..line.IndexOf(',')];  // a view, no new string
Console.WriteLine(date.ToString());               // 2026-09-28

ReadOnlySpan<byte> ok = "OK"u8;                   // UTF-8 bytes, fixed at compile time
Console.WriteLine(ok.Length);                     // 2
```

### Normalization

Because some characters can be written more than one way, two strings can look identical and still differ. **Normalization** rewrites a string into one standard form. The common form is NFC, which combines characters where possible. `string.Normalize()` uses NFC by default.

Normalize text that people type before you compare or store it as a key. User names and search terms are typical cases.

### Comparing and sorting

C# has two kinds of string comparison.

**Ordinal** comparison compares the raw code units. It is fast and predictable. Use it for identifiers, keys, file names, URLs, headers and anything a program reads.

**Culture-sensitive** comparison follows the rules of a human language. It is right for sorting names shown to a person. It is wrong for everything else, because results change with the machine's language settings.

Always pass a `StringComparison` explicitly. The .NET code analyzers can warn you when you forget.

### Case, sorting and regular expressions

Changing case is language-specific, and it is not always one character to one character. German "ß" uppercases to "SS" in most languages' rules. .NET only does one-to-one case changes, so `"Straße".ToUpperInvariant()` keeps the "ß" and the length stays the same. Python's `"Straße".upper()` gives "STRASSE". Two systems can disagree about the "same" operation.

Sorting has the same issue. Ordinal order follows code numbers, so every uppercase ASCII letter comes before every lowercase one. Culture order follows a language's rules, and different languages order letters like "ä" differently. Neither sorts numbers inside text the way people expect: "file10" comes before "file2". .NET 10 added `CompareOptions.NumericOrdering` for that.

Regular expressions have a Unicode trap too. In .NET, `\d` matches any decimal digit in Unicode, not just 0 to 9. A value can pass a `\d+` check and then fail `int.Parse`, which only accepts ASCII digits.

```csharp
// compare.cs — run with: dotnet run compare.cs
using System.Globalization;
using System.Text.RegularExpressions;

string[] names = { "apple", "Zebra", "banana" };
Array.Sort(names, StringComparer.Ordinal);
Console.WriteLine(string.Join(", ", names));   // Zebra, apple, banana: 'Z' is 90, 'a' is 97
Array.Sort(names, StringComparer.InvariantCulture);
Console.WriteLine(string.Join(", ", names));   // apple, banana, Zebra (ordinal again in invariant globalization mode)

string[] files = { "file10", "file2", "file1" };
Array.Sort(files, StringComparer.Ordinal);
Console.WriteLine(string.Join(", ", files));   // file1, file10, file2
Array.Sort(files, StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering));
Console.WriteLine(string.Join(", ", files));   // file1, file2, file10  (.NET 10)

Console.WriteLine("Straße".ToUpperInvariant());           // STRAßE: one-to-one only
Console.WriteLine(Regex.IsMatch("١٢٣", @"^\d+$"));        // True: Arabic-Indic digits
Console.WriteLine(Regex.IsMatch("١٢٣", @"^[0-9]+$"));     // False
Console.WriteLine(int.TryParse("١٢٣", out _));             // False
```

### Text in files and databases

A text file is just bytes. Nothing inside it says which encoding was used. Some UTF-8 files start with three marker bytes, `EF BB BF`, called a **byte order mark** or BOM. Many tools add or expect it. Others choke on it.

In SQL Server, `NVARCHAR` stores UTF-16 and holds any Unicode text. `VARCHAR` stores bytes in the column's code page, and characters outside it become "?". Since SQL Server 2019, a `VARCHAR` column with a UTF-8 collation (one ending in `_UTF8`) can store Unicode as UTF-8. PostgreSQL databases are usually UTF-8 throughout.

### Turning bytes into text

Some channels only carry text: JSON, XML, URLs, email and HTTP headers. To send raw bytes through them, you encode the bytes as text.

**Hex** writes each byte as two characters. It is easy to read and doubles the size.

**Base64** uses 64 safe characters: A–Z, a–z, 0–9, `+` and `/`. Each character carries 6 bits, so 3 bytes become 4 characters, and the size grows by a third. `=` signs pad the end to a multiple of 4. **Base64Url** swaps `+` and `/` for `-` and `_`, which are safe in URLs and file names. JSON Web Tokens use it.

These are encodings, not encryption. Anyone can decode them.

### Escaping text for where it goes

Text often ends up inside another format: a URL, an HTML page, a JSON document or a SQL query. Each format gives some characters a special meaning. **Escaping** rewrites those characters so they are read as plain text.

Each target needs its own escaping. URLs use percent-encoding, where each byte becomes `%` and two hex digits. HTML uses entities like `&lt;`. JSON uses backslash escapes. SQL should not be escaped at all; use parameters instead.

Forgetting to escape, or using the wrong kind, is the root of injection attacks. Those are covered in Common web vulnerabilities (10.4).

```csharp
// escape.cs — run with: dotnet run escape.cs
using System.Buffers.Text;
using System.Net;
using System.Text.Json;

byte[] data = { 0x4E, 0x61, 0x6D, 0xFF, 0xFE };
Console.WriteLine(Convert.ToHexString(data));        // 4E616DFFFE
Console.WriteLine(Convert.ToBase64String(data));     // TmFt//4=
Console.WriteLine(Base64Url.EncodeToString(data));   // TmFt__4  (.NET 9; no padding)

Console.WriteLine(Uri.EscapeDataString("a b&c=é"));        // a%20b%26c%3D%C3%A9
Console.WriteLine(WebUtility.HtmlEncode("<b>\"hi\"</b>"));  // &lt;b&gt;&quot;hi&quot;&lt;/b&gt;
Console.WriteLine(JsonSerializer.Serialize("line1\nsaid \"hi\""));  // "line1\nsaid \u0022hi\u0022"
```

### Text that looks the same but isn't

Unicode contains characters that look alike. Cyrillic "а" looks exactly like Latin "a". A user name like "аdmin" with a Cyrillic first letter is a different string. These lookalikes are called **homoglyphs**, and attackers use them for fake user names and web addresses.

Some characters are invisible. A zero-width space, U+200B, takes no room on screen. Direction-control characters can make text display in a different order than it is stored. In 2021, researchers showed that these could hide malicious logic in source code that looked harmless in review.

For identifiers such as user names, normalize the text and allow only an expected set of characters.

### In short

Unicode gives every character a number called a code point, and encodings such as UTF-8 and UTF-16 turn those numbers into bytes. C# strings are UTF-16, so `string.Length` counts two-byte units, not visible characters. Always know which encoding a file or request uses, or text turns into garbage. Compare identifiers with ordinal comparisons, and use culture-aware ones only for text shown to people. Escape text for exactly the place it is going, such as a URL, HTML or JSON, and use parameters for SQL.

### What goes wrong

**Mojibake.** Text written in one encoding and read in another comes out as "cafÃ©" or "â€™". Always state the encoding when reading and writing files and HTTP bodies. The HTTP header `Content-Type: text/plain; charset=utf-8` does this for web traffic.

**Cutting text in the wrong place.** `Substring`, truncation to a column length, and reversing a string by `char` can split a surrogate pair or grapheme. The result shows broken symbols. Use `StringInfo` to cut on grapheme boundaries.

**Length limits that don't match what people see.** A "50 character" limit in the UI, the database and the API can mean graphemes, UTF-16 units and bytes respectively. Decide which one you mean and use it everywhere.

**Culture-sensitive comparisons on keys.** Code that works on your machine fails for a user with a Turkish or Lithuanian culture. Use ordinal comparisons for anything that is not shown to a person.

**Missing culture data in containers.** Many slim container images run .NET in invariant globalization mode. There, culture-specific formatting and sorting are missing, and creating a culture such as `tr-TR` can throw.

**Unicode in the wrong column type.** Names saved to a `VARCHAR` column with a Western code page lose their non-Latin characters, which become "?". The data is gone, not just hidden.

**`+=` in a loop.** A report builder that concatenates thousands of rows slows to a crawl. Use `StringBuilder`, `string.Join` or `string.Create`.

**Checking with `\d`, then parsing.** Input passes validation and then throws, or gets stored in a form other systems can't read. Use `[0-9]`.

**Invariant globalization changes sorting.** In invariant mode, culture comparisons become ordinal. Lists shown to users suddenly sort uppercase first. Test sorting in the same container image you ship.

**Escaping for the wrong target.** HTML-encoding a value that goes into a URL, or into JavaScript, still leaves it open to injection. Escape for exactly the place the text lands.

**Lookalike identifiers.** Two accounts appear to have the same name. Restrict and normalize characters in user names and similar keys.

### Cost model

UTF-8 is compact for mostly-ASCII text, which covers code, JSON, HTML and most Western languages. UTF-16 is more compact for Chinese, Japanese and Korean, at 2 bytes instead of 3.

Converting between encodings costs one pass over the data. .NET strings are UTF-16, but almost everything arriving over a network is UTF-8. So every request body gets converted. That is why modern .NET has APIs that work on UTF-8 bytes directly, like `"text"u8` literals and `Utf8JsonReader`. Culture-sensitive comparison is also much slower than ordinal comparison.

Concatenating in a loop costs time proportional to the square of the final length. `StringBuilder` is roughly proportional to the length. Slicing a span costs nothing, while `Substring` allocates a new string. Base64 adds 33% to size and hex adds 100%. Regular expressions can be slow. Some patterns take exponentially long on unlucky input, because the engine tries every way of matching. Set a timeout, or use `RegexOptions.NonBacktracking`, available since .NET 7. The `[GeneratedRegex]` attribute compiles a pattern into C# at build time, which is fastest.

### In other languages (optional)

**JavaScript and TypeScript.** Strings are UTF-16, like C#. `"👍".length` is 2. `[..."👍"].length` counts code points and gives 1. `Intl.Segmenter` splits text into graphemes. `TextEncoder` turns a string into UTF-8 bytes. `localeCompare` sorts by culture, and `normalize()` normalizes.

**Python.** A `str` is a sequence of code points, so `len("👍")` is 1. Bytes and text are separate types: `.encode("utf-8")` and `.decode("utf-8")` convert between them. Always pass `encoding="utf-8"` to `open()`, because the default has depended on the platform.

**SQL Server.** `LEN` counts characters and ignores trailing spaces. `DATALENGTH` counts bytes. Unicode literals need an `N` prefix, as in `N'café'`, or they are converted to the database code page first. Case sensitivity comes from the collation: `CI` in its name means case-insensitive.

**PostgreSQL.** Text is usually UTF-8. Comparisons are case-sensitive by default. `ILIKE` and the `citext` type give case-insensitive matching.

### Key terms

- **Encoding**: A table of characters plus a rule for turning them into bytes.
- **ASCII**: A 7-bit character table from the 1960s with 128 characters, covering English text.
- **Code page**: One of many old 8-bit extensions of ASCII. Each filled the extra 128 slots differently.
- **Unicode**: One character table for every writing system, with over a million slots.
- **Code point**: A character's number in the Unicode table, such as U+00E9 for é. C# calls it a Rune.
- **Code unit**: One piece of an encoding: one byte in UTF-8, two bytes in UTF-16.
- **Surrogate pair**: Two UTF-16 code units that together hold one code point above U+FFFF.
- **Grapheme**: What a person sees as one character. It can be several code points. C# calls it a text element.
- **UTF-8**: The dominant encoding. Uses 1 to 4 bytes per code point and matches ASCII for English text.
- **Normalization**: Rewriting a string into one standard form, so that identical-looking text compares equal.
- **Ordinal comparison**: Comparing strings by their raw code units. Fast and the same on every machine.
- **Immutable**: Unable to change after creation. C# strings are immutable.
- **Base64**: A way to write bytes as text using 64 safe characters. Every 3 bytes become 4 characters.
- **Escaping**: Rewriting special characters so a target format reads them as plain text.
- **Mojibake**: Garbled text caused by decoding bytes with the wrong encoding.

### Where this leads

Strings as a language type come back in Types (2.3). Encodings on the wire appear in HTTP (6.6). Column types and collations are part of The relational model and SQL (7.2). JavaScript strings, which are also UTF-16, are covered in Modern JavaScript (9.2).

### Check yourself

1. Print the UTF-8 bytes of your full name in hex with `Convert.ToHexString`. Count the bytes and explain the count.
2. Write `Truncate(string s, int maxSeen)` that never breaks a visible character. Test it with the family emoji.
3. Find the collation of a SQL Server database you work with (`SELECT DATABASEPROPERTYEX(DB_NAME(), 'Collation')`). Check whether its text columns are `VARCHAR` or `NVARCHAR`.
4. Write `IsAsciiDigits(string s)` without regular expressions. Compare its speed with `Regex.IsMatch(s, "^[0-9]+$")` on a million calls.
5. Base64-encode a 1,000-byte array and check that the result is 1,336 characters long. Explain the number.
6. Build a user-name check that normalizes to NFC and allows only ASCII letters, digits, dot and underscore. Test it with "аdmin" written with a Cyrillic first letter.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: "LHR" not found because someone typed "lhr"

An airport lookup dictionary misses lowercase codes, and on a Turkish-locale server "ist".ToUpper() becomes "İST".

*The fix*

```csharp
var airports = new Dictionary<string, Airport>(StringComparer.OrdinalIgnoreCase)
{
    ["LHR"] = new("London Heathrow"),
    ["IST"] = new("Istanbul"),
};
airports.TryGetValue("ist", out var a);          // found, on any server locale

// For normalizing codes, use the invariant culture:
var code = input.Trim().ToUpperInvariant();
```

> **Lesson:** Identifiers and keys use ordinal comparison; culture-aware comparison is only for text shown to people.

#### Example 2: A name field that cut an emoji in half

Passenger display names are truncated to 20 chars for boarding passes. Some passes print a broken character at the end.

**What goes wrong.** `Substring(0, 20)` counts UTF-16 units and can split a surrogate pair or a combined character.

*Truncate by text elements*

```csharp
using System.Globalization;

static string Truncate(string s, int maxChars)
{
    var e = StringInfo.GetTextElementEnumerator(s);
    var sb = new StringBuilder();
    int n = 0;
    while (n < maxChars && e.MoveNext()) { sb.Append(e.GetTextElement()); n++; }
    return sb.ToString();
}
```

> **Lesson:** string.Length is not "characters a person sees". Truncate by text elements, and limit storage by bytes when the database column is byte-sized.

### Time, Culture & Text

<p class="src">From <em>.NET Backend Deep Dive</em> · Time, Culture & Text</p>

<a id="dn-141"></a>

#### Culture bugs

```csharp
// On a Vietnamese/German machine this produces "1,5" - and breaks JSON, SQL and CSV
var s = 1.5m.ToString();

// Always specify culture for machine-readable output
var s = 1.5m.ToString(CultureInfo.InvariantCulture);
decimal.Parse(input, CultureInfo.InvariantCulture);
```

The classic failure: works on the developer's machine, breaks in a container with a different locale, or vice versa. Since .NET 5 Linux containers default to **invariant globalization** unless ICU is installed, which flips the symptom around and makes it worse to debug.

**String comparison is the other half:**

```csharp
// Culture-sensitive - SLOW and locale-dependent. In Turkish, "I".ToLower() is not "i".
if (name.ToLower() == "admin")            // the classic Turkish-I security bug

// Correct for identifiers, keys, protocol values, file paths
if (name.Equals("admin", StringComparison.OrdinalIgnoreCase))

// Culture-sensitive comparison is right ONLY for sorting text shown to users
list.Sort((a, b) => string.Compare(a, b, StringComparison.CurrentCulture));
```

Rule of thumb: **`Ordinal` for machines, `CurrentCulture` for humans.** Enable the `CA1307`/`CA1309` analyzers so it's enforced rather than remembered.

<a id="dn-142"></a>

#### Regex and ReDoS

```csharp
// Catastrophic backtracking: nested quantifiers on overlapping alternatives.
// A crafted 30-character input can hang a core for minutes.
var bad = new Regex(@"^(a+)+$");

// Defenses
var ok = new Regex(@"^\w+@\w+\.\w+$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

// Compile-time generated, no startup cost, analyzer-checked (.NET 7+)
[GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
private static partial Regex DateFormat();
```

Always set a **timeout** on any regex applied to user input, and never build a regex from user input. Static/compiled instances only — constructing a `Regex` per call is a well-known performance trap.

<a id="dn-143"></a>

#### Strings and allocation

```csharp
s += x                 // in a loop: O(n²) allocation. Use StringBuilder.
s.Substring(0, 10)     // allocates. s.AsSpan(0, 10) does not.
string.Join(",", items)      // one pass, one allocation
string.Create(...)           // for hot paths where you know the final length
```

Also remember strings are **immutable and interned for literals**, so a secret held in a `string` can linger in memory until collected — `SecureString` is deprecated and not a real fix; the practical answer is to minimize the lifetime and never log it.

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a29"></a>

#### Strings, encoding and text performance

Strings are immutable UTF-16 reference types. Every concatenation allocates a new one, which is fine once and catastrophic in a loop over a million rows.

``` csharp
// interpolation compiles to a DefaultInterpolatedStringHandler - fast, low allocation
var s = $"Order {id} total {total:C}";

// building in a loop: StringBuilder, sized up front if you can guess
var sb = new StringBuilder(capacity: 4096);
foreach (var r in rows) sb.Append(r.Name).Append(',').Append(r.Value).AppendLine();

// comparison: be explicit, always
name.Equals(other, StringComparison.OrdinalIgnoreCase);   // identifiers, keys, paths
string.Compare(a, b, StringComparison.CurrentCulture);    // sorting for a human

// searching without allocating substrings
ReadOnlySpan<char> span = line.AsSpan();
var idx = span.IndexOf(',');
```

| Comparison          | Use for                                                             |
|---------------------|---------------------------------------------------------------------|
| `Ordinal`           | IDs, keys, file paths, protocol tokens — fastest and safest default |
| `OrdinalIgnoreCase` | The same, case-insensitively                                        |
| `InvariantCulture`  | Persisted or transmitted values that must not vary by machine       |
| `CurrentCulture`    | Text displayed to and sorted for a user                             |

> **\[Trap\]**
>
> **Common trap.** `ToLower()` for comparison allocates a whole new string and is culture-dependent — the Turkish dotless i famously breaks it. Use `StringComparison.OrdinalIgnoreCase` instead.

###### Where string allocations come from, and how to avoid them

``` text
  a .NET string is UTF-16, immutable, and heap-allocated.

    "hello"     ->  object header (16 bytes) + length (4) + 5 chars x 2 bytes
                    = ~32 bytes, rounded up

  EVERY operation that "changes" a string allocates a new one:
    s.ToUpper()      s.Trim()      s.Substring()     s.Replace()
    s + t            $"{a}{b}"     s.Split()

  in a loop, this is where the garbage comes from.
```

``` csharp
// the classic, and its cost
var s = "";
foreach (var item in items) s += item + ",";     // N allocations, O(N^2) copying

var sb = new StringBuilder();
foreach (var item in items) sb.Append(item).Append(',');   // amortised, one buffer
var s = sb.ToString();                                     // one final allocation

// better still when the shape is known:
var s = string.Join(",", items);                 // single pass, single allocation

// ZERO-allocation slicing: Span does not copy
ReadOnlySpan<char> line = "2026-08-07,4471,99.50";
var date  = line[..10];                          // no allocation
var idEnd = line[11..].IndexOf(',') + 11;
var id    = line[11..idEnd];
int.TryParse(id, out var orderId);               // parses from the span directly

// and string.Create when you know the length and want to fill it once
var masked = string.Create(card.Length, card, static (span, value) =>
{
    value.AsSpan().CopyTo(span);
    span[..^4].Fill('*');                        // one allocation, total
});
```

| Comparison                 | Use                                            | Why                                                             |
|----------------------------|------------------------------------------------|-----------------------------------------------------------------|
| `StringComparison.Ordinal` | **Identifiers, keys, protocol tokens, paths**  | Byte comparison. Fast, and culture-independent                  |
| `OrdinalIgnoreCase`        | Case-insensitive identifiers, headers          | Fast, predictable                                               |
| `CurrentCulture`           | Text shown to a user, sorting a displayed list | Respects local collation rules                                  |
| `InvariantCulture`         | Persisted or transmitted values                | Stable across machines and locales                              |
| `==` on strings            | Equality only                                  | Already ordinal; the ambiguity is in `Compare` and `StartsWith` |

> **\[Trap\]**
>
> **Common trap.** `string.ToLower()` for case-insensitive comparison allocates a whole new string and is culture-dependent — it is wrong twice. Use `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)`. The culture point is not theoretical: in Turkish, `"I".ToLower()` is `"ı"`, not `"i"`, so an identifier comparison that works everywhere else fails in `tr-TR`.

<a id="be-a34"></a>

#### Globalization, culture and the bugs it causes

Culture affects number parsing, date formatting, string comparison and casing. The classic failure is code that works everywhere in the office and breaks on a machine set to a locale where the decimal separator is a comma.

``` csharp
// wrong: culture-dependent, produces "1,5" in NL and breaks the API contract
var body = "{ \"amount\": " + amount + " }";

// right: invariant for machine-readable output
var body2 = "{ \"amount\": " + amount.ToString(CultureInfo.InvariantCulture) + " }";

// parsing untrusted input
if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
    return Results.BadRequest("Not a number.");
```

- **Invariant culture** for anything persisted, transmitted or compared. **Current culture** only for display.
- Set `InvariantGlobalization=true` in containers only if you truly do not need culture data — it removes ICU and shrinks the image, but breaks culture-aware comparison.
- A slim Linux image without `libicu` throws at startup unless invariant mode is on. This is a very common first Docker error.
- Sorting is culture-dependent: the order of a list can differ between your machine and the server unless you specify a comparer.

###### The bugs, and the invariant-mode trap in containers

``` csharp
// the classic: a number formatted on one machine, parsed on another
var s = 1234.56m.ToString();            // "1234.56" in en-US, "1234,56" in nl-NL
decimal.Parse(s);                       // works locally, throws on the other server

// and the silent version, which is worse:
decimal.Parse("1.234");                 // en-US -> 1.234
                                        // de-DE -> 1234      <- no exception

// the rule: CultureInfo.InvariantCulture for anything MACHINE-READABLE.
var s = value.ToString(CultureInfo.InvariantCulture);
decimal.Parse(s, CultureInfo.InvariantCulture);
DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

// better: use round-trip formats that are culture-independent by construction
value.ToString("R");                    // round-trippable numeric
date.ToString("O");                     // ISO 8601: 2026-08-07T10:22:31.0000000Z
```

| Bug                                    | Cause                                         | Fix                                           |
|----------------------------------------|-----------------------------------------------|-----------------------------------------------|
| Parses locally, throws in production   | Server culture differs from the developer's   | Invariant culture for machine data            |
| Number parsed wrongly with no error    | `.` as a thousands separator in some cultures | Invariant culture; never trust a silent parse |
| Sort order differs by machine          | Culture-sensitive comparison                  | `StringComparison.Ordinal` for keys           |
| `"I".ToLower() != "i"`                 | The Turkish dotless ı                         | `ToLowerInvariant`, or ordinal comparison     |
| Dates off by a day                     | `DD/MM` vs `MM/DD`                            | ISO 8601 (`"O"`) everywhere internally        |
| Works in tests, fails on a build agent | The agent's locale differs                    | Set the culture explicitly in test setup      |

``` text
  THE CONTAINER TRAP

  minimal base images (alpine, chiseled, distroless) often ship WITHOUT ICU -
  the library that provides culture data. .NET then falls back to
  GLOBALIZATION-INVARIANT MODE.

  what breaks, silently:
    every culture becomes InvariantCulture
    culture-aware string comparison degrades to ordinal
    CultureInfo("nl-NL") may throw, or return invariant
    time zone lookups by IANA id can fail
    currency and date formatting is wrong for every locale

  and it is often ENABLED DELIBERATELY to shrink the image:
    <InvariantGlobalization>true</InvariantGlobalization>

  that is a fine choice for a machine-to-machine API that formats nothing
  for humans. It is a bug for anything that displays localised text.
  Decide it consciously - the failure mode is wrong output, not an exception.
```

``` text
# check what mode you are actually running in
Console.WriteLine(CultureInfo.CurrentCulture.Name);          // "" means invariant
Console.WriteLine(CultureInfo.GetCultures(CultureTypes.AllCultures).Length);  // 1 = invariant

# if you need full globalization on a slim image, add ICU explicitly
RUN apt-get update && apt-get install -y --no-install-recommends libicu72
```

<a id="be-a35"></a>

#### Localization: resources, and what must never be translated

Culture handling is about parsing and formatting correctly. Localization is the separate question of which *text* a user sees — and the discipline that matters is knowing which strings are for humans and which are contracts.

``` csharp
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

var supported = new[] { "en", "ja", "vi" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supported)      // formatting: dates, numbers
    .AddSupportedUICultures(supported));  // resource lookup: which .resx
// providers run in order: ?culture=ja  →  cookie  →  Accept-Language

public class OrderService(IStringLocalizer<OrderService> t)
{
    public string Confirmation(int n) => t["OrderConfirmed", n];
    // Resources/OrderService.ja.resx → "ご注文 {0} 件を承りました"
    // missing key? IStringLocalizer returns the KEY itself, no exception —
    // so a typo ships silently. Assert on t[key].ResourceNotFound in tests.
}
```

| Localise                           | Never localise                    |
|------------------------------------|-----------------------------------|
| Validation messages shown to users | Error *codes* in the API response |
| Email and notification bodies      | Log messages (you grep these)     |
| UI labels and help text            | Enum values and JSON field names  |
| Currency and date *presentation*   | Stored values and identifiers     |

``` csharp
// The pattern that keeps both honest: a stable code plus a localised message
return TypedResults.Problem(new ProblemDetails
{
    Status = 422,
    Title  = t["InsufficientFunds"],           // for the human
    Extensions = { ["code"] = "INSUFFICIENT_FUNDS" }   // for the client
});
// the client branches on code; the user reads Title. Translating the code
// would break every consumer the moment someone changes their locale.
```

> **\[Trap\]**
>
> **Common trap.** Pluralisation is not string interpolation. “1 item / 2 items” works in English and fails in languages with three or six plural forms; concatenating fragments (`"You have " + n + " items"`) also breaks word order in languages that put the verb last. Keep whole sentences as single resource entries with positional placeholders, and use a plural-aware format where the language needs it.

> **\[Why it matters\]** Backend localisation is mostly invisible until a partner in another market sends a bug report you cannot reproduce — or until someone’s log query stops matching because messages arrive in Japanese. Deciding up front which strings are contracts and which are copy costs nothing; retrofitting the distinction after clients depend on translated text is a breaking change.

### Primitives

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Primitives</p>

<a id="be-b263"></a>

#### char, Rune, and text

`char` is a **UTF-16 code unit**, not a character. This matters more than it sounds:

``` text
"👍".Length          // 2 — one emoji, two chars (a surrogate pair)
"é".Length           // 1 or 2, depending on composition form
"👨‍👩‍👧".Length      // 8 — a grapheme cluster of several code points
```

- **`char`** — one UTF-16 code unit
- **`Rune`** (.NET Core 3+) — one Unicode code point, handles surrogate pairs
- **Grapheme cluster** — what a human calls a character; iterate with `StringInfo.GetTextElementEnumerator`

Reversing a string by reversing its `char`s corrupts any non-BMP character. Truncating a string to N chars can split a surrogate pair and produce invalid text.

### Strings

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Strings</p>

<a id="be-b267"></a>

#### Immutability and interning

`string` is a reference type that is immutable. Every operation returns a new string.

``` csharp
var s = "a";
for (int i = 0; i < 10_000; i++) s += "x";   // 10,000 allocations, ~50 MB copied
```

That loop is O(n²) in bytes copied. `StringBuilder` makes it O(n).

**Interning** is the runtime keeping a table of unique string instances so identical text shares one object.

All string **literals** are interned automatically at JIT time, so every `"error"` anywhere in your program is the same instance:

``` csharp
ReferenceEquals("ab", "ab");                       // true — same interned instance
ReferenceEquals("a" + "b", "ab");                  // true — constant-folded at compile time
var a = "a"; ReferenceEquals(a + "b", "ab");       // FALSE — computed at runtime
```

Runtime-computed strings are not interned unless you call `string.Intern` explicitly, and you almost never should: **the intern table is rooted for the process lifetime**, so interned strings can never be collected. Interning user input is an unbounded memory leak that no GC will clean up.

This is also why you must never compare strings with `ReferenceEquals` or `(object)a == (object)b` — whether two equal strings are the same instance depends on whether they were literals, which is not a property your logic should depend on. Use `==` (which is ordinal value comparison for `string`) or an explicit `StringComparer`.

<a id="be-b268"></a>

#### When StringBuilder

- **Concatenating in a loop** → `StringBuilder`, always.
- **A fixed number of parts** → `+` or interpolation. The compiler turns `a + b + c` into a single `string.Concat` call, which is faster than a builder for a handful of pieces.
- **Formatting once** → interpolation. Since C# 10, interpolated strings use `DefaultInterpolatedStringHandler`, which builds into pooled memory and avoids the old boxing of value-type arguments.

Pre-size when you can: `new StringBuilder(capacity)` avoids intermediate growth.

<a id="be-b269"></a>

#### Comparison — the bug that ships

``` text
"Straße".ToUpper()                              // "STRASSE" in some cultures
"i".ToUpper(new CultureInfo("tr-TR"))           // "İ" — Turkish dotted capital
"file".Equals("FILE", StringComparison.CurrentCultureIgnoreCase)  // culture-dependent!
```

The classic failure: a security or protocol check written with culture-sensitive comparison, deployed to a Turkish locale, and the behaviour changes. This is a real, recurring incident class.

**Rules:**

- **`StringComparison.Ordinal`** for identifiers, keys, protocol tokens, file paths, anything machine-facing. It's also the fastest — a byte comparison.
- **`OrdinalIgnoreCase`** for case-insensitive machine comparison.
- **`CurrentCulture`** only for sorting or comparing text *shown to a user*.
- **Always pass a `StringComparison` explicitly.** `s1 == s2` is ordinal, but `string.Compare` and `IndexOf` default to culture-sensitive — an inconsistency that catches everyone.

``` csharp
// dictionaries keyed by machine-facing strings
new Dictionary<string, T>(StringComparer.Ordinal);
new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
```

Specifying `StringComparer.Ordinal` explicitly is also measurably faster than the default for string keys.

<a id="be-b270"></a>

#### Span-based string work

``` csharp
ReadOnlySpan<char> span = "key=value".AsSpan();
int i = span.IndexOf('=');
var key = span[..i];              // no allocation
var value = span[(i + 1)..];      // no allocation

// parsing without allocating
int.TryParse(value, out var n);   // span overloads exist throughout the BCL
```

Slicing a span is free — it's a pointer and a length. `Substring` allocates a new string every time. In parsing hot paths this is often a 5–10× difference.

`string.Create` builds a string with a callback that writes directly into the final buffer, avoiding an intermediate copy:

``` csharp
string s = string.Create(len, state, (span, st) => { /* fill span */ });
```

<a id="be-b271"></a>

#### Parsing and formatting

Always use `TryParse` at boundaries, and always pass a culture:

``` csharp
decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var d);
```

Parsing `"1,234.56"` with a German culture gives you a different number than with an invariant one, and the failure is silent. **Invariant culture for machine data; current culture only for user-entered text.**

`IParsable<T>` and `ISpanParsable<T>` (.NET 7) let generic code parse any type, which is how model binders and serializers stay allocation-light.

------------------------------------------------------------------------

### The type system in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The type system in practice</p>

<a id="be-m48"></a>

#### char, Rune, and Unicode

A `char` is a 16-bit **UTF-16 code unit**, not a character. Characters outside the Basic Multilingual Plane (emoji, some CJK extensions, historic scripts) are stored as a *surrogate pair* of two `char`s.

``` csharp
string s = "😀";
Console.WriteLine(s.Length);              // 2, not 1
Console.WriteLine(char.IsHighSurrogate(s[0])); // True

// System.Text.Rune (.NET Core 3.0+) is a real Unicode scalar value:
foreach (Rune r in s.EnumerateRunes())
    Console.WriteLine(r.Value);           // 128512, once

// And a "grapheme cluster" is what a user calls a character:
var e = StringInfo.GetTextElementEnumerator("é");  // may be 1 or 2 chars (NFC vs NFD)
```

Three levels, and you need to know which one your requirement is about: **code unit** (`char`, what `Length` counts), **scalar** (`Rune`, what a codepoint is), **grapheme cluster** (`StringInfo`, what a human sees).

> **\[Trap\]**
>
> **Trap.** Truncating a string to N characters with `Substring(0, n)` can split a surrogate pair and produce invalid UTF-16, or split a combining sequence and move an accent onto the wrong letter. Database column limits are usually in code units too — worth checking against your provider.

### Strings and text

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Strings and text</p>

<a id="be-m79"></a>

#### String internals: immutability, layout, interning

A `string` is a reference type holding an `int` length followed by UTF-16 code units inline in the object. Cost on x64: **22 + 2n bytes**, rounded up to 8. A 10-character string is 40 bytes; an empty string is 24.

Immutability buys you: safe sharing across threads, a cacheable hash code, and safe use as a dictionary key. It costs you: every transformation allocates.

**Interning.** String literals in your assemblies are stored once in an intern pool and share a reference. Runtime-built strings are not interned unless you ask.

``` csharp
string a = "hello", b = "hello";
Console.WriteLine(ReferenceEquals(a, b));            // True - both literals

string c = new string(['h','e','l','l','o']);
Console.WriteLine(ReferenceEquals(a, c));            // False
Console.WriteLine(ReferenceEquals(a, string.Intern(c))); // True
```

> **\[Trap\]**
>
> **Do not intern at runtime as a memory optimisation.** The intern pool is rooted for the life of the process — interned strings never get collected. For deduplicating strings read from a database or a file, use your own `Dictionary<string,string>` or a `ConcurrentDictionary` cache you can clear.

``` csharp
// A per-request or per-load dedup pool you actually control:
var pool = new Dictionary<string, string>(StringComparer.Ordinal);
string Dedup(string s) => pool.TryGetValue(s, out var e) ? e : pool[s] = s;
```

<a id="be-m80"></a>

#### String comparison: ordinal vs culture

This is the highest-value five minutes in this whole guide. There are two fundamentally different operations wearing the same name.

| Comparison          | What it does                                  | Speed             | Use for                                                            |
|---------------------|-----------------------------------------------|-------------------|--------------------------------------------------------------------|
| `Ordinal`           | compares UTF-16 code units numerically        | fast (vectorised) | identifiers, keys, paths, protocol tokens, anything machine-facing |
| `OrdinalIgnoreCase` | ordinal + ASCII/invariant case fold           | fast              | header names, enum-ish strings, file names                         |
| `CurrentCulture`    | linguistic collation for the thread's culture | slow (ICU call)   | sorting a list shown to a user                                     |
| `InvariantCulture`  | linguistic, culture-independent               | slow              | rare; stable linguistic ordering                                   |

``` csharp
// == and Equals(string) are ORDINAL. This surprises people who expect culture rules.
"straße" == "strasse"                          // False
string.Equals(a, b, StringComparison.OrdinalIgnoreCase)  // explicit, best

// But these are CULTURE-SENSITIVE by default:
a.StartsWith(b); a.EndsWith(b); a.IndexOf(b); a.CompareTo(b); a.ToUpper();
// Always pass a StringComparison:
a.StartsWith(b, StringComparison.Ordinal);
a.IndexOf(b, StringComparison.Ordinal);
a.ToUpperInvariant();   // not ToUpper()
```

> **\[Trap\]**
>
> **The Turkish-I bug.** In `tr-TR`, `"i".ToUpper()` is `"İ"`, not `"I"`. Code that upper-cases a header name or a config key to compare it breaks on a Turkish-locale server. Case-insensitive comparison should *never* be done by changing case — use `StringComparison.OrdinalIgnoreCase`.

> **\[Tip\]**
>
> **Enforce it.** Turn on analyzer rules CA1307, CA1309, CA1310 and CA1862 to flag every culture-sensitive call that lacks an explicit `StringComparison`. On a legacy codebase this typically finds hundreds of latent bugs.

Goes deeper: [Formatting and parsing across cultures](#be-m97)

<a id="be-m81"></a>

#### Concatenation, StringBuilder, interpolation

The compiler is smarter than the folklore. Know what each form actually compiles to:

``` text
"a" + "b" + "c"          // constant-folded at compile time -> "abc", zero cost
a + b + c + d            // one call to string.Concat(a,b,c,d) - ONE allocation
$"{a} and {b}"           // DefaultInterpolatedStringHandler: pooled buffer, one alloc
string.Join(",", items)  // one pass, one allocation - best for sequences

// This is the actual quadratic disaster:
for (int i = 0; i < n; i++) s += x;   // n allocations, O(n^2) bytes copied
```

**Rule of thumb:** a fixed, small number of concatenations in one expression → just use `+` or interpolation. Concatenation inside a loop, or an unknown number of pieces → `StringBuilder` or `string.Join`.

``` csharp
var sb = new StringBuilder(capacity: estimatedLength); // pre-size to avoid chunk growth
foreach (var item in items) sb.Append(item.Name).Append(", ");
return sb.ToString();   // the only allocation of the final string
```

`StringBuilder` is a linked list of char chunks, so `Append` is amortised O(1) but indexing (`sb[i]`) is O(chunks). Do not use it as a random-access buffer.

> **\[Tip\]**
>
> **Zero-allocation formatting.** `string.Create(length, state, (span, s) => { ... })` lets you write directly into the final string's buffer. For logging, prefer structured logging with message templates (`logger.LogInformation("User {Id} did {Action}", id, action)`) — the string is never built at all when the level is disabled.

<a id="be-m82"></a>

#### Span of char and zero-allocation parsing

`ReadOnlySpan<char>` is a view over existing characters — a pointer and a length. Slicing it is free. This turns the classic parse-by-splitting into an allocation-free operation.

``` csharp
// Before: 1 array + N strings + N boxed parses
var parts = line.Split(',');
int id = int.Parse(parts[0]);

// After: zero allocations
ReadOnlySpan<char> span = line;
int comma = span.IndexOf(',');
int id = int.Parse(span[..comma]);
ReadOnlySpan<char> rest = span[(comma + 1)..];
```

Most BCL parse and format APIs have span overloads: `int.TryParse(ReadOnlySpan<char>, out int)`, `DateTime.TryParseExact`, `TryFormat(Span<char>, out int written)`, and the `IUtf8SpanFormattable` family in .NET 8+.

``` csharp
// Stack-allocate small buffers instead of renting or allocating:
Span<char> buffer = stackalloc char[32];
if (value.TryFormat(buffer, out int written)) { /* use buffer[..written] */ }
// Rule: only stackalloc a CONSTANT, small size (<= ~1KB) and never in a loop.
```

> **\[Trap\]**
>
> **Constraint.** `Span<T>` is a `ref struct`. You cannot use it across an `await`, in an iterator, in a lambda, or as a class field. Use `Memory<T>` / `ReadOnlyMemory<T>` for those cases and call `.Span` at the point of use.

> **\[Why it matters\]**
>
> **When it is worth it.** Parsing a CSV of a million rows, a hot protocol decoder, a serializer: yes. A method called twice per HTTP request: no — you have made the code harder to read for a saving you cannot measure.

Worked example: [Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)

<a id="be-m83"></a>

#### Encoding: UTF-8, UTF-16, and byte counts

.NET strings are UTF-16 in memory. Almost everything on the wire and on disk is UTF-8. Every boundary crossing is a transcode.

``` text
byte[] bytes = Encoding.UTF8.GetBytes(s);          // allocates
int n = Encoding.UTF8.GetByteCount(s);             // no allocation, just counts
Encoding.UTF8.GetBytes(s, destinationSpan);        // into your own buffer
```

UTF-8 byte counts are not character counts: ASCII is 1 byte, Latin/Greek/Cyrillic 2, most CJK and Vietnamese with diacritics 3, emoji 4. A `varchar(50)` in a UTF-8 collation holds 50 *bytes*.

``` text
Encoding.UTF8              // emits a BOM when used via GetPreamble/StreamWriter
new UTF8Encoding(false)    // no BOM - what you almost always want for files/HTTP
new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                           // fail loudly on malformed input instead of silently
                           // substituting U+FFFD
```

> **\[Tip\]**
>
> **Modern path.** `System.Text.Json`, Kestrel, and `HttpClient` work in UTF-8 bytes end to end. If you find yourself doing `Encoding.UTF8.GetString(...)` just to deserialize, pass the bytes or the `Stream` straight to `JsonSerializer.Deserialize` and skip the intermediate string entirely — on a large payload this is one of the easiest large allocation wins available.

> **\[Trap\]**
>
> **Legacy trap.** Code pages beyond the built-in set (Windows-1252, Shift-JIS) require `System.Text.Encoding.CodePages` and `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` on .NET Core and later. Code that worked on .NET Framework throws `NotSupportedException` after a migration.

### Text, bytes, and streams

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Text, bytes, and streams</p>

<a id="be-m84"></a>

#### Regex: backtracking, ReDoS, and source generation

.NET's default regex engine backtracks: when a branch fails it rewinds and tries alternatives. For most patterns that is fine. For patterns with nested or adjacent quantifiers over overlapping character sets, the number of paths explodes exponentially.

``` csharp
// Catastrophic on a 30-character non-matching input - hours, not milliseconds.
new Regex(@"^(a+)+$").IsMatch(new string('a', 30) + "!");

// The same shape hides in innocuous-looking validators:
@"^(\s*\w+\s*)+$"          // nested quantifier over overlapping classes
@"^(\d+|\w+)*$"            // alternation inside a quantifier
@"^([a-zA-Z]+\.)*[a-zA-Z]+@"   // the classic "email validator" ReDoS
```

If the pattern or the input is user-supplied, this is a denial-of-service vector: one 40-character request pins a core indefinitely.

``` csharp
// Three defences, in order of preference.

// 1. Linear-time engine (.NET 7+). No backtracking, guaranteed O(n).
new Regex(pattern, RegexOptions.NonBacktracking);
//    Cost: no backreferences, no lookaround, no atomic groups.

// 2. A timeout. Always set one on any regex over untrusted input.
new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
//    Throws RegexMatchTimeoutException rather than hanging.

// 3. Source generation (.NET 7+): compiled at build time, no startup cost,
//    trimming/AOT friendly, and the analyzer warns about risky constructs.
[GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
private static partial Regex IsoDate();
```

| Construction            | Startup                  | Match speed | Use for                   |
|-------------------------|--------------------------|-------------|---------------------------|
| `new Regex(p)` per call | parse every time         | interpreted | never in a loop           |
| `static readonly Regex` | once                     | interpreted | cold paths                |
| `RegexOptions.Compiled` | emits IL, slow first use | fast        | hot, long-lived processes |
| `[GeneratedRegex]`      | none                     | fast        | **the default now**       |

> **\[Trap\]**
>
> **The static `Regex.IsMatch(input, pattern)` overloads** use a small internal cache keyed by the pattern (15 entries by default). Exceed it with dynamically-built patterns and every call re-parses. Either use instance regexes held in static fields, or raise `Regex.CacheSize` deliberately.

> **\[Tip\]**
>
> **Often the answer is not a regex.** Validating a date, splitting on a delimiter, checking a prefix, or extracting a fixed-position field are all faster, clearer, and unattackable with `ReadOnlySpan<char>` methods ([Span of char and zero-allocation parsing](#be-m82)). Reach for regex when the grammar is genuinely irregular.

Worked example: [Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)

<a id="be-m85"></a>

#### SearchValues and vectorized searching

`SearchValues<T>` (.NET 8) precomputes an optimal search strategy for a fixed set of values — a bitmap, a SIMD probe, or a lookup table depending on the set — and reuses it on every call. It is one of the cheapest wins available in text-heavy code.

``` csharp
// Build once, in a static field. Construction is what costs; searching is what repeats.
private static readonly SearchValues<char> Delimiters = SearchValues.Create(",;\t|");
private static readonly SearchValues<char> Invalid    = SearchValues.Create("<>\"'&\0");
private static readonly SearchValues<byte> CrLf       = SearchValues.Create("\r\n"u8);

int i = line.AsSpan().IndexOfAny(Delimiters);
bool clean = text.AsSpan().IndexOfAny(Invalid) < 0;
int end = buffer.Span.IndexOfAny(CrLf);

// .NET 9 extends it to whole strings - a fast multi-substring scan:
private static readonly SearchValues<string> Keywords =
    SearchValues.Create(["SELECT", "INSERT", "DROP"], StringComparison.OrdinalIgnoreCase);
int hit = sql.AsSpan().IndexOfAny(Keywords);
```

Compared with the obvious alternatives, on a large input:

| Approach                                    | Behaviour                                         |
|---------------------------------------------|---------------------------------------------------|
| `foreach (var c in s) if (set.Contains(c))` | one char at a time, hash per char                 |
| `s.IndexOfAny(charArray)`                   | vectorised for ≤5 chars, linear beyond            |
| `Regex` character class                     | engine overhead per call                          |
| `SearchValues`                              | vectorised for any set size, strategy chosen once |

``` csharp
// The idiomatic scanning loop: find, slice, repeat - no allocation anywhere.
static void ScanFields(ReadOnlySpan<char> line) {
    while (!line.IsEmpty) {
        int i = line.IndexOfAny(Delimiters);
        var field = i < 0 ? line : line[..i];
        Handle(field);
        line = i < 0 ? default : line[(i + 1)..];
    }
}
```

> **\[Trap\]**
>
> **Construct it once.** `SearchValues.Create` is deliberately expensive — it analyses the set to pick an implementation. Calling it inside the method that searches makes the code slower than the naive version it replaced. It belongs in a `static readonly` field, exactly like a compiled regex.

Worked example: [Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)

<a id="be-m86"></a>

#### Normalization and searchable text

The same visible text can have several byte representations. "é" is either one code point (U+00E9, composed) or two (U+0065 + U+0301, decomposed). Ordinal comparison says they differ; a user says they are the same word.

``` csharp
var composed   = "\u00e9";           // é
var decomposed = "e\u0301";          // e + combining acute
composed == decomposed;                          // False - different code units
composed.Normalize(NormalizationForm.FormC) ==
    decomposed.Normalize(NormalizationForm.FormC);  // True
```

| Form                | Does                                      | Use for                                 |
|---------------------|-------------------------------------------|-----------------------------------------|
| `FormC`             | compose — the canonical storage form      | **normalise on input, store this**      |
| `FormD`             | decompose                                 | stripping diacritics                    |
| `FormKC` / `FormKD` | \+ compatibility folding (Ａ → A, ﬁ → fi) | search keys, never for storage of names |

``` csharp
// A search key: accent-folded, case-folded, whitespace-collapsed.
static string SearchKey(string input) {
    var d = input.Normalize(NormalizationForm.FormD);
    var sb = new StringBuilder(d.Length);
    foreach (var ch in d)
        if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            sb.Append(char.ToLowerInvariant(ch));
    return sb.ToString().Normalize(NormalizationForm.FormC);
}
// "Nguyễn"  -> "nguyen"      "Café" -> "cafe"
```

> **\[Trap\]**
>
> **Diacritic stripping is not universal.** Vietnamese `đ`/`Đ` is a distinct letter, not `d` plus a mark, so decomposition leaves it untouched — you need an explicit mapping. German `ß` folds to `ss` only under compatibility rules. Turkish dotless `ı` and Danish `ø` behave similarly. There is no correct language-independent fold; there is only a fold that suits your users.

> **\[Tip\]**
>
> **Store both.** Keep the original text exactly as entered — names are identity, and mangling them is a real harm — and keep a computed, indexed `SearchKey` column beside it. Search hits the key column with an ordinary index; display uses the original. This beats a case- and accent-insensitive collation because it is explicit, portable, and does not make your predicates non-sargable ([Column types, sizing, and collation](07-02-the-relational-model-and-sql.md#be-m190)).

Worked example: [Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)

<a id="be-m87"></a>

#### Fuzzy matching

Levenshtein distance is the number of single-character insertions, deletions, or substitutions needed to turn one string into another. The textbook implementation allocates an *n*×*m* matrix; you only ever need two rows.

``` csharp
public static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int max = int.MaxValue) {
    if (Math.Abs(a.Length - b.Length) > max) return max + 1;   // early exit
    if (a.Length > b.Length) (a, b) = (b, a);                  // keep the row short

    Span<int> prev = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
    Span<int> curr = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
    for (int j = 0; j <= b.Length; j++) prev[j] = j;

    for (int i = 1; i <= a.Length; i++) {
        curr[0] = i;
        int rowMin = i;
        for (int j = 1; j <= b.Length; j++) {
            int cost = a[i - 1] == b[j - 1] ? 0 : 1;
            curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            rowMin = Math.Min(rowMin, curr[j]);
        }
        if (rowMin > max) return max + 1;                      // cannot get better
        prev.CopyTo(curr); (prev, curr) = (curr, prev);
    }
    return prev[b.Length];
}
```

| Metric              | Good at                                | Cost                    |
|---------------------|----------------------------------------|-------------------------|
| Levenshtein         | typos, OCR errors                      | O(nm)                   |
| Damerau-Levenshtein | \+ transpositions (*teh*/*the*)        | O(nm)                   |
| Jaro-Winkler        | short strings, shared prefixes — names | O(nm), fast in practice |
| Trigram similarity  | long text, indexable in Postgres       | O(n), set intersection  |
| Soundex / Metaphone | phonetic, English-centric              | O(n)                    |

> **\[Trap\]**
>
> **It does not scale by itself.** Comparing every pair in a 100,000-row table is 5×10<sup>9</sup> distance computations. Always *block* first: only compare records that share a cheap key — same first two letters, same postcode, same trigram bucket — then run the expensive metric within blocks. That turns an O(n²) problem into O(n × block size), and it is how every real record-linkage system works ([Union-find: grouping and deduplication](03-06-graphs.md#be-m120)).

> **\[Tip\]**
>
> Do this in the database when it has support: PostgreSQL's `pg_trgm` gives GIN-indexed similarity search; SQL Server has `DIFFERENCE`/`SOUNDEX` (weak) and full-text search with fuzzy operators. Pulling a table into memory to fuzzy-match it is a last resort, not a first one.

### Crossing boundaries

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Crossing boundaries</p>

<a id="be-m97"></a>

#### Formatting and parsing across cultures

Every conversion between text and a value carries a culture. Getting the default is a bug waiting for a server in a different locale.

``` csharp
var value = 1234.56m;
value.ToString(CultureInfo.InvariantCulture);   // "1234.56"
value.ToString(new CultureInfo("de-DE"));       // "1234,56"   comma decimal
value.ToString(new CultureInfo("vi-VN"));       // "1234,56"

decimal.Parse("1,234", CultureInfo.InvariantCulture);   // 1234    - thousands separator
decimal.Parse("1,234", new CultureInfo("de-DE"));       // 1.234   - decimal separator
// Same input. Same method. Two answers that differ by a factor of 1000.
```

**The rule is a boundary rule:** use `InvariantCulture` for anything a machine reads or writes — files, APIs, database literals, logs, config, cache keys — and `CurrentCulture` only for text a human will read.

``` csharp
// Machine-facing: explicit, round-trippable, unambiguous
DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);  // ISO 8601, full precision
value.ToString(CultureInfo.InvariantCulture);
long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                       out var d);

// Human-facing: let the culture decide
amount.ToString("C", CultureInfo.CurrentCulture);   // ₫1.234 or $1,234.00
date.ToString("D", CultureInfo.CurrentCulture);     // long date, localised
```

> **\[Trap\]**
>
> **Currency formatting does not convert currency.** `ToString("C")` applies the *culture's* symbol, so a USD amount rendered under `vi-VN` displays as đồng — the same number with the wrong unit, which is worse than an error. Format money from its own currency code, not from the ambient culture ([Units and dimensional safety](01-02-numbers-inside-the-machine.md#be-m72)).

> **\[Tip\]**
>
> **Analyzer rules CA1305 and CA1304** flag every call that omits an explicit `IFormatProvider`. Enabling them on a legacy codebase typically surfaces hundreds of latent locale bugs at once, most of which have simply never run on a non-English server — and each is a one-word fix ([String comparison: ordinal vs culture](#be-m80)).
