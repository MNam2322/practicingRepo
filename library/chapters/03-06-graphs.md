---
id: "3-6"
number: "3.6"
title: "Graphs"
part: 3
part_title: "Data structures and algorithms"
words: 4895
minutes: 21
---

# 3.6 Graphs

> **Quick look.** Graphs model connections, and a few classic algorithms answer most questions about them.
>
> - A graph is vertices joined by edges, which may be directed and weighted; store most as adjacency lists.
> - Breadth-first search finds paths with the fewest edges; depth-first search answers reachability and cycle questions.
> - Dijkstra finds shortest weighted paths when no weight is negative; A* speeds it up with a good estimate.
> - Topological sort orders dependencies (builds, migrations) and detects cycles.
> - Always keep a visited set; union-find groups items transitively (customer de-duplication).


### The big picture

Many real problems are about connections: airports and routes, projects and their dependencies, build steps, database migrations, people and friendships. A graph is the general structure for such things: items, and links between them.

Once a problem is seen as a graph, a small set of well-known algorithms answers most questions about it. This chapter uses an airport route network to show how to find the route with the fewest legs and the route with the shortest distance. It also shows how to put tasks that depend on each other in the right order, and how to spot circular dependencies.

### Before you start

**Builds on:** 2.6 Scope and lifetime, 3.4 Trees, 1.5 Memory, 3.5 Heaps and priority queues.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.
- **Call stack** (3.8): The stack of frames for all method calls currently in progress.
- **Dynamic programming** (3.8): Solving a problem by combining stored answers to overlapping smaller subproblems.

**Tools and technologies named here:**

- **SQL Server** (7.2): Microsoft's relational database product.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **IP** (6.2): Internet Protocol: the protocol that addresses packets and routes them between networks.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, collection expressions, lambdas, LINQ, null operators, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between a tree and a graph?**

> A graph is any set of nodes connected by edges, possibly with loops and several paths between two nodes. A tree is a special graph: connected, with no cycles and exactly one path between any two nodes.

**2. Which search finds the route with the fewest legs, and which data structure does it use?**

> Breadth-first search, using a queue. It explores everything one step away, then two steps, and so on, so the first time it reaches the target is by the fewest edges.

**3. Why doesn't breadth-first search find the shortest route in kilometres?**

> It counts edges, not distance. Two long flights can be fewer legs but more kilometres than three short ones. Weighted edges need Dijkstra's algorithm or A*.

**4. What does Dijkstra's algorithm require of edge weights?**

> That none are negative. It assumes that once the closest unvisited node is taken from the priority queue, no later path can reach it more cheaply.

**5. What is a topological sort, and when does one exist?**

> An ordering of nodes where every edge points forward, such as build steps where each step comes after the steps it depends on. It exists only if the graph is a directed acyclic graph: no cycles.

**6. When is an adjacency matrix better than an adjacency list?**

> When the graph is dense, meaning most pairs of nodes are connected, or when you need to check "is there an edge from A to B?" in O(1). For sparse graphs, which most real ones are, the list uses far less memory.

**7. What makes A* faster than Dijkstra's algorithm?**

> A heuristic: an estimate of the remaining distance to the target, such as the straight-line distance. It steers the search toward the target instead of spreading equally in all directions. The estimate must never be larger than the true distance.

### What a graph is

A **graph** is a set of things and the connections between them. The things are **vertices**, also called nodes; one of them is a **vertex**. The connections are **edges**. That is the whole definition, and it describes a remarkable amount of software.

Airports and the routes between them form a graph. So do many other things. Projects in a solution reference each other, NuGet packages depend on other packages, and build steps and database migrations must run in order. Web pages link to each other, an order moves between states, and people connect in a social network. The dependency injection container from Scope and lifetime (2.6) builds a graph of services every time it starts.

Trees (3.4) were graphs with strict rules. General graphs drop those rules. A node can be reached by many paths. Paths can loop back on themselves.

### Kinds of graph

