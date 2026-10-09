---
id: "2-1"
number: "2.1"
title: "What a programming language is"
part: 2
part_title: "From source code to running program"
words: 3778
minutes: 16
---

# 2.1 What a programming language is

> **Quick look.** What a programming language is, and how a tool turns text into meaning.
>
> - A language has syntax (rules for valid code) and semantics (what valid code means).
> - Tools work in stages: split into tokens, parse into a syntax tree, check names and types, produce output.
> - Operator precedence is built into the grammar, which is why it surprises people.
> - Languages mix styles: imperative, object-oriented, functional, declarative.
> - The language, its runtime and its standard library are separate; most learning time goes on the library.
> - Don't hand-roll parsers for real formats, and don't build code by gluing strings together.


### The big picture

Part 1 showed the machine as it really is. Nobody wants to write programs as raw instructions, so we use programming languages: precise notations that tools translate into instructions for us.

This chapter explains what a language actually is. You will see how a compiler reads text, splits it into pieces, builds a tree showing the structure and works out what it means, by building a small working language yourself. The chapter also covers the different styles of programming languages support, the difference between a language and its libraries, and how C# has changed in recent versions.

### Before you start

**Builds on:** 1.4 The processor.

**Ideas explained later:**

- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **SDK** (11.2): Software development kit: the tools needed to build for a platform. The .NET SDK includes the dotnet command, compilers and MSBuild.
- **Target framework** (11.2): The .NET version a project builds for, such as net10.0, which decides the APIs it may use.
- **JIT** (JIT compiler, 2.2): A compiler that turns bytecode into machine code while the program runs, just before it is needed.
- **Machine code** (2.2): Instructions encoded as numbers, ready for a specific processor to run.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Bytecode** (2.2): Compact instructions for a virtual machine, run by an interpreter or JIT.

**Tools and technologies named here:**

- **LINQ** (2.5): Language Integrated Query: C# methods such as Where, Select and Count that filter and transform sequences of items.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **CLR** (2.2): Common Language Runtime: the .NET runtime that loads, JIT-compiles and runs .NET code and manages its memory.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, LINQ, switch expressions and patterns, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between syntax and semantics?**

> Syntax is the shape of valid code: which symbols can go where. Semantics is what valid code means when it runs. `x = x + 1` has the same syntax in C# and Python, and nearly the same semantics.

**2. What is an abstract syntax tree?**

> A tree that represents the structure of a program after parsing. `2 * (3 + 4)` becomes a multiply node whose children are 2 and an add node. Compilers, analyzers and formatters all work on this tree, not on raw text.

**3. Why do both 2 + 3 * 4 and 2 + (3 * 4) give 14?**

> Precedence. Multiplication binds more tightly than addition, so the parser builds the same tree for both. The grammar encodes this, usually by giving each precedence level its own rule.

**4. Is SQL imperative or declarative?**

> Declarative. You describe the result you want, and the database decides how to get it. LINQ query syntax in C# works the same way. A `foreach` loop with an `if` is imperative: it spells out each step.

**5. Which C# version comes with .NET 10?**

> C# 14. Each .NET release brings a new C# version, and the SDK picks the language version to match the target framework unless you override it.

**6. What is the difference between C#, the .NET runtime and the base class library?**

> C# is the language: syntax and meaning. The runtime (the CLR) loads and runs compiled code and manages memory. The base class library is the huge set of ready-made types, such as `List<T>`, `File` and `HttpClient`. The same runtime and library also serve F# and Visual Basic.

### Why languages exist

Part 1 ended with the machine as it really is: registers, memory, instructions and an operating system. You could write every program as a list of machine instructions. People did, in the 1940s and 1950s. It was slow, error-prone and tied to one kind of machine.

A **programming language** is a precise, human-readable notation for describing what a computer should do. A tool then translates it into the machine's instructions. How that translation happens is the subject of Compilers, interpreters and JIT (2.2).

A language gives you **abstraction**. It lets you think in terms of names, numbers, lists and decisions instead of registers and addresses. Every language picks which details of the machine to hide and which to keep visible. That choice shapes what the language is good at.

### Levels of abstraction

Languages sit at different distances from the machine.

