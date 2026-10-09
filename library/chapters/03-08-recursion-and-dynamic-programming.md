---
id: "3-8"
number: "3.8"
title: "Recursion and dynamic programming"
part: 3
part_title: "Data structures and algorithms"
words: 4374
minutes: 19
---

# 3.8 Recursion and dynamic programming

> **Quick look.** Recursion for self-similar problems, and dynamic programming to stop repeating work.
>
> - A recursive function needs a base case and must shrink the problem every call.
> - C# doesn't guarantee tail calls; deep recursion on real data needs a loop with an explicit stack.
> - Memoization stores results of pure functions so repeated work disappears.
> - Dynamic programming builds answers from smaller stored answers (edit distance, knapsack).
> - Greedy choices are fast but sometimes wrong; NP-hard problems need solvers and heuristics.


### The big picture

Some problems contain smaller copies of themselves. A folder holds folders. A route is one flight plus a shorter route. The best cargo load is one choice plus the best load of what is left. Recursion solves such problems by having a function call itself on the smaller part.

Done naively, recursion can repeat the same work millions of times. This chapter shows how to write it safely in C#, how remembering answers turns hopeless running times into instant ones, and how dynamic programming solves practical problems like fuzzy matching and cargo loading. It ends Part 3 with problems nobody can solve quickly, and how airlines cope with them.

### Before you start

**Builds on:** 1.5 Memory, 3.4 Trees, 3.7 Searching and sorting, 3.6 Graphs.

**Ideas explained later:**

- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Main thread** (9.1): The browser thread that runs a page's JavaScript, handles input and renders.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Relational model** (7.2): Storing data as tables of rows and columns, connected by keys.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.

**The code uses:** variables and var, methods and local functions, string interpolation, records, collection expressions, generic types, lambdas, LINQ, null operators, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What two parts does every correct recursive function have?**

> A base case that returns without recursing, and a recursive case that calls the function on a smaller problem. Without a reachable base case, it recurses until the stack overflows.

**2. Why is the naive recursive Fibonacci function so slow?**

> It solves the same subproblems over and over. `Fib(30)` makes about 2.7 million calls to compute 31 distinct values. Remembering each result, called memoization, cuts that to one computation per distinct value.

**3. Does C# guarantee tail-call optimization?**

> No. The JIT sometimes turns a tail call into a jump, but you can't rely on it. Deep recursion in C# can always overflow the stack, so convert deep recursion into a loop.

**4. What two properties make a problem suitable for dynamic programming?**

> Overlapping subproblems: the same smaller problems come up many times. And optimal substructure: the best answer is built from best answers to smaller problems.

**5. What is the edit distance between "heatrow" and "heathrow"?**

> 1. One inserted letter turns one into the other. Edit distance counts the fewest single-character insertions, deletions and substitutions, and it is computed with dynamic programming.

**6. When does a greedy algorithm give the wrong answer?**

> When taking the locally best choice rules out a better overall result. Filling 6 units with containers of size 1, 3 and 4, greedy takes 4 + 1 + 1, three containers, where 3 + 3 needs only two.

**7. What does it mean that a problem is NP-hard?**

> No algorithm is known that solves every instance in polynomial time, and finding one would solve a whole family of famous hard problems. In practice you use exact search for small cases and heuristics or solvers for real sizes.

### Functions that call themselves

**Recursion** is a function solving a problem by calling itself on a smaller version of the same problem. It fits naturally wherever the data or the problem has the same shape at every level: a folder contains folders, a tree node has subtrees, a JSON object contains objects.

Every correct recursive function has two parts. The **base case** is small enough to answer directly, without recursing. The **recursive case** breaks the problem into smaller pieces, calls itself on them and combines the results. Each call must move closer to a base case. If it doesn't, the recursion never ends.

The total weight of a container with containers nested inside it is its own weight plus the total weight of each thing inside it. That sentence is the recursive case. A container with nothing inside is the base case.

### How recursion runs