In an **undirected graph**, edges go both ways. A route between LHR and JFK can be flown in either direction. In a **directed graph**, each edge has a direction. "Project A references project B" doesn't mean B references A.

In a **weighted graph**, each edge carries a number: a distance, a cost, a flight time. In an unweighted graph, every edge counts the same.

A **cycle** is a path that returns to where it started. A directed graph with no cycles is a **directed acyclic graph**, or DAG. Dependency graphs must be DAGs: if A needs B, B needs C and C needs A, nothing can be built first.

A graph is **sparse** when each node connects to only a few others, and **dense** when most pairs are connected. Real graphs are almost always sparse. An airport connects to dozens of others, not thousands.

### Storing a graph

Three representations are common, with different costs. *V* is the number of vertices and *E* the number of edges.

An **adjacency list** keeps, for each node, a list of its neighbours. In C#, that is usually a `Dictionary<TNode, List<TNode>>`, or a list of tuples when edges have weights. It uses O(V + E) memory, and finding a node's neighbours costs only as much as there are neighbours. It is the default choice.

An **adjacency matrix** is a V × V grid where cell [a, b] says whether an edge goes from a to b, or holds its weight. Checking one edge is O(1). But it uses O(V²) memory, even for a sparse graph, and listing a node's neighbours means scanning a whole row. It suits small or dense graphs.

An **edge list** is just a list of (from, to, weight) records. It is how graphs usually arrive, from a file or a database table, and it is the input for some algorithms. Most code converts it into an adjacency list first.

### Breadth-first search

**Breadth-first search** (BFS) explores a graph in rings. Start at one node. Visit all its neighbours. Then all their unvisited neighbours. Then theirs. Like the level-order traversal in Trees (3.4), it uses a queue: take a node from the front, add its unvisited neighbours to the back.

Because it goes one ring at a time, the first time BFS reaches a node is by a path with the fewest possible edges. So BFS answers "fewest legs", "fewest hops" and "degrees of separation" questions.

Two details matter. Keep a set of visited nodes, or the search will go round cycles forever. And to recover the path, record for each node which node it was reached from, then walk those links back from the target.

BFS visits every node and edge at most once: O(V + E).

### Depth-first search

**Depth-first search** (DFS) goes as far as it can along one path before backing up to try another. It uses a stack, either an explicit `Stack<T>` or the call stack through recursion.

DFS doesn't find shortest paths. It is the tool for questions about structure. Can B be reached from A at all? Does the graph contain a cycle? Which nodes form separate islands? In what order can tasks run?

To detect a cycle in a directed graph, DFS marks each node as "in progress" when it starts exploring it and "done" when it finishes. Reaching a node that is still in progress means the path has looped back on itself.

DFS is also O(V + E). On very large or deep graphs, prefer an explicit stack to recursion, for the stack overflow reasons in Memory (1.5).

### Shortest paths with weights

When edges have weights, fewest edges no longer means shortest. Flying LHR to DXB to SYD is two legs. Flying LHR to SIN to SYD is also two legs and several hundred kilometres shorter. A **shortest path** is the one with the smallest total weight.

**Dijkstra's algorithm**, from 1956, solves this when no weight is negative. It keeps the best known distance to every node, starting with 0 for the source. It always takes the unvisited node with the smallest known distance next, using a priority queue from Heaps and priority queues (3.5). For each neighbour, it checks whether going through this node is shorter than the best route known so far, and if so records the new distance.

The key insight is that when a node comes out of the priority queue, its distance is final. Every other route to it would have to pass through a node that is already at least as far away, and adding non-negative weights can't make that shorter. That is also why negative weights break it. Graphs with negative weights need the slower Bellman-Ford algorithm.

With a binary heap and lazy deletion, Dijkstra's algorithm costs O((V + E) log V).

### A*: searching with a sense of direction

Dijkstra's algorithm spreads out evenly in every direction from the start, like a ripple. When you know roughly where the target is, that wastes work. **A\* search** adds a **heuristic**: an estimate of the remaining distance from each node to the target. It explores nodes in order of distance so far plus estimated distance left, so it heads toward the target.

