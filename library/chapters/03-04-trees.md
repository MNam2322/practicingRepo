---
id: "3-4"
number: "3.4"
title: "Trees"
part: 3
part_title: "Data structures and algorithms"
words: 5297
minutes: 23
---

# 3.4 Trees

> **Quick look.** Trees keep data in order, from SortedSet to the B-trees inside every database index.
>
> - A binary search tree discards half the data per step, but only while balanced; sorted input turns it into a chain.
> - SortedSet and SortedDictionary are self-balancing red-black trees.
> - SortedList is better when you build once and read often; SortedDictionary when inserts and reads interleave.
> - Trees can be walked depth-first (pre, in, post order) or level by level.
> - Databases use wide, shallow B-trees so a lookup takes a handful of disk reads.
> - Tries answer prefix queries (autocomplete) in time proportional to the key length.


### The big picture

Hash tables are fast, but they keep no order. When you need data sorted, or everything between two values, you need a tree: a structure that branches like a family tree turned upside down.

Trees are everywhere: folders on a disk, the elements of a web page, the structure of your code, and every database index. This chapter builds a binary search tree and shows how it can quietly degrade into a slow chain, how balanced trees prevent that, and how the wide trees inside databases keep a lookup to a handful of disk reads.

### Before you start

**Builds on:** 2.1 What a programming language is, 1.5 Memory.

**Ideas explained later:**

- **Database index** (7.3): A separate sorted structure, usually a B+ tree, holding some columns with pointers back to rows.
- **Binary search** (3.7): Finding a value in sorted data by repeatedly halving the range. O(log n).
- **HTML** (9.1): HyperText Markup Language: the language that describes a page's content and structure.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Schema** (7.5): The structure of a database: its tables, columns, types, keys, constraints and indexes.

**Tools and technologies named here:**

- **SQL Server** (7.2): Microsoft's relational database product.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, collection expressions, generic types, lambdas, LINQ, null operators, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What rule makes a binary tree a binary search tree?**

> For every node, all values in its left subtree are smaller and all values in its right subtree are larger. Searching then goes left or right at each step, like a binary search.

**2. You insert 1, 2, 3, ... 1000 into a plain binary search tree. What happens?**

> Each new value is larger than everything before, so it always goes to the right. The tree becomes a chain 1,000 nodes tall, and every operation becomes O(n). Self-balancing trees prevent this.

**3. What structure is behind SortedSet<T> and SortedDictionary<TKey, TValue>?**

> A red-black tree, a kind of self-balancing binary search tree. Add, remove and lookup are O(log n), and enumerating gives keys in order.

**4. Is SortedList<TKey, TValue> a tree too?**

> No. It is two sorted arrays. Lookups use binary search and are O(log n), but inserting into the middle shifts elements and is O(n). It uses less memory than SortedDictionary.

**5. What does an in-order traversal of a binary search tree produce?**

> All the values in sorted order. It visits the left subtree, then the node, then the right subtree.

**6. Why do databases use B-trees instead of binary trees for indexes?**

> Each B-tree node holds hundreds of keys and fits one disk page. The tree stays very shallow, three or four levels for millions of rows, so a lookup reads only a few pages.

**7. What is a trie good for?**

> Finding keys by prefix. Each level of the tree is one character, so all keys starting with "LH" sit under one node. Autocomplete and routing tables use tries.

### What a tree is

A **tree** is a structure of nodes connected like a family tree turned upside down. One node at the top is the **root**. Every other node has exactly one parent. A node can have any number of children. A node with no children is a **leaf**.

There are no loops. Following parents upward from any node always ends at the root, by exactly one path.

Some words describe positions and sizes. A node's **depth** is how many steps it is from the root. The tree's **height** is the number of nodes on its longest path from root to leaf. A **subtree** is any node together with everything below it; every subtree is a tree in its own right. That last fact is why so much tree code is recursive.

Trees are everywhere in software. Folders on a disk form a tree. So do HTML elements in a web page, the nested objects of a JSON document, the syntax tree of a program from What a programming language is (2.1), an organization chart and a product category hierarchy.

### Binary trees

A **binary tree** is a tree where each node has at most two children, called left and right. Binary trees are the building block for most of the fast, ordered structures in this chapter.

### Binary search trees