Each call gets its own stack frame with its own parameters and locals, as Memory (1.5) described. A recursive function that goes 1,000 levels deep has 1,000 frames on the **call stack** at once. When the base case returns, the frames unwind one by one, each finishing its own work with the result from below.

The stack is small, typically 1 MB for the main thread on Windows. Depending on frame size, that allows somewhere between thousands and hundreds of thousands of levels. Go beyond it, and .NET throws a `StackOverflowException`, which can't be caught and ends the process.

Some languages turn a **tail call**, a recursive call that is the very last thing a function does, into a jump that reuses the current frame, so it uses no extra stack. F# and many functional languages rely on this. C# doesn't guarantee it. The JIT sometimes does it, but code that depends on it can crash on another machine or version.

So in C#, use recursion when the depth is naturally small, such as a balanced tree or the nesting of a JSON document. When depth could be large, such as a linked chain, a deep folder tree or data you don't control, use a loop with an explicit `Stack<T>`, as Trees (3.4) did.

### Divide and conquer

**Divide and conquer** is recursion with a strategy. Split the problem into independent parts, solve each part recursively, and combine the answers. Merge sort, quicksort and binary search in Searching and sorting (3.7) all work this way.

It often turns an O(n²) approach into O(n log n): splitting in half gives log n levels, and each level does O(n) work in total. The parts are independent, so they can also run on different cores, as Parallelism (5.5) shows.

### Remembering answers

Some recursive functions solve the same subproblem many times. The Fibonacci numbers are the classic example: each is the sum of the two before it. A direct recursive version computes `Fib(28)` twice, `Fib(27)` three times, `Fib(26)` five times, and so on. `Fib(30)` makes nearly 2.7 million calls to compute 31 distinct values. The cost is exponential.

**Memoization** fixes this by remembering each result the first time it is computed, usually in a dictionary keyed by the arguments. Every later call with the same arguments returns the stored answer. `Fib(30)` then computes each value once.

Memoization only works on functions whose result depends on nothing but their arguments, and that have no side effects. Such pure functions are the subject of Immutability and pure functions (4.4). It trades memory for time: the dictionary holds every result.

### Dynamic programming

**Dynamic programming** is the systematic version of that idea. It applies to problems with two properties.

**Overlapping subproblems.** Breaking the problem down produces the same smaller problems many times over.

**Optimal substructure.** The best answer to the whole problem can be built from the best answers to its subproblems.

There are two ways to write it. Top-down is recursion plus memoization, as above. Bottom-up fills a table of answers starting from the smallest subproblems, so that each entry only needs entries already filled in. Bottom-up avoids deep recursion and is usually faster. Top-down only computes the subproblems it actually needs.

Designing a dynamic programming solution follows the same steps every time. Define what one subproblem is, in words. Write how its answer follows from smaller subproblems. Identify the base cases. Choose an order that fills smaller subproblems first. Read off the final answer, and, if you need to know which choices produced it, walk back through the table.

### Edit distance

The **edit distance** between two strings, also called Levenshtein distance, is the fewest single-character insertions, deletions and substitutions that turn one into the other. "kitten" to "sitting" is 3: substitute k with s, substitute e with i, insert g.

It powers fuzzy matching: suggesting "Heathrow" when someone types "Heatrow", matching passenger names with typing mistakes, or spotting near-duplicate records.

The subproblem is: the distance between the first *i* characters of one string and the first *j* of the other. If the characters at *i* and *j* match, the answer is the same as for *i* − 1 and *j* − 1. Otherwise it is one more than the best of three options: delete, insert or substitute. The base cases are an empty string against a string of length *k*, which costs *k*. Filling the table costs O(n × m) for strings of length *n* and *m*.

A close relative, the longest common subsequence, is the basis of diff tools that show what changed between two files.

### The knapsack problem

The **knapsack problem** asks which items to pack into a limited capacity to get the most value. It turns up as cargo loading under a weight limit, choosing projects within a budget, or picking ads for a time slot.