For routes on a map, the straight-line distance is a good heuristic. On the globe it is the great-circle distance between two airports, computed from their latitude and longitude. The heuristic must never overestimate the true remaining distance. If it does, A* can miss the best route. Route planners, games and satellite navigation all use A* or its descendants.

```csharp
// graphs.cs — run with: dotnet run graphs.cs
using System.Linq;

var routes = new Graph();   // approximate great-circle distances in km
routes.AddRoute("LHR", "JFK", 5540);
routes.AddRoute("LHR", "DXB", 5470);
routes.AddRoute("LHR", "SIN", 10880);
routes.AddRoute("LHR", "LAX", 8760);
routes.AddRoute("JFK", "LAX", 3980);
routes.AddRoute("JFK", "DXB", 11020);
routes.AddRoute("DXB", "SIN", 5840);
routes.AddRoute("DXB", "SYD", 12040);
routes.AddRoute("SIN", "SYD", 6300);
routes.AddRoute("LAX", "SYD", 12050);

Console.WriteLine(string.Join(" → ", FewestLegs(routes, "LHR", "SYD")));    // LHR → DXB → SYD
var (km, path) = Shortest(routes, "LHR", "SYD");
Console.WriteLine($"{string.Join(" → ", path)}: {km:N0} km");             // LHR → SIN → SYD: 17,180 km
(km, path) = Shortest(routes, "JFK", "SIN");
Console.WriteLine($"{string.Join(" → ", path)}: {km:N0} km");             // JFK → LHR → SIN: 16,420 km
Console.WriteLine(Shortest(routes, "LHR", "HND").Km);                     // -1: not reachable

// Topological sort: build steps and what each depends on
var steps = new Dictionary<string, string[]>
{
    ["restore"] = [],
    ["lint"] = [],
    ["compile"] = ["restore"],
    ["test"] = ["compile", "lint"],
    ["docs"] = ["compile"],
    ["publish"] = ["test", "docs"],
};
Console.WriteLine(string.Join(", ", Order(steps)!));    // lint, restore, compile, test, docs, publish
steps["restore"] = ["publish"];                         // introduce a cycle
Console.WriteLine(Order(steps) is null ? "cycle: no valid order" : "ordered");   // cycle: no valid order

static List<string> FewestLegs(Graph g, string from, string to)
{
    var cameFrom = new Dictionary<string, string>();
    var seen = new HashSet<string> { from };
    var queue = new Queue<string>();
    queue.Enqueue(from);
    while (queue.Count > 0)
    {
        string here = queue.Dequeue();
        if (here == to) return PathTo(cameFrom, from, to);
        foreach (var (next, _) in g.Neighbours(here))
            if (seen.Add(next)) { cameFrom[next] = here; queue.Enqueue(next); }
    }
    return [];
}

static (int Km, List<string> Path) Shortest(Graph g, string from, string to)
{
    var best = new Dictionary<string, int> { [from] = 0 };
    var cameFrom = new Dictionary<string, string>();
    var frontier = new PriorityQueue<string, int>();
    frontier.Enqueue(from, 0);
    while (frontier.TryDequeue(out string? here, out int soFar))
    {
        if (soFar > best[here]) continue;          // stale entry: a shorter way here was already handled
        if (here == to) break;                     // its distance is final
        foreach (var (next, km) in g.Neighbours(here))
        {
            int candidate = soFar + km;
            if (!best.TryGetValue(next, out int known) || candidate < known)
            {
                best[next] = candidate;
                cameFrom[next] = here;
                frontier.Enqueue(next, candidate);  // lazy: the older entry stays and is skipped later
            }
        }
    }
    return best.TryGetValue(to, out int total) ? (total, PathTo(cameFrom, from, to)) : (-1, []);
}

static List<string> PathTo(Dictionary<string, string> cameFrom, string from, string to)
{
    var path = new List<string> { to };
    while (path[^1] != from) path.Add(cameFrom[path[^1]]);
    path.Reverse();
    return path;
}

static List<string>? Order(Dictionary<string, string[]> dependsOn)   // Kahn's algorithm
{
    var waitingOn = dependsOn.ToDictionary(kv => kv.Key, kv => kv.Value.Length);
    var dependents = dependsOn.Keys.ToDictionary(k => k, _ => new List<string>());
    foreach (var (step, needs) in dependsOn)
        foreach (string need in needs) dependents[need].Add(step);

    var ready = new Queue<string>(waitingOn.Where(kv => kv.Value == 0).Select(kv => kv.Key).Order());
    var order = new List<string>();
    while (ready.TryDequeue(out string? step))
    {
        order.Add(step);
        foreach (string next in dependents[step])
            if (--waitingOn[next] == 0) ready.Enqueue(next);
    }
    return order.Count == dependsOn.Count ? order : null;   // steps left over are stuck in a cycle
}

sealed class Graph
{
    readonly Dictionary<string, List<(string To, int Km)>> edges = new();

    public void AddRoute(string a, string b, int km) { Add(a, b, km); Add(b, a, km); }   // undirected

    void Add(string from, string to, int km)
    {
        if (!edges.TryGetValue(from, out var list)) edges[from] = list = [];
        list.Add((to, km));
    }

    public IEnumerable<(string To, int Km)> Neighbours(string node) =>
        edges.TryGetValue(node, out var list) ? list : [];
}
```