A **binary search tree** (BST) adds one rule. For every node, every value in its left subtree is smaller, and every value in its right subtree is larger.

That rule makes searching fast. Start at the root. If the value you want is smaller, go left. If larger, go right. Each step discards a whole subtree. Inserting works the same way: search for the value, and put it where the search fell off the tree.

The cost of every operation is proportional to the height of the tree. The big question is how tall the tree is.

### Balance

If values arrive in random order, a BST stays fairly bushy. Its height is around 2 to 3 times log₂ *n*, so a thousand values give a height in the low twenties, and operations are O(log n).

If values arrive in sorted order, it goes badly wrong. Every new value is the largest so far and goes to the right of the last one. The tree becomes a single chain, as tall as the number of values. Every operation is O(n): a linked list with extra steps. Sorted input is common in real data, such as ids, timestamps and imported files, so this isn't a rare case.

A **balanced tree** keeps its height close to log₂ *n* whatever the input order. Self-balancing trees check the shape after each insert or delete and fix it with **rotations**: small local rearrangements that lift one node and lower another while keeping the search order.

Two designs dominate. **AVL trees** keep the heights of every node's two subtrees within one of each other. They are strictly balanced, which makes lookups fast and updates slightly slower. A **red-black tree** colours each node red or black and follows a few colouring rules that keep the longest path at most twice the shortest. It rebalances less often. .NET's `SortedSet<T>` and `SortedDictionary<TKey, TValue>` are red-black trees.

```csharp
// trees.cs — run with: dotnet run trees.cs
using System.Linq;

// 1. Height depends on insertion order
var shuffled = new Bst();
foreach (int x in Enumerable.Range(0, 1000).OrderBy(_ => Random.Shared.Next())) shuffled.Insert(x);
var sorted = new Bst();
foreach (int x in Enumerable.Range(0, 1000)) sorted.Insert(x);
Console.WriteLine($"height, random order: {shuffled.Height()}");   // usually in the low twenties
Console.WriteLine($"height, sorted order: {sorted.Height()}");     // 1000: a chain
Console.WriteLine(shuffled.Contains(417));                         // True

// 2. Four ways to walk the same tree
var small = new Bst();
foreach (int x in new[] { 50, 30, 70, 20, 40, 60, 80 }) small.Insert(x);
Console.WriteLine(string.Join(",", small.InOrder()));     // 20,30,40,50,60,70,80: sorted
Console.WriteLine(string.Join(",", small.PreOrder()));    // 50,30,20,40,70,60,80: parents first
Console.WriteLine(string.Join(",", small.PostOrder()));   // 20,40,30,60,80,70,50: children first
Console.WriteLine(small.Levels());                        // 50 | 30 70 | 20 40 60 80

// 3. A balanced tree from the library, with a range query
var departures = new SortedSet<TimeOnly> { new(9, 15), new(7, 40), new(12, 5), new(10, 30), new(8, 55) };
Console.WriteLine(departures.Min.ToString("HH:mm"));                         // 07:40
var morning = departures.GetViewBetween(new TimeOnly(8, 0), new TimeOnly(11, 0));
Console.WriteLine(string.Join(", ", morning.Select(t => t.ToString("HH:mm"))));   // 08:55, 09:15, 10:30

// 4. A trie finds every key with a prefix
var trie = new Trie();
foreach (string code in new[] { "LHR", "LGW", "LCY", "LTN", "JFK", "LAX" }) trie.Add(code);
Console.WriteLine(string.Join(",", trie.StartingWith("L")));    // LAX,LCY,LGW,LHR,LTN
Console.WriteLine(string.Join(",", trie.StartingWith("LH")));   // LHR
Console.WriteLine(trie.StartingWith("X").Count);                // 0

sealed class Bst
{
    sealed class Node(int value) { public int Value = value; public Node? Left, Right; }
    Node? root;

    public void Insert(int value)
    {
        if (root is null) { root = new Node(value); return; }
        Node current = root;
        while (true)                                   // a loop, not recursion: no stack overflow on a chain
        {
            if (value == current.Value) return;        // no duplicates
            if (value < current.Value)
            {
                if (current.Left is null) { current.Left = new Node(value); return; }
                current = current.Left;
            }
            else
            {
                if (current.Right is null) { current.Right = new Node(value); return; }
                current = current.Right;
            }
        }
    }

    public bool Contains(int value)
    {
        Node? current = root;
        while (current is not null)
        {
            if (value == current.Value) return true;
            current = value < current.Value ? current.Left : current.Right;
        }
        return false;
    }

    public int Height() => HeightOf(root);
    static int HeightOf(Node? n) => n is null ? 0 : 1 + Math.Max(HeightOf(n.Left), HeightOf(n.Right));

    public IEnumerable<int> InOrder()                  // explicit stack instead of recursion
    {
        var stack = new Stack<Node>();
        Node? current = root;
        while (current is not null || stack.Count > 0)
        {
            while (current is not null) { stack.Push(current); current = current.Left; }
            current = stack.Pop();
            yield return current.Value;
            current = current.Right;
        }
    }

    public List<int> PreOrder()
    {
        var result = new List<int>();
        Visit(root);
        return result;
        void Visit(Node? n) { if (n is null) return; result.Add(n.Value); Visit(n.Left); Visit(n.Right); }
    }

    public List<int> PostOrder()
    {
        var result = new List<int>();
        Visit(root);
        return result;
        void Visit(Node? n) { if (n is null) return; Visit(n.Left); Visit(n.Right); result.Add(n.Value); }
    }

    public string Levels()                             // breadth first, using a queue
    {
        if (root is null) return "";
        var levels = new List<string>();
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            int width = queue.Count;
            var level = new List<int>();
            for (int i = 0; i < width; i++)
            {
                Node n = queue.Dequeue();
                level.Add(n.Value);
                if (n.Left is not null) queue.Enqueue(n.Left);
                if (n.Right is not null) queue.Enqueue(n.Right);
            }
            levels.Add(string.Join(" ", level));
        }
        return string.Join(" | ", levels);
    }
}

sealed class Trie
{
    sealed class Node { public SortedDictionary<char, Node> Children = new(); public bool IsWord; }
    readonly Node root = new();

    public void Add(string word)
    {
        Node n = root;
        foreach (char c in word)
        {
            if (!n.Children.TryGetValue(c, out Node? next)) n.Children[c] = next = new Node();
            n = next;
        }
        n.IsWord = true;
    }

    public List<string> StartingWith(string prefix)
    {
        Node n = root;
        foreach (char c in prefix)
            if (!n.Children.TryGetValue(c, out Node? next)) return [];
            else n = next;
        var results = new List<string>();
        Collect(n, prefix);
        return results;

        void Collect(Node node, string soFar)
        {
            if (node.IsWord) results.Add(soFar);
            foreach (var (c, child) in node.Children) Collect(child, soFar + c);
        }
    }
}
```