The subproblem is: the best value using only the first *i* items with capacity *c*. For each item there are two choices. Leave it out, and the best value is the same as without it. Or, if it fits, take it, and add its value to the best value for the remaining capacity without it. The table has one row per item and one column per unit of capacity, so the cost is O(n × W), where W is the capacity.

That cost depends on the size of the capacity number, not just on how many items there are. With a capacity in whole tonnes, it is fast. With a capacity in grams, the table is a million times wider. Algorithms whose cost grows with the value of a number, rather than its length, are called pseudo-polynomial.

```csharp
// dp.cs — run with: dotnet run dp.cs
using System.Linq;

// 1. Repeated work, and remembering it
int calls = 0;
int Fib(int n) { calls++; return n < 2 ? n : Fib(n - 1) + Fib(n - 2); }
Console.WriteLine($"{Fib(30)} in {calls:N0} calls");          // 832040 in 2,692,537 calls

var memo = new Dictionary<int, long>();
int computed = 0;
long FibMemo(int n)
{
    if (n < 2) return n;
    if (memo.TryGetValue(n, out long known)) return known;
    computed++;
    return memo[n] = FibMemo(n - 1) + FibMemo(n - 2);
}
Console.WriteLine($"{FibMemo(30)} with {computed} computations");   // 832040 with 29 computations
Console.WriteLine(FibBottomUp(90));                                 // 2880067194370816120: no recursion at all

// 2. Recursive data: nested containers
var pallet = new Box("pallet", 25, [
    new Box("crate A", 10, [new Box("parts", 40, []), new Box("tools", 15, [])]),
    new Box("crate B", 12, [new Box("manuals", 5, [])]),
]);
Console.WriteLine(TotalRecursive(pallet));   // 107
Console.WriteLine(TotalWithStack(pallet));   // 107: same answer, no risk of stack overflow

// 3. Edit distance for fuzzy matching
string typed = "heatrow";
string[] airports = ["Heathrow", "Gatwick", "Stansted", "Luton"];
Console.WriteLine(airports.MinBy(a => EditDistance(typed, a.ToLowerInvariant())));   // Heathrow
Console.WriteLine(EditDistance("kitten", "sitting"));                                // 3

// 4. Knapsack: most valuable cargo within 10 tonnes
(string Name, int Tonnes, int Value)[] cargo =
    [("mail", 2, 3), ("medical", 3, 10), ("machinery", 6, 12), ("perishables", 4, 8), ("documents", 1, 2)];
var (value, chosen) = Knapsack(cargo, capacity: 10);
Console.WriteLine($"{value}: {string.Join(", ", chosen)}");   // 24: medical, machinery, documents

// 5. Greedy versus dynamic programming: fill 6 units with containers of size 1, 3 and 4
int[] sizes = [1, 3, 4];
Console.WriteLine($"greedy: {Greedy(sizes, 6)} containers, best: {Fewest(sizes, 6)}");   // greedy: 3 containers, best: 2

// 6. Backtracking: the shortest round trip from LHR through every other airport
var km = new Dictionary<(string, string), int>
{
    [("LHR", "JFK")] = 5540, [("LHR", "DXB")] = 5470, [("LHR", "SIN")] = 10880,
    [("JFK", "DXB")] = 11020, [("JFK", "SIN")] = 15340, [("DXB", "SIN")] = 5840,
};
int Dist(string a, string b) => km.TryGetValue((a, b), out int d) ? d : km[(b, a)];
string[] stops = ["JFK", "DXB", "SIN"];
var (bestKm, bestTour) = ShortestTour("LHR", stops, Dist);
Console.WriteLine($"{string.Join(" → ", bestTour)}: {bestKm:N0} km");   // LHR → JFK → SIN → DXB → LHR: 32,190 km

static long FibBottomUp(int n)
{
    long a = 0, b = 1;                         // the table shrunk to the two entries still needed
    for (int i = 0; i < n; i++) (a, b) = (b, a + b);
    return a;
}

static int TotalRecursive(Box box) => box.Own + box.Inside.Sum(TotalRecursive);

static int TotalWithStack(Box root)
{
    int total = 0;
    var pending = new Stack<Box>();
    pending.Push(root);
    while (pending.TryPop(out Box? box))
    {
        total += box.Own;
        foreach (Box inner in box.Inside) pending.Push(inner);
    }
    return total;
}

static int EditDistance(string a, string b)
{
    var d = new int[a.Length + 1, b.Length + 1];   // d[i, j]: distance between a[..i] and b[..j]
    for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
    for (int j = 0; j <= b.Length; j++) d[0, j] = j;
    for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            int substitute = d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
            d[i, j] = Math.Min(substitute, Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1));   // delete, insert
        }
    return d[a.Length, b.Length];
}

static (int Value, List<string> Chosen) Knapsack((string Name, int Tonnes, int Value)[] items, int capacity)
{
    var best = new int[items.Length + 1, capacity + 1];   // best[i, c]: first i items, capacity c
    for (int i = 1; i <= items.Length; i++)
        for (int c = 0; c <= capacity; c++)
        {
            best[i, c] = best[i - 1, c];                                          // leave item i out
            var (_, w, v) = items[i - 1];
            if (w <= c) best[i, c] = Math.Max(best[i, c], best[i - 1, c - w] + v); // or take it
        }
    var chosen = new List<string>();
    for (int i = items.Length, c = capacity; i > 0; i--)                          // walk back through the choices
        if (best[i, c] != best[i - 1, c]) { chosen.Insert(0, items[i - 1].Name); c -= items[i - 1].Tonnes; }
    return (best[items.Length, capacity], chosen);
}

static int Greedy(int[] sizes, int amount)
{
    int count = 0;
    foreach (int s in sizes.OrderDescending())
        while (amount >= s) { amount -= s; count++; }
    return count;
}

static int Fewest(int[] sizes, int amount)
{
    var fewest = new int[amount + 1];              // fewest[x]: fewest containers that fill exactly x
    for (int x = 1; x <= amount; x++)
    {
        fewest[x] = int.MaxValue;
        foreach (int s in sizes)
            if (s <= x && fewest[x - s] != int.MaxValue)
                fewest[x] = Math.Min(fewest[x], fewest[x - s] + 1);
    }
    return fewest[amount];
}

static (int Km, List<string> Tour) ShortestTour(string home, string[] stops, Func<string, string, int> dist)
{
    int bestKm = int.MaxValue;
    List<string> bestTour = [];
    var route = new List<string> { home };
    var used = new bool[stops.Length];
    Extend(0);
    return (bestKm, bestTour);

    void Extend(int soFar)
    {
        if (soFar >= bestKm) return;                              // prune: already worse than the best tour
        if (route.Count == stops.Length + 1)
        {
            int total = soFar + dist(route[^1], home);
            if (total < bestKm) { bestKm = total; bestTour = [.. route, home]; }
            return;
        }
        for (int i = 0; i < stops.Length; i++)
        {
            if (used[i]) continue;
            used[i] = true; route.Add(stops[i]);                  // choose
            Extend(soFar + dist(route[^2], stops[i]));            // explore
            route.RemoveAt(route.Count - 1); used[i] = false;     // undo
        }
    }
}

record Box(string Name, int Own, List<Box> Inside);
```