**Assembly language** is one step above machine code, as shown in The processor (1.4). Each line is one instruction.

**Systems languages**, such as C, C++ and Rust, compile straight to machine code. They let you control memory layout and allocation yourself. Operating systems, databases, browsers and game engines are written in them. Aircraft flight software is usually written in C or Ada.

**Managed languages**, such as C#, Java, Kotlin and Go, run with the help of a runtime. The runtime manages memory with a garbage collector and checks things like array bounds. You give up some control and get safety and speed of development.

**Scripting languages**, such as Python, JavaScript and shell scripts, are usually run by an interpreter or a JIT engine. They favour quick writing and flexibility over raw speed.

These are loose groups, not strict boxes. JavaScript engines are very fast today. C# can do low-level work with spans and unsafe code. Most modern languages borrow freely from each other.

### Syntax and semantics

Every language has two layers.

**Syntax** is the set of rules for what counts as valid code. It says that `if` is followed by a condition in parentheses, and that statements end in a semicolon. The formal rules are called a **grammar**.

**Semantics** is what valid code means. It says what happens when the code runs. `a / b` has the same syntax in C# and Python. Its semantics differ: C# does integer division when both sides are integers, and Python 3's `/` always gives a fraction.

Most bugs are semantic. The code is valid, and it does something other than what you meant.

### From text to structure

A compiler or interpreter doesn't understand text directly. It works in stages.

First, **lexing**, also called tokenizing. The text is split into **tokens**: the smallest meaningful pieces. `total = price * 2;` becomes the tokens `total`, `=`, `price`, `*`, `2` and `;`. Whitespace and comments are dropped here.

Second, **parsing**. A **parser** reads the tokens and checks them against the grammar. It builds a tree that shows how the pieces fit together. This is the **abstract syntax tree**, or AST. "Abstract" means details like parentheses and semicolons are gone; only the structure remains.

Third, **semantic analysis**. The compiler works out what each name refers to, checks types and reports errors like "variable not found".

Finally, **code generation**. The tree becomes machine code, bytecode or another language.

The C# compiler, called Roslyn, exposes its syntax trees to tools. That is how analyzers, refactorings and source generators work.

### A tiny language

The best way to see these stages is to build them. This program implements a complete small language for arithmetic. It tokenizes, parses into a tree and evaluates the tree.