Look at the first two results. The fewest-legs route and the shortest route are different, and both are correct answers to different questions.

### Topological sort

A **topological sort** orders the nodes of a DAG so that every edge points forward. For build steps, it gives an order in which every step runs after the steps it depends on. Build systems, package managers, database migration tools, spreadsheet recalculation and DI containers all do this.

**Kahn's algorithm**, used above, is simple. Count how many unfinished dependencies each node has. Put every node with zero into a queue. Repeatedly take one out, add it to the order and reduce the count of everything that depends on it; any that reach zero join the queue. If nodes are left over at the end, they are waiting on each other in a cycle, and no valid order exists. Topological order is rarely unique. Sorting the ready nodes, as the example does, makes the result repeatable.

### Connected components

In an undirected graph, a **connected component** is a group of nodes that can all reach each other, with no path to anything outside the group. A route network might split into several components if some airports are only served by a regional airline with no connections.

Running BFS or DFS from every not-yet-visited node finds all components. When edges arrive one at a time and you keep asking "are these two connected yet?", a structure called **union-find**, or disjoint set, answers each question in nearly O(1).

### Other classic problems

A few more graph problems come up in practice.

A **minimum spanning tree** connects all nodes with the least total edge weight and no cycles: the cheapest way to cable a set of buildings. Kruskal's and Prim's algorithms solve it quickly.

**Maximum flow** asks how much can pass through a network with limited capacity on each edge, such as passengers through a set of connecting flights.

Some problems have no known fast solution. Visiting every node once by the shortest total route is the **travelling salesman problem**. The number of possible routes grows factorially, and no algorithm is known that solves every case in polynomial time. Recursion and dynamic programming (3.8) explains what "hard" means here and what people do instead.

### Graphs in databases

Relational databases can store graphs as an edge table with from and to columns. Recursive common table expressions walk them, although long or cyclic walks need care to avoid running forever. SQL Server has node and edge tables with a `MATCH` clause. Dedicated graph databases such as Neo4j store graphs natively and query them with languages like Cypher, and suit data where the questions are mostly about connections. More in Non-relational stores (7.6).

### In short

A graph is a set of vertices joined by edges, which can have directions and weights. Store most graphs as adjacency lists. Breadth-first search, using a queue, finds paths with the fewest edges; depth-first search, using a stack, answers questions about reachability and cycles. Dijkstra's algorithm finds the shortest weighted path when no weight is negative, and A* speeds it up with a good estimate. A topological sort orders dependencies and detects cycles.

### What goes wrong