The memoized Fibonacci computes 29 values rather than 31, because 0 and 1 are base cases that are never stored.

### Greedy algorithms

A **greedy algorithm** makes the choice that looks best right now, never reconsiders it, and moves on. It is simple and fast. Dijkstra's algorithm from Graphs (3.6) is greedy, and so is the gate check in Searching and sorting (3.7), which always reuses the gate that frees up first. For some problems, greedy is provably optimal.

For others, it is plainly wrong, as the containers example shows. With sizes 1, 3 and 4, greedy fills 6 units as 4 + 1 + 1. The best answer is 3 + 3. The first choice, taking the biggest container, ruled out the better combination. Dynamic programming considers every way to fill each smaller amount and doesn't fall into that trap.

Before using a greedy approach, look for a proof that it works, or at least try hard to find a counterexample.

### Backtracking

Some problems are searches through combinations: arrange, assign or choose items so that every rule holds. **Backtracking** builds a candidate one step at a time. At each step it makes a choice, explores further, and then undoes the choice to try the next one. When a partial candidate can't possibly lead to a valid or better answer, it stops exploring it. That is **pruning**.

`ShortestTour` above follows the choose, explore, undo pattern, and prunes any partial route already longer than the best complete tour found so far. Sudoku solvers, schedulers with constraints and puzzle solvers work the same way. Good pruning can cut the search enormously, but in the worst case backtracking still tries every combination.