```csharp
// calc.cs — run with: dotnet run calc.cs
using System.Globalization;
using System.Linq;

string source = "2 * (3 + 4) - 10 / 5";
List<Token> tokens = Lexer.Tokenize(source);
Console.WriteLine(string.Join(" ", tokens.Select(t => t.Kind)));
// Number Star LParen Number Plus Number RParen Minus Number Slash Number End

Expr tree = new Parser(tokens).ParseAll();
Console.WriteLine(Tree.Show(tree));   // (- (* 2 (+ 3 4)) (/ 10 5))
Console.WriteLine(Tree.Eval(tree));   // 12

try { new Parser(Lexer.Tokenize("2 + * 3")).ParseAll(); }
catch (FormatException ex) { Console.WriteLine(ex.Message); }   // Unexpected '*'

enum Kind { Number, Plus, Minus, Star, Slash, LParen, RParen, End }
record Token(Kind Kind, string Text);

abstract record Expr;
record Num(double Value) : Expr;
record Binary(char Op, Expr Left, Expr Right) : Expr;

static class Lexer
{
    public static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
                list.Add(new Token(Kind.Number, s[start..i]));
                continue;
            }
            Kind k = c switch
            {
                '+' => Kind.Plus, '-' => Kind.Minus, '*' => Kind.Star, '/' => Kind.Slash,
                '(' => Kind.LParen, ')' => Kind.RParen,
                _ => throw new FormatException($"Unexpected '{c}' at position {i}"),
            };
            list.Add(new Token(k, c.ToString()));
            i++;
        }
        list.Add(new Token(Kind.End, ""));
        return list;
    }
}

// The grammar, one rule per precedence level:
//   expression := term   (("+" | "-") term)*
//   term       := factor (("*" | "/") factor)*
//   factor     := number | "(" expression ")"
class Parser(List<Token> tokens)
{
    int pos;
    Token Peek => tokens[pos];
    Token Next() => tokens[pos++];

    public Expr ParseAll()
    {
        Expr e = ParseExpression();
        if (Peek.Kind != Kind.End) throw new FormatException($"Unexpected '{Peek.Text}'");
        return e;
    }

    Expr ParseExpression()
    {
        Expr left = ParseTerm();
        while (Peek.Kind is Kind.Plus or Kind.Minus)
        {
            char op = Next().Text[0];
            left = new Binary(op, left, ParseTerm());   // the loop makes 10 - 3 - 2 mean (10 - 3) - 2
        }
        return left;
    }

    Expr ParseTerm()
    {
        Expr left = ParseFactor();
        while (Peek.Kind is Kind.Star or Kind.Slash)
        {
            char op = Next().Text[0];
            left = new Binary(op, left, ParseFactor());
        }
        return left;
    }

    Expr ParseFactor()
    {
        Token t = Next();
        if (t.Kind == Kind.Number) return new Num(double.Parse(t.Text, CultureInfo.InvariantCulture));
        if (t.Kind == Kind.LParen)
        {
            Expr inner = ParseExpression();
            if (Next().Kind != Kind.RParen) throw new FormatException("Expected ')'");
            return inner;
        }
        throw new FormatException($"Unexpected '{t.Text}'");
    }
}

static class Tree
{
    public static string Show(Expr e) => e switch
    {
        Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        Binary b => $"({b.Op} {Show(b.Left)} {Show(b.Right)})",
        _ => throw new InvalidOperationException(),
    };

    public static double Eval(Expr e) => e switch
    {
        Num n => n.Value,
        Binary { Op: '+' } b => Eval(b.Left) + Eval(b.Right),
        Binary { Op: '-' } b => Eval(b.Left) - Eval(b.Right),
        Binary { Op: '*' } b => Eval(b.Left) * Eval(b.Right),
        Binary { Op: '/' } b => Eval(b.Left) / Eval(b.Right),
        _ => throw new InvalidOperationException(),
    };
}
```

Look at how precedence works. `ParseExpression` handles `+` and `-`. It calls `ParseTerm` for each side, which handles `*` and `/`. So multiplication is grouped first, deeper in the tree. Parentheses send the parser back up to `ParseExpression`, which is how they override precedence. This technique is called **recursive descent**, and many real compilers, including Roslyn, use it.

`Eval` walks the tree and computes a value. That makes this program an **interpreter**. A compiler would walk the same tree and write out instructions instead.

### Paradigms

A **paradigm** is a style of programming, a way of organizing your thinking about a program.

**Imperative programming** describes steps. Do this, then this, then repeat until done. Machine code is imperative, and so is most everyday C#.

**Object-oriented programming** groups data and the code that works on it into objects. C#, Java and Python support it heavily.

**Functional programming** builds programs from functions that take values and return new values, avoiding changing things in place. F#, Haskell and Elm are built around it. LINQ brings much of it into C#.

**Declarative programming** describes the result you want and leaves the steps to the system. SQL, HTML, CSS and regular expressions are declarative.

Here is the same question asked two ways.

```csharp
// paradigms.cs — run with: dotnet run paradigms.cs
using System.Linq;

int[] delays = { 120, 45, 300, 80, 15 };   // delay in minutes for five flights

// Imperative: say how
int late = 0;
foreach (int d in delays)
    if (d > 60) late++;
Console.WriteLine(late);                           // 3

// Declarative: say what
Console.WriteLine(delays.Count(d => d > 60));      // 3

// The same question in SQL:
//   SELECT COUNT(*) FROM Flights WHERE DelayMinutes > 60;
```

Modern languages are **multi-paradigm**. C# lets you mix all of these in one file. Choosing the right style for each piece of a program is covered in Procedural, object-oriented and functional styles (4.1).

### General-purpose and domain-specific languages

A general-purpose language can build anything: C#, Java, Python, Go. A **domain-specific language** (DSL) is made for one kind of problem. SQL is for querying data. Regular expressions are for matching text. HTML is for page structure. MSBuild files, Dockerfiles and YAML pipelines are DSLs too.

DSLs are powerful because they are narrow. A few lines of SQL can replace pages of loops. They are also a common source of bugs, because people use them less often and learn them less deeply.