### Walking a tree

A **tree traversal** visits every node once. There are two families.

**Depth-first** traversals go as deep as possible before backing up. The three orders differ only in when a node is handled compared with its children. **Pre-order** handles the node first, then its children. Copying a tree or printing a folder listing works this way. An **in-order traversal** handles the left subtree, then the node, then the right subtree. On a binary search tree this gives every value in sorted order. **Post-order** handles the children first, then the node. Deleting a folder must delete its contents first, and evaluating an expression tree must compute both sides of `*` before multiplying, so both use post-order.

**Breadth-first**, also called **level-order traversal**, visits all nodes at depth 0, then depth 1, and so on. It uses a queue: take a node from the front, handle it, add its children to the back.

Depth-first code is naturally recursive, as `PreOrder` shows. On a very deep tree, recursion can overflow the stack, as Memory (1.5) warned. An explicit `Stack<T>` does the same job in a loop, as `InOrder` shows. The same stack-versus-queue split reappears in Graphs (3.6).

### B-trees

Binary trees are a poor fit for data on disk. Each node is a separate read, and a balanced binary tree over a million keys is 20 levels deep. Twenty random disk reads per lookup is far too slow.

A **B-tree** makes each node wide. One node holds hundreds of keys, in sorted order, with a child pointer between each pair. A node is sized to one disk page, 8 KB in SQL Server. With hundreds of children per node, a tree over millions of rows is only three or four levels deep. The top levels are read so often that they stay in memory, so a lookup often costs one or two actual reads.