**No visited set.** A search on a graph with cycles loops forever or explodes in memory. Always track visited nodes.

**BFS for weighted routes.** Fewest legs is reported as shortest distance. Use Dijkstra's algorithm or A* when edges have weights.

**Negative weights with Dijkstra's algorithm.** Results are silently wrong. Check weights, or use Bellman-Ford.

**An overestimating A* heuristic.** The search is fast and finds a route, but not the best one. Make sure the estimate can't exceed the true remaining cost.

**Recursive DFS on huge graphs.** Deep paths overflow the stack and kill the process. Use an explicit stack.

**Assuming dependency data has no cycles.** A circular project reference or migration dependency makes a naive ordering routine hang or give a wrong order. Detect cycles and report which nodes are involved.

**Adjacency matrices for sparse graphs.** A matrix for 100,000 nodes needs 10 billion cells, most of them empty. Use adjacency lists.

### Cost model

| Task | Algorithm | Cost |
|---|---|---|
| Fewest edges | Breadth-first search | O(V + E) |
| Reachability, cycles, components | Depth-first search | O(V + E) |
| Order a DAG | Kahn's algorithm | O(V + E) |
| Shortest weighted path, no negative weights | Dijkstra's algorithm | O((V + E) log V) |
| Shortest path with negative weights | Bellman-Ford | O(V × E) |
| Adjacency list memory | | O(V + E) |
| Adjacency matrix memory | | O(V²) |

A graph of 10,000 airports and 100,000 routes fits in a few megabytes as adjacency lists, and Dijkstra's algorithm over it runs in milliseconds.

### In other languages (optional)

**JavaScript and TypeScript.** Graphs are usually `Map<string, string[]>` adjacency lists. Build tools such as bundlers work on the dependency graph of modules.

**Python.** The NetworkX library provides graph structures and almost every classic algorithm, including shortest paths, components and flows. The standard library's `graphlib.TopologicalSorter` does topological sorts.

**Java.** JGraphT is the widely used graph library.

**SQL.** `WITH RECURSIVE` in PostgreSQL, or a recursive `WITH` in SQL Server, walks graphs stored as edge tables. PostgreSQL's pgRouting extension adds Dijkstra's algorithm and A* for road networks.

### Key terms

- **Graph**: A set of vertices connected by edges.
- **Vertex**: One of the things in a graph. Also called a node.
- **Edge**: A connection between two vertices, possibly with a direction and a weight.
- **Directed graph**: A graph whose edges each point one way.
- **Weighted graph**: A graph whose edges carry a number such as a distance or cost.
- **Cycle**: A path that returns to its starting vertex.
- **Directed acyclic graph**: A directed graph with no cycles. Often shortened to DAG.
- **Sparse graph**: A graph where each vertex connects to only a few others.
- **Adjacency list**: Storing a graph as a list of neighbours for each vertex.
- **Adjacency matrix**: Storing a graph as a grid of all vertex pairs, marking which are connected.
- **Breadth-first search**: Exploring a graph ring by ring using a queue. Finds paths with the fewest edges.
- **Depth-first search**: Exploring a graph as deep as possible before backing up, using a stack.
- **Shortest path**: The path between two vertices with the smallest total edge weight.
- **Dijkstra's algorithm**: Finding shortest paths with non-negative weights by always expanding the closest unvisited vertex.
- **A* search**: A shortest-path search that uses a heuristic estimate to head toward the target.
- **Heuristic**: An estimate that guides a search, such as straight-line distance to the destination.
- **Topological sort**: An ordering of a DAG's vertices where every edge points forward.
- **Connected component**: A group of vertices that can all reach each other and nothing outside the group.
- **Union-find**: A structure that tracks which items are connected and merges groups in nearly constant time.

### Where this leads

Why some graph problems are hard, and how to cope, is in Recursion and dynamic programming (3.8). Dependency graphs of packages and projects come back in Builds and dependencies (11.2). Graph databases are in Non-relational stores (7.6). Networks of servers are graphs too, and routing across them is in IP addresses and routing (6.2). Services calling each other form a graph in Monoliths, modules and services (12.1).