### Language, runtime and library

When people say "C#", they often mean four different things.

The **language** is the syntax and semantics: what `record`, `await` and `switch` mean.

The **runtime** runs compiled code. For .NET it is the Common Language Runtime, or CLR. It loads code, compiles it to machine code, manages memory and handles exceptions.

The **standard library** is the set of ready-made types that come with the platform. In .NET it is the base class library: collections, files, networking, JSON, cryptography and much more.

The **ecosystem** is everything else: the NuGet package repository, tools, frameworks like ASP.NET Core and EF Core, and the community.

Learning a new language usually takes days. Learning its library and ecosystem well takes months. That is where most of your relearning time will go.

### Versions and standards

Languages change. Each version adds features and sometimes changes rules.

C# has moved fast. C# 8, with .NET Core 3.0 in 2019, added nullable reference types. C# 9 added records and top-level statements. C# 10 added global usings and file-scoped namespaces. C# 11 added raw string literals, list patterns and generic math. C# 12 added primary constructors and collection expressions. C# 13 added `params` collections and a new `Lock` type. C# 14, with .NET 10, adds extension members and the `field` keyword for properties.

The SDK picks the language version from the project's target framework. A project targeting .NET 8 gets C# 12 by default. You can set `<LangVersion>` to override it, but some features need runtime support too.

Some languages are formally standardized. C# has an ECMA standard, ECMA-334. JavaScript is defined by ECMAScript, ECMA-262, with a new edition every year. SQL has an ISO standard, but every database speaks its own dialect of it. TypeScript is defined by its compiler.

### Languages in safety-critical systems

Software that can hurt people if it fails is held to stricter rules. In aviation, airborne software is certified under a standard called DO-178C. The most critical levels demand full traceability from requirements to code to tests, and very thorough test coverage.

This shapes language choice. Flight software is typically written in C under strict coding rules such as MISRA C, or in Ada, often with its provable subset SPARK. Garbage collectors, dynamic memory allocation after start-up and unbounded recursion are usually banned, because they make timing and memory use hard to predict. Ground systems, such as planning, maintenance and operations tools, are ordinary business software, and C# fits there well.

### In short

A language has syntax, the rules for valid code, and semantics, what valid code means. Tools read code in stages: split it into tokens, parse the tokens into a syntax tree, check names and types, then produce output. Operator precedence is built into the grammar. Languages support styles such as imperative, object-oriented, functional and declarative, and modern languages mix them. The language, its runtime and its standard library are separate things, and the library is where most learning time goes.

### What goes wrong

**Assuming the same syntax means the same semantics.** `/`, `%`, `==` and string comparison all behave differently between C#, JavaScript, Python and SQL. Check the rules when switching between them.

**Precedence surprises.** `a + b * c` is clear, but mixes like `x & mask == 0`, `a ?? b + c` and `!a && b` trip people up. Add parentheses whenever the grouping isn't obvious to a reader.

**Hand-rolled parsers for real formats.** Parsing CSV, JSON, dates or URLs with `Split` and `IndexOf` fails on quotes, escapes and edge cases. Use a proper library for any format you didn't invent.

**Building code as strings.** Assembling SQL, shell commands or HTML by joining strings mixes data into code. That is how injection attacks happen. Use parameters and proper builders instead.

**Mismatched language and runtime versions.** Forcing a newer `<LangVersion>` onto an older target framework compiles some features that then fail at run time or not at all. Keep them in step.

### Cost model

Lexing and parsing are fast, roughly proportional to the size of the source. The expensive part of compiling is usually semantic analysis and optimization. An interpreter that walks a tree, like `calc.cs`, is simple but slow: each node costs a method call and a type check. Real interpreters compile to bytecode first, and fast engines compile hot code to machine code, as the next chapter shows.

### In other languages (optional)

**JavaScript and TypeScript.** JavaScript is multi-paradigm: it has objects, first-class functions and prototype-based inheritance. TypeScript adds a type system on top and compiles to plain JavaScript. The ECMAScript edition you can use depends on the browsers or Node.js version you target.

**Python.** Python's grammar uses indentation instead of braces. The `ast` module parses Python source into a tree you can inspect: `ast.dump(ast.parse("2 * (3 + 4)"))`.