Most databases use a variant called a **B+ tree**, where all the data sits in the leaves and the leaves are linked in order. Finding the start of a range and then walking the leaves makes range queries such as "all flights between 8 and 11" efficient. Every ordinary SQL index is a B+ tree, as Indexes and query plans (7.3) explains. File systems such as NTFS and APFS use them too.

### Tries

A **trie**, from "retrieval" and usually said "try", stores strings by their characters. Each edge from a node is one character, and the path from the root to a node spells a prefix. All keys that share a prefix share the nodes for it.

Looking up a key costs time proportional to its length, not to how many keys are stored. Finding all keys with a prefix means walking to the prefix's node and collecting everything below it. Autocomplete, spell-checkers and network routers, which match addresses by prefix, all use tries or compressed forms of them.

### Expression trees

The syntax tree from What a programming language is (2.1) has a special role in .NET. When a lambda is assigned to an `Expression<Func<...>>` instead of a `Func<...>`, the compiler doesn't compile it into code. It builds an **expression tree**: a data structure describing the lambda, node by node.

That is how EF Core works. `db.Flights.Where(f => f.Delay > 60)` hands EF Core a tree it can walk and translate into SQL, instead of a compiled function it could only call. It is also why some C# expressions work in memory but can't be translated, as covered in ORMs and EF Core (7.7).

### Trees for location

Ordinary trees order values along one line. Location data has two or more dimensions, so "all airports within 200 km" needs a different kind of tree. Quadtrees split the map into four quarters again and again. R-trees group nearby objects into nested rectangles. Structures like these are called a **spatial index**. SQL Server's `geography` type and PostgreSQL's PostGIS extension use them for distance and area queries.

### Storing trees in a database

Relational tables are flat, so trees need a convention. The simplest is a `ParentId` column pointing at another row of the same table. Queries that walk the whole tree use recursive common table expressions (`WITH RECURSIVE` in PostgreSQL, `WITH` in SQL Server). SQL Server also has a `hierarchyid` type. Other designs store the full path in each row, such as `/1/4/17/`, or keep a separate table of every ancestor and descendant pair. Each trades easy reads against easy updates, as Schema design and migrations (7.5) discusses.

### In short

A binary search tree keeps smaller values to the left and larger to the right, so each step discards half the tree, but only while it stays balanced. Sorted input turns a plain one into a chain. `SortedSet` and `SortedDictionary` are self-balancing red-black trees. Trees can be walked depth-first, visiting parents before, between or after children, or level by level. Databases use wide, shallow B-trees for indexes, and tries find keys by prefix.

### What goes wrong

**Unbalanced trees from sorted input.** A hand-written BST fed ids in order becomes a chain, and everything becomes O(n). Use `SortedSet`, `SortedDictionary`, or a hash table if order doesn't matter.

**Choosing `SortedList` for frequent inserts.** Inserting into the middle shifts arrays and costs O(n). Use `SortedDictionary` for data that changes, and `SortedList` for data built once and read often.

**Deep recursion on deep trees.** Walking a very deep folder tree or a long chain recursively ends in a `StackOverflowException`, which kills the process. Use an explicit stack.

**Sorting after every insert.** Keeping a `List<T>` sorted by calling `Sort` after each `Add` costs O(n log n) per insert. Use a sorted structure, or insert at the position found by binary search.

**Cycles in data that should be a tree.** A category whose parent chain loops back to itself makes recursive code run forever. Validate parent links, and track visited nodes when walking data you don't control.

**Lambdas that can't become SQL.** A `Where` using a custom C# method compiles fine but fails, or loads the whole table, at run time. Keep query expressions to what the provider can translate.

### Cost model

A balanced binary tree over a million keys is about 20 levels deep, and each level is usually a cache miss, so a lookup costs a few hundred nanoseconds to a microsecond. That is several times slower than a hash lookup, and the price of keeping order. Each tree node is a separate object: `SortedDictionary` uses roughly 40 or more bytes per entry beyond the key and value. A B-tree lookup in a database costs three or four page reads, most of them usually in memory. A trie lookup costs one step per character of the key.

### In other languages (optional)

**JavaScript and TypeScript.** There is no built-in sorted map or set. Libraries provide them, or you keep a sorted array and use binary search. The DOM is a tree, and walking it uses the same traversals.