### Check yourself

1. Add routes from SYD to AKL (2160 km) and from LAX to AKL (10480 km). Find the shortest route from LHR to AKL.
2. Change `Shortest` into A*. Add latitude and longitude for each airport, compute great-circle distance with the haversine formula, and use it as the heuristic.
3. Write a DFS-based cycle detector that reports the nodes forming the cycle, and test it with the cyclic build steps.
4. Count the connected components after adding a separate pair of airports that connect only to each other.
5. Model a flight's status changes, such as scheduled, boarding, departed, landed and cancelled, as a directed graph of allowed transitions. Write a method that rejects any change that isn't an edge.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The fewest-stops route between two airports

Customer support wants the itinerary with the fewest connections from Hanoi to Lisbon.

*Breadth-first search*

```csharp
List<string>? FewestStops(Dictionary<string, List<string>> routes, string from, string to)
{
    var prev = new Dictionary<string, string> { [from] = from };
    var q = new Queue<string>([from]);
    while (q.TryDequeue(out var a))
    {
        if (a == to) break;
        foreach (var b in routes.GetValueOrDefault(a, []))
            if (prev.TryAdd(b, a)) q.Enqueue(b);          // visited set prevents loops
    }
    if (!prev.ContainsKey(to)) return null;
    var path = new List<string>();
    for (var x = to; x != from; x = prev[x]) path.Add(x);
    path.Add(from); path.Reverse();
    return path;
}
```

- For shortest distance or price, use Dijkstra's algorithm instead: BFS only minimizes the number of legs.

> **Lesson:** Model connections as a graph and the classic algorithms answer the question in a few lines.

#### Example 2: Running database migrations in dependency order

Twenty data-fix scripts declare which others they depend on. You need a safe order and an error if there's a cycle.