### Hard problems

For some problems, nobody knows an algorithm that is fast on every input. The travelling salesman problem is one: the shortest round trip through *n* places. So are general bin packing, many scheduling problems and assigning crews to flights under all the rules on rest, qualifications and base locations.

These problems are called **NP-hard**. Informally, every known exact algorithm takes exponential time in the worst case, and finding a fast one would also give fast solutions to thousands of other problems that have resisted everyone for decades. Most computer scientists believe no fast algorithm exists.

That doesn't make them hopeless. Small instances can be solved exactly with backtracking or dynamic programming, as the tour example does with three stops. Heuristics find good answers quickly without proving they are best. Approximation algorithms guarantee an answer within some factor of the best. And specialized solvers for mixed-integer programming and constraint programming handle surprisingly large real instances.

Airlines have relied on this field, called **operations research**, for decades: to build crew pairings and rosters, assign aircraft to routes, plan maintenance and recover schedules after disruption. Libraries such as Google OR-Tools, which has a .NET API, bring these solvers within reach of ordinary developers.

### In short

A recursive function needs a base case and must shrink the problem on every call; C# doesn't guarantee tail calls, so deep recursion needs a loop with an explicit stack. Memoization stores the results of pure functions so repeated work disappears. Dynamic programming builds answers from stored answers to smaller subproblems, as in edit distance and knapsack. Greedy choices are fast but sometimes wrong. Some problems are NP-hard, and real systems use solvers and heuristics for them.

### What goes wrong

**Missing or unreachable base cases.** The recursion never stops, and the process dies with a stack overflow. Check that every recursive call makes the problem strictly smaller.

**Deep recursion on real data.** A recursive walk that works in tests crashes on a folder tree or chain that is 100,000 levels deep. Use an explicit stack when depth isn't bounded.

**Exponential recursion.** A clean recursive solution recomputes the same subproblems and takes hours. Look for repeated arguments and add memoization or a table.

**Memoizing impure functions.** Caching results of a function that reads the clock, a database or shared state returns stale answers. Only memoize pure functions, or give the cache an expiry.

**Unbounded memo tables.** A memo dictionary for a long-running service grows forever. Bound it, or scope it to one operation.

**Trusting a greedy shortcut.** A greedy scheduler or packer looks right on examples and gives poor results on real data. Prove it, or compare with an exact method on small cases.

**Brute force on hard problems.** A search that finishes instantly for 8 items never finishes for 25. Estimate the number of combinations before writing the code, and use a solver or heuristic when it is astronomical.

### Cost model

A recursive call costs a few nanoseconds plus stack space, typically tens to a few hundred bytes per frame. Dynamic programming costs roughly the number of table cells times the work per cell: O(n × m) for edit distance, O(n × W) for knapsack. A 1,000 × 1,000 table of `int` is 4 MB. Brute-force search over orderings grows factorially: 10 stops have 3.6 million orderings, 15 have 1.3 trillion, 20 have 2.4 × 10¹⁸. Pruning and memoization change the constant and often the practical limit, but not the worst case.

### In other languages (optional)

**JavaScript and TypeScript.** The call stack is limited, typically around 10,000 frames, and deep recursion throws `RangeError: Maximum call stack size exceeded`. Tail-call optimization is in the language standard but only Safari implements it.

**Python.** The default recursion limit is 1,000 frames. `functools.cache` adds memoization to any pure function with one decorator.

**F#, Haskell and Scheme.** Rely on tail calls for loops, so deep tail recursion is normal and safe.