**Python.** There is no built-in balanced tree. The `bisect` module inserts into sorted lists by binary search, and the third-party `sortedcontainers` package is widely used.

**Java.** `TreeMap` and `TreeSet` are red-black trees, like .NET's sorted collections.

**C++.** `std::map` and `std::set` are usually red-black trees.

**SQL.** Every normal index is a B+ tree. Recursive common table expressions walk tree-shaped data stored with parent ids.

### Key terms

- **Tree**: Nodes connected with no loops, where every node except the root has exactly one parent.
- **Leaf**: A tree node with no children.
- **Height**: The number of nodes on the longest path from a tree's root to a leaf.
- **Subtree**: A node together with everything below it.
- **Binary tree**: A tree where each node has at most two children, left and right.
- **Binary search tree**: A binary tree where every left subtree holds smaller values and every right subtree larger ones.
- **Balanced tree**: A tree whose height stays close to log n, whatever order values arrive in.
- **Rotation**: A small local rearrangement that rebalances a tree while keeping its order.
- **Red-black tree**: A self-balancing binary search tree that uses node colours to limit its height. Behind SortedSet and SortedDictionary.
- **Tree traversal**: Visiting every node of a tree once, in a defined order.
- **In-order traversal**: Visiting the left subtree, then the node, then the right subtree. Gives sorted order on a binary search tree.
- **Level-order traversal**: Visiting a tree level by level from the root, using a queue. Also called breadth-first.
- **B-tree**: A shallow tree with many keys per node, sized to disk pages. Used for database indexes.
- **Trie**: A tree that stores strings one character per level, so keys with a common prefix share nodes.
- **Expression tree**: A data structure describing a lambda's code, which libraries like EF Core translate into SQL.
- **Spatial index**: A tree that organizes data by location, for queries such as "everything within 200 km".

### Where this leads

A tree stored in an array is the heap in Heaps and priority queues (3.5). Trees are special graphs, and the traversals generalize in Graphs (3.6). Recursive tree algorithms return in Recursion and dynamic programming (3.8). B+ tree indexes are at the heart of Indexes and query plans (7.3). Expression trees turn into SQL in ORMs and EF Core (7.7). The browser's DOM tree is in How a browser shows a page (9.1).

### Check yourself

1. Add a `Min()` and a `Max()` method to `Bst` without recursion.
2. Add `Remove(int value)` to `Bst`. Handle the three cases: a leaf, a node with one child, and a node with two children.
3. Insert the ids 1 to 100,000 into `Bst` and into a `SortedSet<int>`. Time both, and explain the difference.
4. Add a `Count` to each trie node of how many words pass through it, and use it to answer "how many codes start with L?" without collecting them.
5. Write a method that prints a folder tree with indentation, first recursively, then with an explicit stack. Run both on a deep folder such as `node_modules`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Finding the next available departure

Given a time, find the first scheduled departure at or after it, thousands of times a second.

*A sorted set with a range view*

```csharp
var departures = new SortedSet<DateTimeOffset>(LoadDepartures());

DateTimeOffset? NextAfter(DateTimeOffset t)
{
    var view = departures.GetViewBetween(t, DateTimeOffset.MaxValue);   // O(log n)
    return view.Count > 0 ? view.Min : null;
}
```

*Or, for data that doesn't change: sorted array + binary search*

```csharp
var arr = LoadDepartures().Order().ToArray();
int i = Array.BinarySearch(arr, t);
if (i < 0) i = ~i;                 // index of the first element greater than t
var next = i < arr.Length ? arr[i] : (DateTimeOffset?)null;
```

> **Lesson:** When you need order or ranges, use a tree or a sorted array with binary search; hash tables can't answer "next after".

#### Example 2: Airport autocomplete

A search box should suggest airports as the user types "lo": London, Los Angeles, Lombok.

*Prefix search on a sorted list*

```csharp
var names = airports.Select(a => a.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray();

IEnumerable<string> Suggest(string prefix)
{
    int i = Array.BinarySearch(names, prefix, StringComparer.OrdinalIgnoreCase);
    if (i < 0) i = ~i;
    for (; i < names.Length && names[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase); i++)
        yield return names[i];
}
```

> **Lesson:** Sorted data makes prefix search cheap. Reach for a trie only when the data is huge or changes constantly.

### The Collections

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Collections</p>