*Topological sort (Kahn's algorithm)*

```csharp
List<string> Order(Dictionary<string, string[]> dependsOn)
{
    var indegree = dependsOn.Keys.ToDictionary(k => k, k => dependsOn[k].Length);
    var users = dependsOn.SelectMany(kv => kv.Value.Select(d => (d, kv.Key))).ToLookup(x => x.d, x => x.Key);
    var ready = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var order = new List<string>();
    while (ready.TryDequeue(out var s))
    {
        order.Add(s);
        foreach (var u in users[s]) if (--indegree[u] == 0) ready.Enqueue(u);
    }
    if (order.Count != dependsOn.Count) throw new InvalidOperationException("Dependency cycle");
    return order;
}
```

> **Lesson:** Topological sort orders dependencies and detects cycles in the same pass.

### Structures you build yourself

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Structures you build yourself</p>

<a id="be-m122"></a>

#### Graphs: representation and topological sort

Business data is full of graphs that nobody calls graphs: category trees, bill-of-materials, task dependencies, entity relationships, permission inheritance, module load order.

``` csharp
// Adjacency list - the default. O(V + E) memory, fast to iterate neighbours.
var adjacency = new Dictionary<int, List<int>>();

// For dense graphs or fixed small V, an adjacency matrix as a bitset is faster:
// row i, column j = edge i->j, one bit each.

// For a fixed graph queried many times, CSR (compressed sparse row) is the
// cache-friendly form: two flat arrays, zero pointer chasing.
int[] offsets;   // offsets[i]..offsets[i+1] indexes into 'targets'
int[] targets;
```

``` csharp
/// <summary>Kahn's algorithm. Returns false if the graph has a cycle.</summary>
static bool TryTopologicalSort<T>(
        IReadOnlyCollection<T> nodes,
        Func<T, IEnumerable<T>> dependsOn,
        out List<T> ordered) where T : notnull {
    var inDegree = nodes.ToDictionary(n => n, _ => 0);
    var dependents = nodes.ToDictionary(n => n, _ => new List<T>());

    foreach (var n in nodes)
        foreach (var dep in dependsOn(n)) {
            inDegree[n]++;
            dependents[dep].Add(n);
        }

    var ready = new Queue<T>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    ordered = new List<T>(nodes.Count);

    while (ready.Count > 0) {
        var n = ready.Dequeue();
        ordered.Add(n);
        foreach (var d in dependents[n])
            if (--inDegree[d] == 0) ready.Enqueue(d);
    }
    return ordered.Count == nodes.Count;      // short = a cycle exists
}
```

``` csharp
// The nodes left with in-degree > 0 ARE the cycle - report them, don't just fail:
var stuck = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
throw new InvalidOperationException($"Circular dependency among: {string.Join(", ", stuck)}");
```

> **\[Tip\]**
>
> **The relational counterpart.** Hierarchies stored in a database can be traversed with a recursive CTE ([CTEs and recursive queries](07-02-the-relational-model-and-sql.md#be-d413)), but a **materialised path** (`/1/17/93/`) or a **closure table** (one row per ancestor-descendant pair) turns "all descendants" into an index range scan or a simple join. Both trade write cost for read speed; pick based on how often the tree changes ([Normalization and deliberate denormalization](07-02-the-relational-model-and-sql.md#be-m189)).

> **\[Trap\]**
>
> **Recursion depth.** A recursive depth-first traversal over a deep chain — a 50,000-node linked category list, for instance — overflows the 1 MB stack and takes the process down with a `StackOverflowException` that *cannot be caught* ([Stack and heap: the real mental model](01-05-memory.md#be-m25)). Any traversal over data of unbounded shape should use an explicit `Stack<T>`, not the call stack.

<a id="be-m120"></a>

#### Union-find: grouping and deduplication

Disjoint-set union answers "are these two things in the same group?" and "merge these two groups" in effectively constant amortised time. It is the right tool for transitive deduplication — the customer-matching problem where A matches B on email, B matches C on phone, and therefore A, B and C are one customer.

``` csharp
public sealed class UnionFind {
    private readonly int[] _parent;
    private readonly int[] _rank;
    public int GroupCount { get; private set; }

    public UnionFind(int n) {
        _parent = new int[n]; _rank = new int[n]; GroupCount = n;
        for (int i = 0; i < n; i++) _parent[i] = i;
    }

    public int Find(int x) {                      // path compression
        while (_parent[x] != x) {
            _parent[x] = _parent[_parent[x]];     // halve the path as we walk
            x = _parent[x];
        }
        return x;
    }

    public bool Union(int a, int b) {             // union by rank
        int ra = Find(a), rb = Find(b);
        if (ra == rb) return false;
        if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
        _parent[rb] = ra;
        if (_rank[ra] == _rank[rb]) _rank[ra]++;
        GroupCount--;
        return true;
    }

    public bool Connected(int a, int b) => Find(a) == Find(b);
}
```

``` csharp
// Transitive customer deduplication, in one pass per matching rule:
var index = customers.Select((c, i) => (c, i)).ToDictionary(x => x.c.Id, x => x.i);
var uf = new UnionFind(customers.Count);

foreach (var group in customers.Where(c => c.Email is not null).GroupBy(c => c.Email!))
    foreach (var c in group.Skip(1)) uf.Union(index[group.First().Id], index[c.Id]);

foreach (var group in customers.Where(c => c.Phone is not null).GroupBy(c => c.Phone!))
    foreach (var c in group.Skip(1)) uf.Union(index[group.First().Id], index[c.Id]);

var clusters = customers
    .Select((c, i) => (Root: uf.Find(i), Customer: c))
    .GroupBy(x => x.Root)
    .Select(g => g.Select(x => x.Customer).ToList())
    .ToList();
```

> **\[Why it matters\]**
>
> **Why not just a graph traversal?** You could build an adjacency list and run connected components, and for a one-off that is fine. Union-find wins when the merges arrive *incrementally* — you can answer "same group?" at any point during ingestion without rebuilding anything, which is what makes it the standard tool for streaming deduplication and for Kruskal's minimum spanning tree.