**SQL.** Each database has its own dialect. `TOP 10` in SQL Server is `LIMIT 10` in PostgreSQL and MySQL. Standard SQL uses `FETCH FIRST 10 ROWS ONLY`.

**F#.** Also runs on .NET and uses the same libraries as C#. It is functional first, with a lighter syntax.

### Key terms

- **Programming language**: A precise notation for describing what a computer should do, translated into machine instructions by tools.
- **Abstraction**: Hiding details so you can work with bigger ideas, such as names and lists instead of addresses and registers.
- **Syntax**: The rules for what counts as valid code.
- **Semantics**: What valid code means when it runs.
- **Grammar**: The formal set of syntax rules for a language.
- **Token**: The smallest meaningful piece of source code, such as a name, a number or an operator.
- **Parser**: The part of a compiler that checks tokens against the grammar and builds a tree.
- **Abstract syntax tree**: A tree showing the structure of a program, with punctuation removed. Often shortened to AST.
- **Recursive descent**: A parsing technique with one method per grammar rule, where rules call each other.
- **Paradigm**: A style of programming, such as imperative, object-oriented, functional or declarative.
- **Imperative programming**: Describing a computation as a sequence of steps that change state.
- **Declarative programming**: Describing the result you want and leaving the steps to the system. SQL is declarative.
- **Domain-specific language**: A language made for one kind of problem, such as SQL or regular expressions. Often shortened to DSL.
- **Runtime**: The software that loads and runs compiled code and manages memory. For .NET, the CLR.
- **Standard library**: The ready-made types that come with a language platform. In .NET, the base class library.

### Where this leads

How source code becomes running machine code is in Compilers, interpreters and JIT (2.2). The rules a type system adds on top of syntax are in Types (2.3). Programming styles come back in Procedural, object-oriented and functional styles (4.1). SQL as a declarative language is in The relational model and SQL (7.2). Why building code from strings is dangerous is in Common web vulnerabilities (10.4).

### Check yourself

1. Add a `%` operator to `calc.cs` at the same precedence as `*` and `/`. Test `10 % 4 + 1`.
2. Add unary minus, so that `-3 * 2` and `2 * -(1 + 1)` work. Decide which grammar rule it belongs in.
3. Add a `^` power operator that binds tighter than `*` and groups from the right, so `2 ^ 3 ^ 2` is 512.
4. Rewrite the imperative loop in `paradigms.cs` to also compute the average delay of the late flights, then do the same with LINQ.
5. Check the `<TargetFramework>` and `<LangVersion>` of a project you work on. Work out which C# version it uses.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A config parser that broke on the first quoted comma

A tool reads airport lists from CSV with `line.Split(',')`. A partner sends "Washington, Dulles" in quotes and every column after it shifts.

**What goes wrong.** CSV is a small language with quoting and escaping rules; splitting text by hand isn't parsing it.

*Use a real parser*

```csharp
// dotnet add package CsvHelper
using var reader = new StreamReader("airports.csv");
using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
foreach (var row in csv.GetRecords<AirportRow>())
    Console.WriteLine($"{row.Code}: {row.Name}");   // "Washington, Dulles" stays one field

record AirportRow(string Code, string Name, string Country);
```

> **Lesson:** Every real format has a grammar. Use a tested parser for CSV, JSON, XML and dates instead of string splitting.

#### Example 2: Generating SQL by gluing strings

A report builder concatenates user-chosen column names and values into SQL. A tester types `x' OR '1'='1` and sees every customer.

*Values as parameters, identifiers from an allow-list*

```csharp
static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
{
    ["date"] = "CreatedAt", ["total"] = "Total", ["customer"] = "CustomerName",
};

var column = SortColumns.GetValueOrDefault(request.SortBy, "CreatedAt");   // never user text
var sql = $"SELECT Id, Total FROM Orders WHERE CustomerId = @customerId ORDER BY {column} DESC";
var rows = await conn.QueryAsync<OrderRow>(sql, new { customerId = request.CustomerId });   // Dapper
```

> **Lesson:** Code built from strings mixes data with structure. Pass values as parameters and pick identifiers from a fixed list.