**SQL.** Recursive common table expressions express recursion over rows. SQL Server stops after 100 levels by default, changeable with `OPTION (MAXRECURSION n)`.

### Key terms

- **Recursion**: A function solving a problem by calling itself on smaller versions of it.
- **Base case**: The case a recursive function answers directly, without recursing.
- **Recursive case**: The case that breaks the problem down and calls the function on the smaller pieces.
- **Call stack**: The stack of frames for all method calls currently in progress.
- **Tail call**: A call that is the last thing a function does, which some languages turn into a jump.
- **Divide and conquer**: Splitting a problem into independent parts, solving each, and combining the results.
- **Memoization**: Remembering a function's results so repeated calls with the same arguments return instantly.
- **Dynamic programming**: Solving a problem by combining stored answers to overlapping smaller subproblems.
- **Overlapping subproblems**: The same smaller problems appearing many times when a problem is broken down.
- **Optimal substructure**: The property that the best answer is built from best answers to subproblems.
- **Edit distance**: The fewest single-character insertions, deletions and substitutions between two strings.
- **Knapsack problem**: Choosing items with weights and values to get the most value within a capacity.
- **Greedy algorithm**: An algorithm that always takes the locally best choice and never reconsiders it.
- **Backtracking**: Searching by making a choice, exploring, and undoing it to try the next choice.
- **Pruning**: Abandoning a partial search path that can't lead to a valid or better answer.
- **NP-hard**: A class of problems with no known algorithm that is fast on every input.
- **Operations research**: The field that applies optimization and solvers to planning and scheduling problems.

### Where this leads

This ends Part 3. Part 4 moves from algorithms to the structure of programs, starting with Procedural, object-oriented and functional styles (4.1). Pure functions, which make memoization safe, are in Immutability and pure functions (4.4). Running divide-and-conquer work on many cores is in Parallelism (5.5). Recursive queries over rows are in The relational model and SQL (7.2). Caching results across a whole system is in Caching (8.6).

### Check yourself

1. Write a memoized top-down version of `Fewest` in `dp.cs` and check it gives the same answers as the table version for amounts from 1 to 50.
2. Extend `EditDistance` to also return the list of edits needed, by walking back through the table.
3. Add a fifth cargo item to `Knapsack` and a capacity of 12. Predict the answer by hand, then check it.
4. Add DXB and SIN distances to AKL and include AKL as a fourth stop in `ShortestTour`. Count how many complete tours are checked with and without pruning.
5. Write the longest common subsequence of two lists of strings, and use it to print which lines were added and removed between two versions of a short file.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Fuzzy matching passenger names

Check-in must match "Nguyen Van Nam" against a manifest that says "Nguyen Van Name".

*Edit distance with two rows*

```csharp
static int Levenshtein(string a, string b)
{
    var prev = new int[b.Length + 1];
    var cur = new int[b.Length + 1];
    for (int j = 0; j <= b.Length; j++) prev[j] = j;
    for (int i = 1; i <= a.Length; i++)
    {
        cur[0] = i;
        for (int j = 1; j <= b.Length; j++)
        {
            int cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
            cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
        }
        (prev, cur) = (cur, prev);
    }
    return prev[b.Length];   // 1 here: a likely match worth flagging for an agent
}
```

> **Lesson:** Dynamic programming builds the answer from smaller stored answers; keeping only two rows makes it cheap.

#### Example 2: Walking a deep category tree without a stack overflow

A recursive function walks product categories. One malformed import created a chain 200,000 deep and crashed the process.

*Explicit stack plus a visited set*

```csharp
IEnumerable<Category> Descendants(Category root)
{
    var stack = new Stack<Category>([root]);
    var seen = new HashSet<int>();
    while (stack.TryPop(out var c))
    {
        if (!seen.Add(c.Id)) continue;          // also stops cycles
        yield return c;
        foreach (var child in c.Children) stack.Push(child);
    }
}
```

> **Lesson:** Stack overflow can't be caught. For data of unbounded depth, use a loop with an explicit stack.