<a id="be-b279"></a>

#### The sorted family

**`SortedDictionary<K,V>`** — a red-black tree. O(log n) for everything, and every node is a separate heap object, so it's memory-hungry and cache-hostile. Right when you insert and remove frequently while keeping order.

**`SortedList<K,V>`** — two parallel arrays, kept sorted. O(log n) binary-search lookup, but **O(n) insert** because it shifts. Much less memory and far better locality. Right when you build once and read often, or when n is small.

The choice is the same shape as the whole document: `SortedList` trades insert cost for memory and locality.

**`SortedSet<T>`** — a red-black tree of unique values, with `GetViewBetween` for range queries.

For a build-once-then-read workload, a **plain sorted `List<T>` with `BinarySearch`** frequently beats all three.

### Implementing Them Yourself

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Implementing Them Yourself</p>

<p class="pd">Understanding is easier from the inside. These are simplified but faithful.</p>

<a id="be-b286"></a>

#### Binary search tree with balance

``` csharp
public class Avl<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public TKey Key; public TValue Value;
        public Node? L, R;
        public int Height = 1;
    }

    private Node? _root;
    private static int H(Node? n) => n?.Height ?? 0;
    private static void Fix(Node n) => n.Height = 1 + Math.Max(H(n.L), H(n.R));
    private static int Balance(Node n) => H(n.L) - H(n.R);

    private static Node RotateRight(Node y)
    {
        var x = y.L!; y.L = x.R; x.R = y;
        Fix(y); Fix(x); return x;
    }
    private static Node RotateLeft(Node x)
    {
        var y = x.R!; x.R = y.L; y.L = x;
        Fix(x); Fix(y); return y;
    }

    public void Insert(TKey key, TValue value) => _root = Insert(_root, key, value);

    private static Node Insert(Node? n, TKey key, TValue value)
    {
        if (n is null) return new Node { Key = key, Value = value };
        int c = key.CompareTo(n.Key);
        if (c < 0) n.L = Insert(n.L, key, value);
        else if (c > 0) n.R = Insert(n.R, key, value);
        else { n.Value = value; return n; }

        Fix(n);
        int b = Balance(n);
        if (b > 1 && key.CompareTo(n.L!.Key) < 0) return RotateRight(n);          // LL
        if (b < -1 && key.CompareTo(n.R!.Key) > 0) return RotateLeft(n);          // RR
        if (b > 1) { n.L = RotateLeft(n.L!); return RotateRight(n); }             // LR
        if (b < -1) { n.R = RotateRight(n.R!); return RotateLeft(n); }            // RL
        return n;
    }
}
```

**Why balancing exists:** insert sorted data into an unbalanced BST and you get a linked list — O(n) lookups. Rotations keep height at O(log n). AVL is more strictly balanced than red-black (faster reads, more rotations on write); .NET's `SortedDictionary` uses red-black, which favours writes.

<a id="be-b287"></a>

#### Trie

``` csharp
public class Trie
{
    private sealed class Node
    {
        public Dictionary<char, Node> Next = new();
        public bool IsWord;
    }
    private readonly Node _root = new();

    public void Add(string word)
    {
        var n = _root;
        foreach (var c in word)
        {
            if (!n.Next.TryGetValue(c, out var next)) n.Next[c] = next = new Node();
            n = next;
        }
        n.IsWord = true;
    }

    public IEnumerable<string> WithPrefix(string prefix)
    {
        var n = _root;
        foreach (var c in prefix)
        {
            if (!n.Next.TryGetValue(c, out var next)) yield break;
            n = next;
        }
        foreach (var s in Collect(n, prefix)) yield return s;
    }

    private static IEnumerable<string> Collect(Node n, string acc)
    {
        if (n.IsWord) yield return acc;
        foreach (var (c, child) in n.Next)
            foreach (var s in Collect(child, acc + c))
                yield return s;
    }
}
```

Lookup is **O(length of key)** and independent of how many words are stored — that's the point. A dictionary can't do prefix queries at all without scanning everything.

The memory cost is real: a `Dictionary` per node is heavy. Production tries use arrays indexed by character, or compress single-child chains (a radix tree / PATRICIA trie).

### Collections

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Collections</p>

<a id="be-m103"></a>

#### SortedDictionary vs SortedList vs SortedSet

|                   | `SortedDictionary<K,V>` | `SortedList<K,V>`            | `SortedSet<T>` |
|-------------------|-------------------------|------------------------------|----------------|
| Structure         | red-black tree          | two sorted arrays            | red-black tree |
| Lookup by key     | O(log n)                | O(log n) binary search       | O(log n)       |
| Insert / Remove   | O(log n)                | **O(n)** (array shift)       | O(log n)       |
| Index by position | no                      | **yes**, O(1)                | no             |
| Memory per item   | node object + pointers  | just the array slots         | node object    |
| Enumeration       | in key order            | in key order, cache-friendly | in order       |

Choose `SortedList` when you build once and read many times, or when memory matters. Choose `SortedDictionary` when inserts and removals are interleaved with reads. Choose `SortedSet` when you need ordered uniqueness or range queries:

``` csharp
var s = new SortedSet<int>(new[] { 1, 5, 9, 12, 20 });
foreach (var x in s.GetViewBetween(5, 12)) Console.WriteLine(x); // 5, 9, 12
Console.WriteLine(s.Min + " " + s.Max);
```

> **\[Tip\]**
>
> **Often neither.** If you need "sorted" only for output, keep a `List<T>` or `Dictionary` and sort once at the end with `OrderBy`. A tree costs you on every single operation to save one O(n log n) pass.

### Structures you build yourself

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Structures you build yourself</p>

<a id="be-m118"></a>

#### Trie: prefix search and autocomplete

A trie stores strings by their characters, so every node is a prefix. Lookup and insert are O(length of the key) regardless of how many keys are stored, and "all keys starting with X" is a single subtree walk — something no hash-based structure can do at all.

``` csharp
public sealed class Trie {
    private sealed class Node {
        public Dictionary<char, Node>? Children;
        public bool IsTerminal;
        public int SubtreeCount;                    // enables "how many match this prefix"
    }
    private readonly Node _root = new();

    public void Add(ReadOnlySpan<char> word) {
        var node = _root;
        node.SubtreeCount++;
        foreach (var ch in word) {
            node.Children ??= new Dictionary<char, Node>(4);
            if (!node.Children.TryGetValue(ch, out var next))
                node.Children[ch] = next = new Node();
            node = next;
            node.SubtreeCount++;
        }
        node.IsTerminal = true;
    }

    private Node? Descend(ReadOnlySpan<char> prefix) {
        var node = _root;
        foreach (var ch in prefix) {
            if (node.Children is null || !node.Children.TryGetValue(ch, out var next))
                return null;
            node = next;
        }
        return node;
    }

    public bool Contains(ReadOnlySpan<char> word) => Descend(word) is { IsTerminal: true };
    public int CountWithPrefix(ReadOnlySpan<char> prefix) => Descend(prefix)?.SubtreeCount ?? 0;

    public IEnumerable<string> StartingWith(string prefix, int limit = 20) {
        var start = Descend(prefix);
        if (start is null) yield break;
        var stack = new Stack<(Node N, string S)>();
        stack.Push((start, prefix));
        while (stack.Count > 0 && limit > 0) {
            var (n, s) = stack.Pop();
            if (n.IsTerminal) { yield return s; limit--; }
            if (n.Children is null) continue;
            foreach (var (ch, child) in n.Children.OrderByDescending(k => k.Key))
                stack.Push((child, s + ch));            // reverse push = ascending output
        }
    }
}
```

> **\[Trap\]**
>
> **Memory is the catch.** A node per character with a `Dictionary` in each is enormously wasteful — a naive trie over a million words can cost hundreds of megabytes. Real implementations compress: a **radix tree** collapses single-child chains into one node holding a whole substring, and a **DAWG** additionally merges identical suffixes. Start with the simple version, measure, and compress only if it matters.

> **\[Tip\]**
>
> **Usually you do not need one.** For prefix search over a bounded set, a sorted array plus `LowerBound` ([Binary search and its variants](03-07-searching-and-sorting.md#be-m115)) gives you the same range in O(log n) with a fraction of the memory: all strings with prefix `P` occupy the contiguous range `[LowerBound(P), LowerBound(P + '\uffff'))`. Reach for a trie when you need incremental insertion, per-prefix counts, or fuzzy matching.
