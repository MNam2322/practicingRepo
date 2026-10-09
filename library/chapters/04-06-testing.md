---
id: "4-6"
number: "4.6"
title: "Testing"
part: 4
part_title: "Designing programs"
words: 11338
minutes: 49
---

# 4.6 Testing

> **Quick look.** Tests make change safe; this is what to test, how, and how to keep the suite trustworthy.
>
> - Arrange, act, assert, one behaviour per test, named so a failure explains itself.
> - Many fast unit tests, fewer integration tests against real infrastructure, a few end-to-end tests.
> - Use stand-ins only at the edges; test through public behaviour, not internal calls.
> - Don't use EF's InMemory provider: test against a real database with Testcontainers.
> - Flaky tests come from hidden inputs (time, randomness, shared state); quarantine or delete them immediately. Inject TimeProvider.
> - Coverage shows what ran, not whether bugs would be caught; mutation testing and query-count assertions catch more.
> - Contract tests (Pact) prove services still agree; BenchmarkDotNet's Allocated column matters as much as Mean.


### The big picture

Tests are code that checks other code. Their obvious job is catching mistakes. Their bigger job is making change safe, so you can refactor, upgrade and fix without fear.

This chapter covers the kinds of test and how many of each to have, the stand-ins used to isolate the code under test, and why some tests pass and fail at random. A runnable file shows exactly what a test framework does, before moving on to xUnit and to tests against a real database. It ends Part 4.

### Before you start

**Builds on:** 4.5 Design principles that hold up, 4.2 Abstraction and interfaces, 4.4 Immutability and pure functions, 1.7 The operating system.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **Message broker** (8.7): The server that hosts queues and topics and delivers messages.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Runner** (11.4): The machine or container that runs a pipeline's jobs.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.
- **SQL Server** (7.2): Microsoft's relational database product.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, null operators, tuples, exceptions, async and await, attributes. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between a stub, a spy, a mock and a fake?**

> A stub returns canned answers. A spy also records how it was called so the test can check afterwards. A mock is set up with expectations in advance and fails if they aren't met. A fake is a working but simplified implementation, such as an in-memory repository.

**2. What does the test pyramid recommend?**

> Many fast unit tests at the bottom, fewer integration tests in the middle and a small number of slow end-to-end tests at the top. Each level catches different problems at a different cost.

**3. Why is EF Core's in-memory provider a poor stand-in for a real database in tests?**

> It doesn't behave like SQL. It has no real transactions, doesn't enforce many constraints, compares strings differently and runs queries that a real database would reject. Tests pass and production fails. Use a real database, for example in a container.

**4. What does 90% code coverage tell you?**

> That 90% of lines ran during the tests. Not that their results were checked. A test with no assertions can reach high coverage. Mutation testing measures whether tests actually catch changes.

**5. A test passes on your machine and fails randomly in CI. What are the usual causes?**

> Dependence on the current time, on random values, on timing and thread scheduling, on test order, or on shared state such as a database or static fields left over from another test.

**6. What is property-based testing?**

> Instead of checking a few chosen examples, you state a rule that must hold for all inputs, such as "sorting twice gives the same result as sorting once". A tool then checks it against hundreds of generated inputs, and shrinks any failure to the smallest example.

**7. What is the cycle in test-driven development?**

> Red, green, refactor. Write a small failing test. Write just enough code to make it pass. Then clean up the design while all tests stay green.

**8. How do you test code that depends on the current time?**

> Inject a `TimeProvider` instead of reading `DateTime.UtcNow`. In tests, pass `FakeTimeProvider` from the Microsoft.Extensions.TimeProvider.Testing package, which lets the test set and advance time by hand.

### Why test

Tests are code that checks other code. Their first job is catching mistakes. Their bigger job is making change safe.

Design principles that hold up (4.5) described refactoring as changing structure without changing behaviour. Without tests, you can't tell whether behaviour changed. With them, you can restructure boldly, upgrade libraries and fix bugs knowing that anything you break will show up in minutes, not in production.

Tests also document. A well-named test shows exactly how code is meant to be used and what it promises. And they give design feedback: code that is hard to test is usually code with hidden dependencies, too many responsibilities or unclear contracts.

### The shape of a test

Most tests follow three steps, called **arrange-act-assert**. Arrange: set up the objects and inputs. Act: call the thing being tested, usually once. Assert: check the outcome. Behaviour-driven teams call the same steps given, when, then.

A good test checks one behaviour, has a name that says what that behaviour is, and fails with a message that explains what went wrong. "Business seats add the lounge fee" is a better name than "Test3" or "PriceTest".

### Kinds of test

**Unit tests** check a small piece of behaviour in isolation, in memory, in milliseconds. A "unit" is a behaviour, not necessarily one class: a fare calculation might involve three small classes and still be one unit.

**Integration tests** check that pieces work together with real infrastructure: the code with a real database, a real HTTP pipeline or a real message broker. They catch problems unit tests can't, such as SQL that the database rejects, serialization mismatches and wrong configuration. They take seconds rather than milliseconds.

**End-to-end tests** drive the whole system the way a user would: open the browser, click through a booking, check the confirmation email. They give the most confidence and are the slowest, most fragile and most expensive to maintain.

The **test pyramid**, described by Mike Cohn, suggests many unit tests, fewer integration tests and only a few end-to-end tests. Some teams prefer a "testing trophy" shape with more integration tests, because modern containers make real dependencies cheap to run. Either way, put each check at the lowest level that can catch the problem.

When a bug is found, write a test that reproduces it before fixing it. That **regression test** proves the fix works and keeps the bug from coming back.

### Test doubles

Unit tests replace slow or unpredictable collaborators with stand-ins. Gerard Meszaros named the general idea a **test double**, after stunt doubles in films, and described several kinds.

A **dummy** is passed only to satisfy a parameter and never used. A **stub** returns fixed answers: "this flight exists and departs at 14:00". A **spy** records the calls it receives, so the test can check them afterwards: "one notification was sent, with this text". A **mock** is set up with expectations beforehand and verifies them itself, usually through a library such as NSubstitute or Moq. A **fake** is a real, working implementation that is simpler than production, such as an in-memory repository or a fake clock.

Doubles need seams, the interfaces at the edges from Abstraction and interfaces (4.2). The best places for them are the edges of the system: databases, other services, the clock, randomness and the file system.

Use doubles sparingly inside your own logic. A test that mocks every collaborator and checks every call locks in how the code works rather than what it does. Refactor anything, and dozens of tests break even though the behaviour is the same. Test through public behaviour, and use real objects for everything that is fast and deterministic.

### A test run you can see

Real projects use a test framework, shown below. First, here is a complete, runnable file with a tiny homemade runner, so you can see exactly what a framework does for you.

```csharp
// testing.cs — run with: dotnet run testing.cs
using System.Linq;

var results = new List<(string Name, bool Passed, string? Error)>();

void Test(string name, Action body)
{
    try { body(); results.Add((name, true, null)); }
    catch (Exception ex) { results.Add((name, false, ex.Message)); }
}

// 1. Pure logic: no setup, no doubles
Test("business seats add the lounge fee", () =>
{
    var calculator = new FareCalculator();                 // arrange
    decimal price = calculator.Price("business", 900m);    // act
    Assert.Equal(950m, price);                             // assert
});
Test("negative fares are rejected", () =>
    Assert.Throws<ArgumentOutOfRangeException>(() => new FareCalculator().Price("economy", -1m)));

// 2. A fake clock makes a time rule deterministic
Test("boarding opens 40 minutes before departure", () =>
{
    var clock = new FakeClock(new DateTimeOffset(2026, 9, 28, 13, 20, 0, TimeSpan.Zero));
    var gate = new Gate(clock);
    var departure = new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
    Assert.Equal(true, gate.BoardingOpen(departure));
    clock.Now = clock.Now.AddMinutes(-1);                  // one minute earlier: 41 minutes to go
    Assert.Equal(false, gate.BoardingOpen(departure));
});

// 3. A spy stands in for the notification service
Test("delays over an hour notify passengers", () =>
{
    var spy = new SpyNotifier();
    var handler = new DelayHandler(spy);
    handler.Handle("BA117", TimeSpan.FromMinutes(75));
    handler.Handle("QF1", TimeSpan.FromMinutes(20));
    Assert.Equal(1, spy.Sent.Count);
    Assert.Equal("BA117 is delayed by 75 minutes", spy.Sent[0]);
});

// 4. Property-based: a rule checked against a thousand generated inputs
Test("sorting keeps every item, orders them, and is stable when repeated", () =>
{
    var rng = new Random(123);                             // a fixed seed makes failures reproducible
    for (int i = 0; i < 1_000; i++)
    {
        int[] input = Enumerable.Range(0, rng.Next(0, 50)).Select(_ => rng.Next(-100, 100)).ToArray();
        int[] sorted = input.Order().ToArray();
        Assert.Equal(input.Length, sorted.Length);
        Assert.Equal(true, sorted.Zip(sorted.Skip(1)).All(p => p.First <= p.Second));
        Assert.Equal(true, sorted.SequenceEqual(sorted.Order()));
    }
});

// 5. A failing test, to see what failure looks like
Test("this one fails on purpose", () => Assert.Equal(3, 1 + 1));

foreach (var (name, passed, error) in results)
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(error is null ? "" : $": {error}")}");
Console.WriteLine($"{results.Count(r => r.Passed)} passed, {results.Count(r => !r.Passed)} failed");
// PASS  business seats add the lounge fee ... FAIL  this one fails on purpose: expected 3, got 2
// 5 passed, 1 failed
return results.All(r => r.Passed) ? 0 : 1;                // a non-zero exit code fails a CI build

static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"expected {expected}, got {actual}");
    }

    public static void Throws<TException>(Action act) where TException : Exception
    {
        try { act(); }
        catch (TException) { return; }
        throw new Exception($"expected {typeof(TException).Name}");
    }
}

sealed class FareCalculator
{
    public decimal Price(string cabin, decimal baseFare)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseFare);
        return cabin == "business" ? baseFare + 50m : baseFare;
    }
}

interface IClock { DateTimeOffset Now { get; } }
sealed class FakeClock(DateTimeOffset start) : IClock { public DateTimeOffset Now { get; set; } = start; }

sealed class Gate(IClock clock)
{
    public bool BoardingOpen(DateTimeOffset departure) => departure - clock.Now <= TimeSpan.FromMinutes(40);
}

interface INotifier { void Send(string message); }
sealed class SpyNotifier : INotifier
{
    public List<string> Sent { get; } = [];
    public void Send(string message) => Sent.Add(message);   // record instead of sending
}

sealed class DelayHandler(INotifier notifier)
{
    public void Handle(string flight, TimeSpan delay)
    {
        if (delay > TimeSpan.FromHours(1))
            notifier.Send($"{flight} is delayed by {delay.TotalMinutes} minutes");
    }
}
```

The example uses its own `IClock` to stay self-contained. In real code, inject .NET's `TimeProvider` and use `FakeTimeProvider` in tests, which also controls timers and delays.

### Test frameworks in .NET

Three frameworks are widely used: xUnit, NUnit and MSTest. They all discover test methods, run them in isolation, report results and integrate with Visual Studio, `dotnet test` and CI systems. xUnit is the most common in new open-source code. Create a test project with `dotnet new xunit`, reference the project under test, and write tests like these.

```csharp
// FareCalculatorTests.cs in a test project created with: dotnet new xunit -n Fares.Tests
using Xunit;

public class FareCalculatorTests
{
    [Fact]
    public void Business_seats_add_the_lounge_fee()
    {
        var calculator = new FareCalculator();
        decimal price = calculator.Price("business", 900m);
        Assert.Equal(950m, price);
    }

    public static TheoryData<string, decimal, decimal> Cases => new()
    {
        { "economy", 120m, 120m },
        { "business", 900m, 950m },
    };

    [Theory, MemberData(nameof(Cases))]              // one test per row of data
    public void Price_depends_on_cabin(string cabin, decimal baseFare, decimal expected) =>
        Assert.Equal(expected, new FareCalculator().Price(cabin, baseFare));
}
```

`[Fact]` marks a test. `[Theory]` marks a test that runs once per data row. xUnit creates a fresh instance of the test class for every test, so fields can't leak state between tests.

### Integration tests with real dependencies

For database code, test against the real database engine. Fakes such as EF Core's in-memory provider don't enforce constraints, have no real transactions, compare strings differently and happily run queries that SQL Server would reject.

**Testcontainers** starts real databases, message brokers and other services in Docker for the duration of a test run, and throws them away afterwards. Each run gets a clean, real environment.

```csharp
// A sketch of an integration test. Needs Docker and the Testcontainers.MsSql package.
public class FlightRepositoryTests : IAsyncLifetime
{
    readonly MsSqlContainer db = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync() => await db.StartAsync();   // a fresh SQL Server for this test class
    public async Task DisposeAsync() => await db.DisposeAsync();

    [Fact]
    public async Task Saves_and_loads_a_flight()
    {
        var repository = new FlightRepository(db.GetConnectionString());
        await repository.SaveAsync(new Flight("BA117", "LHR", "JFK"));
        Assert.Equal("JFK", (await repository.FindAsync("BA117"))!.To);
    }
}
```

For web APIs, ASP.NET Core's `WebApplicationFactory` runs the whole application in memory and gives the test an `HttpClient` to call it, as A modern .NET backend (8.3) shows. For browser-level end-to-end tests, Playwright drives real browsers.

### Deterministic tests

A **deterministic test** gives the same result every time. A **flaky test** passes sometimes and fails sometimes without any code change. Flaky tests are worse than no tests: people learn to ignore failures, and then real failures get ignored too.

The causes are always hidden inputs. Time: code that reads the clock, or tests that assume a date, a time zone or that a delay will finish in time. Randomness: generated values without a fixed seed. Concurrency: tests that assume one thread finishes before another. Order: tests that pass only when run after another test that leaves data behind. Shared state: databases, files, static fields and caches used by several tests at once.

The cures follow from Immutability and pure functions (4.4). Pass time and randomness in. Give each test its own data. Wait for conditions, not for fixed delays. Make tests independent so they can run in any order and in parallel.

### Measuring test quality

**Code coverage** measures which lines or branches ran during the tests. Tools such as coverlet produce it from `dotnet test`. Low coverage points at code with no tests at all. High coverage proves little: a test that calls a method and checks nothing still counts. Chasing 100% leads to tests of trivial code that only slow down change.

**Mutation testing** asks a sharper question: would the tests notice a bug? A tool makes small changes to the code, such as turning `<` into `<=`, `+` into `-` or `true` into `false`, and runs the tests against each change. A change that no test catches is a survivor, and it shows a gap. Stryker.NET does this for C#. It is slow, so teams run it occasionally or on changed code only.

### Other techniques

**Property-based testing** states rules that must hold for all inputs and lets a tool generate hundreds of inputs to check them. When it finds a failure, it shrinks the input to the smallest case that still fails. It finds edge cases nobody thought to write down: empty lists, negative numbers, Unicode, very large values. FsCheck and CsCheck bring it to .NET. The sorting test above is a hand-made version.

**Snapshot testing**, also called approval testing, saves the output of the code, such as a generated report or a JSON response, the first time, and on later runs compares new output against the saved one. Any difference fails the test and shows a diff to approve or reject. The Verify library does this for .NET. It suits large outputs that are tedious to assert field by field.

### Test-driven development

Kent Beck popularized **test-driven development** (TDD) in the late 1990s. Work in a tight loop. Red: write a small test for behaviour that doesn't exist yet, and watch it fail. Green: write the simplest code that makes it pass. Refactor: clean up the code and the test while everything stays green. Repeat, in steps of a few minutes.

TDD guarantees every behaviour has a test, and it pushes designs toward small, loosely coupled pieces, because code written test-first has to be testable. Some developers use it for everything, others for tricky logic only. Even without strict TDD, writing the test before fixing a bug is a habit worth keeping.

### Tests in the pipeline

Tests only protect you if they run. Run the fast tests on every build and every pull request, and block merging when they fail, as CI/CD pipelines (11.4) describes. `dotnet test` returns a non-zero exit code on failure, which is how the pipeline knows, the same mechanism as in The operating system (1.7). Keep the fast suite fast, ideally under a few minutes, so people run it before pushing.

### In short

Write each test as arrange, act, assert, checking one behaviour. Have many fast unit tests, fewer integration tests against real infrastructure and a few end-to-end tests. Replace slow or unpredictable collaborators at the edges with stand-ins, and test through public behaviour rather than internal calls. Random failures come from hidden inputs such as time, randomness and shared state. Coverage shows which code ran; mutation testing shows whether the tests would notice a bug.

### What goes wrong

**Testing implementation instead of behaviour.** Tests that mock every collaborator and verify every call break on each refactoring. Test through public behaviour and keep doubles at the edges.

**Tests with no real assertion.** Code runs, coverage rises, and nothing is checked. Every test should fail if the behaviour it names breaks. Try breaking the code and watching the test fail.

**Flaky tests left in place.** Teams re-run failing builds until they go green, and real failures slip through. Fix or delete flaky tests immediately.

**The in-memory database as a stand-in.** Queries and constraints behave differently from production, so tests pass and production fails. Use the real engine in a container.

**Shared test data.** Tests depend on rows another test created, or on the order they run in. Give each test its own data, or reset between tests.

**Chasing a coverage number.** Tests of getters and framework code are written to hit a target. Aim coverage at logic that matters, and use mutation testing to judge quality.

**Slow suites nobody runs.** A test suite that takes an hour is run once a day, and failures are found late. Keep unit tests fast and move slow tests to a separate stage.

### Cost model

A unit test typically runs in well under a millisecond, so thousands run in seconds. An integration test with a real database takes tens to hundreds of milliseconds per test, plus several seconds to start the container once per run. An end-to-end browser test takes seconds to tens of seconds. Writing tests adds perhaps a third to the time spent on a feature at first. That is repaid the first time a refactoring, upgrade or bug fix is made safely, and many times over during the life of the code. Maintaining brittle tests is the cost to avoid.

### In other languages (optional)

**JavaScript and TypeScript.** Vitest and Jest are the common test runners, with built-in mocking and snapshot testing. Node.js also has a built-in test runner, `node --test`. Playwright and Cypress drive browsers for end-to-end tests. Testing Library encourages testing components the way users see them.

**Python.** pytest is the standard, with plain `assert` statements and fixtures for setup. Hypothesis is a mature property-based testing library.

**Java.** JUnit 5 with Mockito for doubles and Testcontainers, which started in the Java world.

**SQL.** tSQLt runs unit tests inside SQL Server for stored procedures and functions. Most teams instead test database code through integration tests from the application side.

### Key terms

- **Arrange-act-assert**: The three steps of a test: set up, perform the action, check the outcome.
- **Unit test**: A fast, in-memory test of a small piece of behaviour in isolation.
- **Integration test**: A test that checks pieces working together with real infrastructure such as a database.
- **End-to-end test**: A test that drives the whole system the way a user would.
- **Test pyramid**: The guideline to have many unit tests, fewer integration tests and few end-to-end tests.
- **Regression test**: A test written to reproduce a bug, so it can't return unnoticed.
- **Test double**: Any stand-in object used in place of a real collaborator in a test.
- **Stub**: A test double that returns fixed answers.
- **Spy**: A test double that records how it was called, for checking afterwards.
- **Mock**: A test double set up with expectations that it verifies itself.
- **Fake**: A working but simplified implementation, such as an in-memory repository.
- **Testcontainers**: A library that runs real databases and services in Docker for the length of a test run.
- **Deterministic test**: A test that gives the same result on every run.
- **Flaky test**: A test that passes and fails at random without any code change.
- **Code coverage**: The share of code lines or branches that ran during the tests.
- **Mutation testing**: Checking test quality by making small changes to code and seeing whether tests catch them.
- **Property-based testing**: Checking a rule against many generated inputs instead of a few chosen examples.
- **Snapshot testing**: Saving output once and failing when later output differs. Also called approval testing.
- **Test-driven development**: Writing a failing test first, making it pass, then refactoring, in short cycles.

### Where this leads

This ends Part 4. Part 5 turns to running several things at once, starting with Why concurrency is hard (5.1), which is also where the hardest flaky tests come from. Testing database code for real is in ORMs and EF Core (7.7). Testing a whole web API in memory is in A modern .NET backend (8.3). Running tests on every change is in CI/CD pipelines (11.4).

### Check yourself

1. Add a premium cabin to `FareCalculator` test-first: write the failing test in `testing.cs`, watch it fail, then make it pass.
2. Break `Gate.BoardingOpen` by changing `<=` to `<`. Check whether a test catches it, and add one that does if not. This is mutation testing by hand.
3. Replace `IClock` with `TimeProvider` in `testing.cs`. Write your own small subclass of `TimeProvider` that returns a fixed time.
4. Create a real xUnit project with `dotnet new xunit`, move the fare tests into it and run `dotnet test`. Check the exit code when a test fails.
5. Find a flaky or slow test in a codebase you know. Identify its hidden input, and fix it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A test that catches N+1 before review does

The order list keeps regressing to one query per row whenever someone adds a property.

*Count database commands in a test*

```csharp
sealed class CommandCounter : DbCommandInterceptor
{
    public int Count;
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r)
    { Interlocked.Increment(ref Count); return r; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
    { Interlocked.Increment(ref Count); return ValueTask.FromResult(r); }
}

[Fact]
public async Task Order_list_uses_a_single_query()
{
    var counter = new CommandCounter();
    await using var db = _fixture.CreateContext(counter);   // real database via Testcontainers
    await new OrderQueries(db).ListAsync(customerId: 1, page: 0, default);
    Assert.Equal(1, counter.Count);
}
```

> **Lesson:** Assert on the number of round trips. It catches performance regressions deterministically, which timing tests can't.

#### Example 2: A flaky expiry test

"Token expires after 15 minutes" uses `Thread.Sleep` and fails randomly on the CI server.

*Control time with FakeTimeProvider*

```csharp
// Microsoft.Extensions.TimeProvider.Testing
var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
var tokens = new TokenService(time);
var t = tokens.Issue("user-1");

time.Advance(TimeSpan.FromMinutes(14));
Assert.True(tokens.IsValid(t));
time.Advance(TimeSpan.FromMinutes(2));
Assert.False(tokens.IsValid(t));     // deterministic, runs in microseconds
```

> **Lesson:** Flaky tests come from hidden inputs. Inject the clock and make time something the test controls.

### Testing & Benchmarking

<p class="src">From <em>.NET Backend Deep Dive</em> · Testing & Benchmarking</p>

<a id="dn-82"></a>

#### Testing the soft-delete interceptor

```csharp
[Fact]
public async Task Delete_should_soft_delete_not_remove_row()
{
    await using var db = CreateContext();       // SQLite in-memory or Testcontainers
    var order = new Order { CustomerId = 1 };
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    db.Orders.Remove(order);
    await db.SaveChangesAsync();

    // Normal query: invisible
    Assert.Empty(await db.Orders.ToListAsync());
    // Filter bypassed: still there, flagged deleted
    var raw = await db.Orders.IgnoreQueryFilters().SingleAsync();
    Assert.True(raw.IsDeleted);
    Assert.NotNull(raw.DeletedAt);
}
```

<a id="dn-83"></a>

#### Catching N+1 in a unit test (very worthwhile)

```csharp
public class CommandCountingInterceptor : DbCommandInterceptor
{
    public int Count { get; private set; }
    public List<string> Commands { get; } = new();

    public override ValueTask<DbDataReader> ReaderExecutingAsync(
        DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> r,
        CancellationToken ct = default)
    {
        Count++;
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, e, r, ct);
    }
}

[Fact]
public async Task GetOrders_should_not_have_n_plus_one()
{
    var counter = new CommandCountingInterceptor();
    await using var db = CreateContext(counter);
    await SeedAsync(db, orders: 50);

    var result = await new OrderService(db).GetOrdersWithCustomerAsync();

    Assert.Equal(50, result.Count);
    Assert.True(counter.Count <= 2,
        $"Expected <= 2 queries but got {counter.Count}:\n{string.Join("\n", counter.Commands)}");
}
```

This catches regressions better than any code review, because N+1 creeps back in the moment someone adds an `.Include` or drops a `.Select`.

<a id="dn-84"></a>

#### Testcontainers — testing against a real database

```csharp
public class DbFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await using var db = new AppDbContext(Options());
        await db.Database.MigrateAsync();     // this also tests your migrations
    }
    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();
}
```

> Avoid EF's InMemory provider: there's no real SQL, no constraint enforcement, no detection of untranslatable LINQ, and no real transactions. SQLite in-memory is better but still differs from SQL Server in types, functions and collation.

<a id="dn-85"></a>

#### BenchmarkDotNet — measuring correctly

```csharp
[MemoryDiagnoser]                 // reports Allocated plus GC counts per generation
[SimpleJob(RuntimeMoniker.Net90)]
public class StringBenchmarks
{
    private readonly string[] _items = Enumerable.Range(0, 100).Select(i => $"item{i}").ToArray();

    [Benchmark(Baseline = true)]
    public string Concat()
    {
        var s = "";
        foreach (var i in _items) s += i;     // O(n^2) allocation
        return s;
    }

    [Benchmark]
    public string Builder()
    {
        var sb = new StringBuilder();
        foreach (var i in _items) sb.Append(i);
        return sb.ToString();
    }

    [Benchmark]
    public string Join() => string.Join("", _items);
}
```

The results will show `Concat` allocating an order of magnitude more. **Always read the `Allocated` column, not just `Mean`** — allocation is what kills p99 in production, via the GC.

Benchmarking rules: warm up (or you're measuring tier-0 JIT), never measure a debug build, and don't let the compiler optimize your work away (return the value or use a `Consumer`).

<a id="dn-86"></a>

#### API integration tests

```csharp
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public ApiTests(WebApplicationFactory<Program> f) =>
        _client = f.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IPaymentGateway>();
            s.AddSingleton<IPaymentGateway, FakePaymentGateway>();   // stub externals
        })).CreateClient();

    [Fact]
    public async Task Post_order_returns_201()
    {
        var res = await _client.PostAsJsonAsync("/api/orders", new { CustomerId = 1 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}
```

<a id="dn-87"></a>

#### `TimeProvider` — testing time-dependent code (.NET 8+)

```csharp
// Production: services.AddSingleton(TimeProvider.System);
// Test:
var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
var svc = new TokenService(time);
time.Advance(TimeSpan.FromMinutes(31));
Assert.True(svc.IsExpired(token));
```

Stop calling `DateTime.UtcNow` directly — it makes code untestable and unmockable.

---

### Testing Strategy in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · Testing Strategy in Depth</p>

<a id="dn-182"></a>

#### The shape that works for backend services

Part 17 covers mechanics. This is what to test and how to keep the suite trustworthy.

The classic pyramid under-weights the layer that catches the most real bugs in a service with a database.

```
        /\        E2E / smoke      few, critical journeys only
       /  \       
      /____\      Integration      MANY - API + real DB (Testcontainers)
     /      \     
    /________\    Unit             many - pure logic, domain rules, edge cases
```

- **Unit tests** for anything with branching logic and no I/O: domain invariants, validators, pricing, date arithmetic. Fast, deterministic, precise failure messages.
- **Integration tests** are the highest-value layer here, because most bugs in a CRUD-ish service live in the *seams* — LINQ that doesn't translate, a missing migration, a wrong constraint, serialization, middleware order, authorization. Mocking `DbContext` tests your mocks, not your system.
- **E2E** only for a handful of money paths (signup, checkout). They're slow and the flakiest thing you own.

<a id="dn-183"></a>

#### Flaky tests

A flaky test is worse than no test: it trains the team to re-run the build, and then a real failure gets ignored too. **Quarantine or delete them the day they appear.**

Usual causes, in order:

1. **Shared state** between tests — a database row, a static field, a singleton cache. Each test must create its own data with unique keys.
2. **Time.** `DateTime.UtcNow` near a boundary, or an assertion that something takes "under 100ms". Inject `TimeProvider` (Part 32.1).
3. **Ordering assumptions** — a query without `OrderBy` has no guaranteed order, and it *will* differ under load.
4. **Async races** — `Task.Delay(100)` then asserting. Wait on a signal, not a duration.
5. **Test execution order dependence.** Run the suite in random order in CI to expose it.

<a id="dn-184"></a>

#### Contract testing between services

Integration tests with a mocked dependency prove nothing about the real one. Contract tests (Pact or similar) let the consumer declare what it needs and the provider verify it in its own pipeline:

```
Consumer test  -> generates a contract ("GET /orders/1 returns id and total")
Provider CI    -> replays that contract against the real provider -> fails the build if broken
```

Worth introducing at the point where "we changed a field name and three teams broke" becomes a recurring conversation. Below that, a shared typed client and a changelog is cheaper.

<a id="dn-185"></a>

#### Test data and assertions

```csharp
// Builders keep tests readable and resilient to constructor changes
var order = new OrderBuilder()
    .ForCustomer(customerId)
    .WithLine(productId, qty: 2)
    .Submitted()
    .Build();
```

Name tests so a failure explains itself without opening the file: `Cancel_throws_when_order_already_shipped`. Assert on outcomes, not implementation — a test that verifies "the repository was called twice" breaks on every refactor and catches nothing.

**Coverage is a diagnostic, not a target.** 90% coverage of getters says nothing; the useful question is whether the *risky* paths are covered. Mutation testing (Stryker.NET) answers that far better by deliberately breaking your code and checking whether any test notices.

---

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a190"></a>

#### Unit testing and test design

A unit test should be fast, deterministic, and fail for exactly one reason. The value is not coverage percentage — it is that you can change code confidently and the test tells you what broke.

``` csharp
public class RefundPolicyTests
{
    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public void Refund_allowed_within_thirty_days(int daysAgo, bool expected)
    {
        var order = OrderBuilder.Shipped(on: DateOnly.FromDateTime(DateTime.Today.AddDays(-daysAgo)));

        var result = new RefundPolicy(FixedClock.Today).CanRefund(order);

        Assert.Equal(expected, result.IsAllowed);
    }
}
```

- **Test behaviour, not implementation.** If a refactor that changes no behaviour breaks fifty tests, the tests were coupled to the wrong thing.
- **Inject the clock.** `DateTime.UtcNow` in domain code makes tests flaky and time-travel impossible. Use `TimeProvider` (built into .NET 8+).
- **Mock roles, not types.** Stub the things you do not own — the payment gateway, the clock. Do not mock your own value objects.
- **Builders over fixtures.** `OrderBuilder.Shipped()` reads better and survives constructor changes.
- **Name the test as a sentence.** The failure message should tell you the rule that broke.

Tooling: xUnit is the default, NUnit is equally fine, and TUnit is the new fast entrant. Use NSubstitute or Moq for stubs, FluentAssertions or Shouldly for readable assertions, and Bogus for realistic test data.

###### Worked example

The same behaviour, tested badly and well. The difference is what happens when you refactor.

``` csharp
// BAD: coupled to the implementation. Renaming a private method or reordering
// two calls breaks it, even though the behaviour is unchanged.
[Fact]
public void Cancel_calls_the_repository_and_the_publisher()
{
    var repo = Substitute.For<IOrderRepository>();
    var bus  = Substitute.For<IPublisher>();
    var svc  = new OrderService(repo, bus);

    svc.Cancel(orderId);

    repo.Received(1).Update(Arg.Any<Order>());
    bus.Received(1).Publish(Arg.Any<OrderCancelled>());
    // this asserts HOW, not WHAT. It will pass with a completely broken implementation
    // that updates the wrong field, and fail with a correct one that batches the publish.
}
```

``` csharp
// GOOD: asserts observable behaviour and the business rule
public class CancellingAnOrder
{
    [Fact]
    public void A_shipped_order_cannot_be_cancelled()
    {
        var order = new OrderBuilder().Shipped().Build();

        var result = order.Cancel(reason: "Changed my mind", at: Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("order.already_shipped", result.Error!.Code);
        Assert.Equal(OrderStatus.Shipped, order.Status);          // unchanged
        Assert.Empty(order.DequeueEvents());                       // nothing published
    }

    [Fact]
    public void Cancelling_a_placed_order_records_the_reason_and_raises_an_event()
    {
        var order = new OrderBuilder().Placed().Build();

        var result = order.Cancel(reason: "Out of stock", at: Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Out of stock", order.CancellationReason);
        var evt = Assert.Single(order.DequeueEvents());
        Assert.Equal(new OrderCancelled(order.Id, "Out of stock", Now), evt);
    }
}
```

The second version has no mocks at all, because the rule lives in the domain object where it belongs. That is the usual outcome of trying to write behaviour-focused tests: it pushes logic out of services and into types that can be tested without any infrastructure, which is a design improvement rather than a testing trick.

###### Test data builders, and why they outlive fixtures

``` csharp
public class OrderBuilder
{
    private OrderStatus _status = OrderStatus.Draft;
    private readonly List<OrderLine> _lines = [];
    private DateTimeOffset _placedAt = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    public OrderBuilder Placed()  { _status = OrderStatus.Placed;  return WithLineIfEmpty(); }
    public OrderBuilder Shipped() { _status = OrderStatus.Shipped; return WithLineIfEmpty(); }
    public OrderBuilder WithLine(string sku, int qty, decimal price)
        { _lines.Add(new(sku, qty, price)); return this; }
    public OrderBuilder PlacedOn(DateTimeOffset when) { _placedAt = when; return this; }

    public Order Build() => Order.Rehydrate(Guid.CreateVersion7(), _status, _lines, _placedAt);

    private OrderBuilder WithLineIfEmpty() =>
        _lines.Count == 0 ? WithLine("DEFAULT", 1, 10m) : this;
}

// each test states ONLY what matters to it; everything else is a sane default
var order = new OrderBuilder().Placed().WithLine("ABC", 3, 19.99m).Build();
```

- **A test should read as a specification.** Anything in the arrangement that is not relevant to the rule under test is noise that obscures it.
- **Builders survive constructor changes.** Adding a required parameter breaks one file instead of two hundred tests.
- **Defaults must be valid.** A builder that produces an invalid object by default makes every test start with unrelated setup.
- **Prefer builders to shared fixtures.** A shared fixture couples tests to each other; one test's expectations start constraining another's.
- **Name tests as the rule, not the method.** `A_shipped_order_cannot_be_cancelled` tells you what broke; `CancelTest2` does not.

Coverage tells you which lines ran, not whether the assertions would notice if they were wrong. Mutation testing answers the second question: it deliberately alters your code — flipping a comparison, removing a call, returning a default — then reruns the suite. A mutant that survives is a line your tests execute but do not actually check.

``` bash
dotnet tool install -g dotnet-stryker
dotnet stryker --project Shop.Domain.csproj

# Killed   82   the test suite caught the change
# Survived 14   ← these are the ones worth reading
# Timeout   2
# Mutation score: 85.4%
```

Run it on the domain layer only, and occasionally rather than in every build — it is slow by nature. The value is not the score; it is the list of survivors, which usually points at a branch everybody assumed was tested.

<a id="be-a191"></a>

#### Integration testing with real dependencies

Mocking a database tells you your mocks agree with each other. Integration tests run the real HTTP pipeline against a real database in a container, and they catch the bugs that actually reach production: wrong SQL, bad migrations, misconfigured serialization, broken auth wiring.

``` csharp
public class OrdersApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public OrdersApiTests(ApiFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Placing_an_order_returns_201_and_a_location()
    {
        var res = await _client.PostAsJsonAsync("/orders", new { customerId, lines });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.NotNull(res.Headers.Location);
    }
}

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().Build();

    public async Task InitializeAsync() => await _db.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder b) =>
        b.UseSetting("ConnectionStrings:Default", _db.GetConnectionString())
         .ConfigureTestServices(s => s.RemoveAll<IPaymentGateway>()
                                      .AddSingleton<IPaymentGateway, FakeGateway>());
}
```

Testcontainers gives you real PostgreSQL, SQL Server, Redis, RabbitMQ and Kafka as disposable Docker containers per test run — no shared test database, no cleanup scripts, no ‘works on my machine’.

| Level       | Count        | Speed        | Catches                            |
|-------------|--------------|--------------|------------------------------------|
| Unit        | Many         | Milliseconds | Logic errors in one class          |
| Integration | Some         | Seconds      | Wiring, SQL, serialization, config |
| Contract    | Per boundary | Seconds      | Producer/consumer schema drift     |
| End-to-end  | Few          | Minutes      | The critical user journeys only    |

###### Worked example

A complete, fast integration test setup: containers started once for the whole suite, database reset between tests, external HTTP stubbed, and the real pipeline exercised end to end.

``` csharp
// one container per test RUN, shared by every test class in the collection
public sealed class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine").WithReuse(true).Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public WebApplicationFactory<Program> App { get; private set; } = null!;
    private Respawner _respawner = null!;
    private NpgsqlConnection _conn = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_db.StartAsync(), _redis.StartAsync());

        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
            b.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
            b.ConfigureTestServices(s =>
            {
                // stub only what you cannot run: third-party HTTP, email, payments
                s.RemoveAll<IPaymentGateway>().AddSingleton<IPaymentGateway, FakeGateway>();
                s.AddSingleton<TimeProvider>(new FakeTimeProvider(TestClock.Start));
                // and bypass real auth with a test scheme
                s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        });

        using var scope = App.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();

        _conn = new NpgsqlConnection(_db.GetConnectionString());
        await _conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(_conn,
            new RespawnerOptions { DbAdapter = DbAdapter.Postgres,
                                   TablesToIgnore = ["__EFMigrationsHistory"] });
    }

    // ~20 ms instead of recreating the schema (~2 s) between tests
    public Task ResetAsync() => _respawner.ResetAsync(_conn);

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await Task.WhenAll(_db.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(nameof(AppCollection))]
public class AppCollection : ICollectionFixture<AppFixture>;
```

``` csharp
[Collection(nameof(AppCollection))]
public class OrdersTests(AppFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();     // clean database per test
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Placing_an_order_reserves_stock_and_returns_location()
    {
        await fx.SeedAsync(new Product("ABC", stock: 10));
        var client = fx.App.CreateClient();

        var res = await client.PostAsJsonAsync("/orders", new { sku = "ABC", qty = 3 });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.NotNull(res.Headers.Location);

        using var scope = fx.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Equal(7, (await db.Products.SingleAsync(p => p.Sku == "ABC")).Stock);
    }
}
```

Two details do most of the work for speed. `WithReuse(true)` keeps the container alive between runs on a developer machine, so the second run starts in a second rather than fifteen. Respawn deletes data rather than recreating the schema, which is roughly a hundred times faster and keeps a two-hundred-test suite under a minute.

###### What to stub and what to run for real

| Dependency           | In an integration test                       | Why                                                            |
|----------------------|----------------------------------------------|----------------------------------------------------------------|
| Your own database    | Real, in a container                         | Half the bugs you are testing for live in the SQL              |
| Redis                | Real, in a container                         | Cheap to run; serialization and key bugs are real              |
| Message broker       | Real, or an in-memory harness                | Real if you are testing consumers; in-memory for handler logic |
| Third-party HTTP API | Stubbed (WireMock.NET) or a fake             | You cannot depend on someone else's uptime in CI               |
| Email / SMS          | Fake that records                            | Assert on what would have been sent                            |
| Payment provider     | Their sandbox in a nightly run; a fake in CI | Sandbox latency and flakiness will ruin a per-commit suite     |
| Clock                | `FakeTimeProvider`                           | Time-dependent tests are otherwise flaky by construction       |
| Auth provider        | Test authentication scheme                   | Testing your policies, not the IdP's token issuance            |

``` csharp
// a test auth handler that lets each test choose its claims
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l,
                             UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = Request.Headers.TryGetValue("X-Test-Claims", out var raw)
            ? JsonSerializer.Deserialize<Dictionary<string,string>>(raw!)!
                .Select(kv => new Claim(kv.Key, kv.Value))
            : [new Claim(ClaimTypes.NameIdentifier, "test-user")];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}
```

<a id="be-a192"></a>

#### Contract testing and load testing

##### Contracts between services

In a microservices system, the integration test you cannot write is ‘does the other team's next release still work with mine?’. Consumer-driven contract testing (PactNet) solves it: the consumer records what it expects, the provider's CI replays those expectations and fails the build if it would break someone.

##### Load and performance testing

Test what you actually care about, which is usually not the average. Track p95 and p99, error rate, and throughput at a fixed latency target — “how many requests per second can we serve while staying under 300ms at p99?”.

``` csharp
// NBomber, in C#, so it lives in the repo with everything else
var scenario = Scenario.Create("place_order", async ctx =>
    {
        var res = await http.PostAsJsonAsync("/orders", Payload.Random());
        return res.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    })
    .WithLoadSimulations(
        Simulation.RampingInject(rate: 200, interval: TimeSpan.FromSeconds(1),
                                 during: TimeSpan.FromMinutes(5)));
```

| Test type | Question it answers                               |
|-----------|---------------------------------------------------|
| Load      | Does it hold up at expected peak?                 |
| Stress    | Where does it break, and how?                     |
| Soak      | Does it leak memory or connections over 12 hours? |
| Spike     | Does it survive 10× traffic for 60 seconds?       |

###### Testing the boundary, and testing the limit

``` text
  THE INTEGRATION TESTING GAP that contract tests fill

    consumer's tests    mock the provider      -> pass
    provider's tests    test itself            -> pass
    production          they disagree          -> outage

    both suites are green and neither tested the AGREEMENT.

  CONSUMER-DRIVEN CONTRACT TESTING

    1  the consumer writes a test describing what it NEEDS
    2  that produces a CONTRACT file (a pact)
    3  the PROVIDER's CI verifies it can satisfy every contract
    4  a provider change that breaks any consumer FAILS THE PROVIDER'S BUILD

  the property that matters: the provider finds out at BUILD time,
  from its own pipeline, rather than from a consumer's incident.
```

``` csharp
// provider verification - the half that catches breaks
[Fact]
public async Task Honours_all_consumer_contracts()
{
    await using var app = new WebApplicationFactory<Program>();
    var verifier = new PactVerifier(new PactVerifierConfig());

    verifier
        .WithHttpEndpoint(app.Server.BaseAddress)
        .WithPactBrokerSource(new Uri(brokerUrl), o => o
            .ConsumerVersionSelectors(new ConsumerVersionSelector { MainBranch = true }))
        .WithProviderStateUrl(new Uri(app.Server.BaseAddress, "/provider-states"))
        .Verify();
}
// lighter-weight alternative if a broker is too much: commit the generated
// OpenAPI document and diff it in CI. Catches shape changes, not semantics -
// but it catches most of what actually breaks people.
```

``` csharp
// load testing in C#, with NBomber - so the test lives with the code
var scenario = Scenario.Create("place_order", async ctx =>
    {
        var res = await client.PostAsJsonAsync("/orders", NewOrder());
        return res.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    })
    .WithWarmUpDuration(TimeSpan.FromSeconds(30))     // warm the cache and JIT
    .WithLoadSimulations(
        Simulation.RampingInject(rate: 200, interval: TimeSpan.FromSeconds(1),
                                 during: TimeSpan.FromMinutes(2)),
        Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1),
                          during: TimeSpan.FromMinutes(5)));   // STEADY state
```

| Load test mistake                  | Why it invalidates the result                                  |
|------------------------------------|----------------------------------------------------------------|
| No warm-up                         | You measured tier-0 JIT and a cold cache, not the steady state |
| Too little data                    | Everything fits in the buffer pool; index depth never matters  |
| Reporting the average              | A p99 of 8 s hides behind a mean of 40 ms                      |
| Finding the ceiling, not the knee  | Max throughput at 4-second latency is not a capacity number    |
| Same input every request           | Caches at every layer answer it; you tested the cache          |
| Testing from one machine           | The load generator saturates before the system does            |
| Not identifying *what* broke first | The useful output is the bottleneck component, not the number  |

> **\[Note\]**
>
> **The output of a load test is a bottleneck, not a number.** “We handle 1,200 requests per second” is nearly useless. “At 1,200 rps the database connection pool saturates, p99 goes to 4 seconds, and adding instances makes it worse” tells you exactly what to fix and what will happen at 2×. Run until something gives way, then identify precisely what gave way.

<a id="be-a193"></a>

#### Analyzers, conventions and enforcing them automatically

Every team agrees on standards in a meeting and then relitigates them in pull requests for the next two years. The fix is to make the build the arbiter: conventions that a machine checks are the only ones that survive contact with a deadline.

``` text
<!-- Directory.Build.props at the repo root — applies to every project -->
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <!-- fail the build on a package with a known CVE -->
    <NuGetAuditMode>all</NuGetAuditMode>
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

``` csharp
# .editorconfig — style AND severity, versioned with the code
[*.cs]
dotnet_diagnostic.CA2007.severity = none      # no ConfigureAwait in ASP.NET
dotnet_diagnostic.CA1848.severity = warning   # use LoggerMessage delegates
dotnet_diagnostic.VSTHRD002.severity = error  # no .Result / .Wait()
csharp_style_namespace_declarations = file_scoped:error

# banned APIs (BannedApiAnalyzers) — BannedSymbols.txt
# T:System.DateTime;Use TimeProvider or DateTimeOffset
# M:System.Threading.Tasks.Task.Result;Deadlock risk — await instead
```

Beyond style, two analyzer families earn their keep immediately: threading analyzers that ban `.Result` and `.Wait()`, and banned-API analyzers that stop a deprecated helper from spreading. Architecture tests then enforce the boundaries that no analyzer knows about.

``` csharp
[Fact]
public void Domain_depends_on_nothing()
{
    var result = Types.InAssembly(typeof(Order).Assembly)
        .Should().NotHaveDependencyOnAny(
            "MyApp.Infrastructure", "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore")
        .GetResult();

    result.IsSuccessful.Should().BeTrue(
        string.Join(", ", result.FailingTypeNames ?? []));
}
```

> **\[Trap\]**
>
> **Common trap.** Turning on `TreatWarningsAsErrors` in an existing codebase produces thousands of errors and gets reverted by Friday. Adopt it the way you adopt nullable: baseline the current state (`dotnet format --verify-no-changes` in CI, warnings-as-errors on new projects only), then ratchet one rule at a time. A standard nobody can build against is not a standard.

> **\[Why it matters\]** Automated conventions convert an entire category of review comments into build output. Reviewers stop spending attention on brace placement and `.Result` calls, and spend it on the thing a machine cannot check — whether the design is right.

### Testing APIs

<p class="src">From <em>Backend Engineering</em> · APIs · Testing APIs</p>

<a id="be-e544"></a>

#### Testing APIs

**Unit** — logic in isolation, dependencies mocked. Fast, plentiful.

**Integration** — the real HTTP pipeline. In .NET, `WebApplicationFactory<Program>` runs your whole app in-process with a real `HttpClient` — routing, model binding, filters, serialization, middleware. This catches the class of bug unit tests structurally cannot: a route typo, a missing `[FromBody]`, a serialization setting.

``` csharp
var client = _factory.CreateClient();
var resp = await client.GetAsync("/users/42");
Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
```

Use **Testcontainers** for real dependencies — a genuine PostgreSQL or Redis in Docker beats an in-memory fake that behaves differently under concurrency and transactions.

**Contract** — Pact or schema validation in CI, so a provider's breaking change fails the *provider's* build.

**What to test that people skip:**

- Authorization: a valid token for the *wrong* user must get 403
- Every documented error path, not just the happy one
- Pagination boundaries: empty, one page exactly, cursor at the end
- Idempotency: send the same key twice, assert one effect and identical responses
- Cancellation: client disconnects mid-request
- Malformed input: wrong types, missing fields, huge payloads, unexpected extra fields

**Load testing** answers a different question than functional testing: where does it break, and how? k6, NBomber, or Bombardier. The valuable output isn't a throughput number — it's *which* resource ran out first, and whether the failure was graceful.

------------------------------------------------------------------------

### Diagnostics

<p class="src">From <em>Backend Engineering</em> · async / await · Diagnostics</p>

<a id="be-c374"></a>

#### Testing async code

**Never block in tests either.** `[Fact] public async Task X()` — xUnit, NUnit, and MSTest all support async test methods returning `Task`. A test using `.Result` can deadlock in a test runner with its own SynchronizationContext, and will mislead you about production behavior regardless.

**Test cancellation explicitly.** It's the least-tested path and the most likely to be silently broken:

``` csharp
[Fact]
public async Task Honors_cancellation()
{
    using var cts = new CancellationTokenSource();
    var task = _sut.LongRunningAsync(cts.Token);
    cts.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => task);
}
```

**Make time deterministic.** `Task.Delay` in tests means slow, flaky tests. .NET 8 added `TimeProvider`:

``` csharp
var time = new FakeTimeProvider();
var sut = new Service(time);
var task = sut.WithTimeoutAsync();
time.Advance(TimeSpan.FromMinutes(5));   // instant, deterministic
await task;
```

**Watch for accidental sync completion.** A mock returning `Task.FromResult(x)` completes synchronously, so your test never exercises the suspension path — the race you're worried about can't happen. Force a real yield when testing concurrency:

``` csharp
mock.Setup(x => x.GetAsync()).Returns(async () => { await Task.Yield(); return value; });
```

This catches a genuine class of bug: code that works only because the await never actually suspended.

**Deadlock detection in CI.** Wrap suspicious tests in a timeout — `await task.WaitAsync(TimeSpan.FromSeconds(10))` — so a deadlock fails the build in ten seconds instead of hanging the agent.

------------------------------------------------------------------------

### Specialised & Advanced

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Specialised & Advanced</p>

<p class="pd">Capabilities beyond ordinary CRUD, plus the engineering practice around databases: procedural code, search, spatial and time-series data, change data capture, migrations, and a method for tuning.</p>

<a id="be-d480"></a>

#### Testing databases

Database logic is frequently the least tested part of a system, and it is where the most damaging bugs live.

| Test                   | Verifies                                 | How                                                     |
|------------------------|------------------------------------------|---------------------------------------------------------|
| Migration test         | The migration applies, and is idempotent | Run it against a restored production copy in CI         |
| Constraint test        | The database rejects invalid data        | Attempt the violation; assert it fails                  |
| Query correctness      | The SQL returns what you believe         | Seed a known dataset; assert exact results              |
| Isolation test         | Concurrency behaves under load           | Two sessions, deliberately interleaved                  |
| Tenant isolation       | No cross-tenant leakage                  | Seed two tenants; assert one never sees the other       |
| Performance regression | A query has not become slower            | Assert on plan shape or row counts, not wall-clock time |

``` sql
-- test the CONSTRAINT, not just the happy path. This is the test people skip,
-- and constraints are exactly the thing you must be able to rely on.
DO $$
BEGIN
    INSERT INTO bookings (room_id, period) VALUES (1, '[2026-07-01,2026-07-05)');
    BEGIN
        INSERT INTO bookings (room_id, period) VALUES (1, '[2026-07-03,2026-07-08)');
        RAISE EXCEPTION 'TEST FAILED: overlapping booking was accepted';
    EXCEPTION WHEN exclusion_violation THEN
        RAISE NOTICE 'PASS: overlap rejected';
    END;
END $$;
```

``` sql
-- a plan-shape assertion is far more stable than a timing assertion
EXPLAIN (FORMAT JSON) SELECT * FROM orders WHERE tenant_id = 1 AND status = 'Open';
-- assert in CI: the top-level node is an Index Scan, not a Seq Scan,
-- and estimated rows are within an order of magnitude of actual.
-- timing tests are flaky on shared CI runners; plan tests are deterministic.
```

- **Use a real database in a container**, at the same major version as production. An in-memory substitute tests your substitute.
- **Reset with data deletion, not schema recreation.** Truncating in dependency order is roughly a hundred times faster than re-migrating between tests.
- **Seed realistic volumes for performance tests.** A plan on 1,000 rows always chooses a scan and tells you nothing.
- **Test the constraints and the concurrency**, because those are the behaviours you cannot verify by reading the code.
- **Keep the test database's statistics fresh** — `ANALYZE` after seeding, or plans will be built on empty-table estimates.

### Evolving, testing, securing data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Evolving, testing, securing data</p>

<a id="be-m224"></a>

#### Testing data access

| Approach                     | Catches                                     | Misses                          |
|------------------------------|---------------------------------------------|---------------------------------|
| Mock `DbSet` / `IQueryable`  | almost nothing                              | everything about translation    |
| EF InMemory provider         | basic wiring                                | **all SQL semantics**           |
| SQLite in-memory             | real SQL, relational behaviour, constraints | provider-specific SQL and types |
| Testcontainers (real engine) | everything, including the plan              | nothing; costs seconds per run  |

> **\[Trap\]**
>
> **The InMemory provider is not a database** — Microsoft's own guidance now says not to use it for testing query behaviour. It has no relational semantics: no foreign keys, no unique constraints, no `NOT NULL` enforcement, case-sensitive comparison where your database is insensitive, no transactions, and — the decisive problem — it evaluates *any* LINQ expression, including ones that could never translate to SQL. A green test suite against InMemory tells you nothing about whether the query runs.

``` csharp
// SQLite in-memory: fast, real relational semantics, no infrastructure.
public sealed class DbFixture : IDisposable {
    private readonly SqliteConnection _conn;
    public DbFixture() {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();                            // the DB lives as long as this connection
        using var db = Create();
        db.Database.EnsureCreated();
    }
    public AppDb Create() => new(new DbContextOptionsBuilder<AppDb>()
        .UseSqlite(_conn).Options);
    public void Dispose() => _conn.Dispose();
}
```

``` csharp
// Testcontainers: the real engine, for anything provider-specific.
public sealed class SqlServerFixture : IAsyncLifetime {
    private readonly MsSqlContainer _c = new MsSqlBuilder().Build();
    public string ConnectionString => _c.GetConnectionString();
    public async Task InitializeAsync() {
        await _c.StartAsync();
        await using var db = Create();
        await db.Database.MigrateAsync();        // also tests your migrations
    }
    public async Task DisposeAsync() => await _c.DisposeAsync();
}
```

> **\[Tip\]**
>
> **The split that works.** SQLite for the many tests that exercise business logic through the data layer — milliseconds each, run on every save. Testcontainers against the real engine for a smaller suite covering provider-specific SQL, migrations, concurrency tokens, and collation — run in CI. Use a fresh database or a rolled-back transaction per test so they never share state, and never assert on row order without an explicit `ORDER BY` ([Comparison: IComparable, IComparer, stable ordering](02-03-types.md#be-m57)).

Worked example: [The review checklist these cases produce](14-05-what-actually-goes-wrong-field-guide-and-checklists.md#be-m306)

Applied in: [Integration testing an API](#be-m271)

<a id="be-m225"></a>

#### Contract tests for equality and comparers

A broken `GetHashCode` or an inconsistent `CompareTo` does not throw. It produces a dictionary that loses entries, a sort that returns garbage, or an exception thousands of lines away. These contracts are mechanically checkable, so check them once with a reusable helper.

``` csharp
public static class EqualityContract {
    public static void Verify<T>(T a, T aCopy, T b) {
        // Reflexive, symmetric, and consistent with the hash code.
        Assert.True(a!.Equals(a));
        Assert.True(a.Equals(aCopy) && aCopy!.Equals(a));
        Assert.False(a.Equals(b));
        Assert.False(a.Equals(null));

        // THE contract: equal implies equal hash. The converse is not required.
        Assert.Equal(a.GetHashCode(), aCopy.GetHashCode());

        // Stability - the hash must not change between calls.
        Assert.Equal(a.GetHashCode(), a.GetHashCode());

        // It must actually work as a key.
        var set = new HashSet<T> { a };
        Assert.Contains(aCopy, set);
        Assert.DoesNotContain(b, set);
    }
}

public static class ComparerContract {
    public static void Verify<T>(IComparer<T> cmp, T low, T mid, T high) {
        Assert.True(cmp.Compare(low, mid) < 0);
        Assert.True(cmp.Compare(mid, low) > 0);              // antisymmetric
        Assert.Equal(0, cmp.Compare(mid, mid));              // reflexive
        Assert.True(cmp.Compare(low, high) < 0);             // transitive
        // Sorting must not throw - the runtime detects inconsistent comparers.
        var many = Enumerable.Repeat(new[] { low, mid, high }, 400).SelectMany(x => x).ToArray();
        Array.Sort(many, cmp);
    }
}
```

``` csharp
// Property-based testing finds the inputs you would not have thought of.
// (FsCheck / CsCheck)
[Property] public void HashIsConsistentWithEquals(PriceKey a, PriceKey b) {
    if (a.Equals(b)) Assert.Equal(a.GetHashCode(), b.GetHashCode());
}

// And a distribution check catches the low-entropy hash from §302 directly:
[Fact] public void HashDistributesWell() {
    var keys = GenerateRealisticKeys(100_000);
    var distinct = keys.Select(k => k.GetHashCode()).Distinct().Count();
    Assert.True(distinct > keys.Count * 0.95, $"only {distinct} distinct hashes");
}
```

> **\[Tip\]**
>
> **Where to apply this.** Every type used as a dictionary key, a set member, or a sort key, and every custom `IComparer`/`IEqualityComparer`. It is roughly six lines per type and it forecloses an entire category of bug that is otherwise found only in production ([Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302), [Equality: ==, Equals, GetHashCode, IEquatable](02-03-types.md#be-m56)).

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

<a id="be-m226"></a>

#### Query and performance regression gates

Performance regressions arrive one innocuous commit at a time. The defences that work in CI are the cheap, deterministic ones — not benchmarks, which are too slow and too noisy on shared runners.

``` csharp
// 1. Assert the SQL a query generates. Deterministic, fast, and it fails loudly
//    when someone's refactor turns a projection into a full entity load.
[Fact] public void OrderList_QueryShape_IsStable() {
    var sql = _db.Orders.AsNoTracking()
        .Where(o => o.TenantId == 1)
        .Select(OrderRow.Projection)
        .ToQueryString();

    Assert.DoesNotContain("[Notes]", sql);           // no nvarchar(max) fetched
    Assert.DoesNotContain("LEFT JOIN", sql);
    Assert.Contains("TOP(", sql);
}
```

``` csharp
// 2. Assert the number of round trips. This is the single most valuable gate:
//    it catches N+1 the moment it is introduced.
[Fact] public async Task OrderList_IssuesOneCommand() {
    using var counter = _db.CountCommands();
    await _sut.GetOrders(tenantId: 1, page: 0);
    Assert.Equal(1, counter.Count);
}
```

``` csharp
// 3. Assert allocations, not time. Time is noisy on CI; allocation is exact.
[Fact] public void Parse_IsAllocationFree() {
    TryParse("A1,10.5,2026-01-01T00:00:00Z,SKU", out _);   // warm up
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++)
        TryParse("A1,10.5,2026-01-01T00:00:00Z,SKU", out _);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.True(allocated < 1024, $"allocated {allocated} bytes");
}
```

``` bash
// 4. Benchmarks run on a schedule, not per PR, and compare against a stored baseline.
dotnet run -c Release -- --filter '*' --exporters json
// then diff the JSON against the last known-good run and alert on a >10% shift.
```

> **\[Why it matters\]**
>
> **Why round-trip counts beat timings.** A command count is exact, deterministic, independent of machine speed, and maps directly to the dominant cost in a data-access application — network latency ([Rules of thumb, ranked](12-04-scaling-and-performance.md#be-m276)). A timing assertion on a shared CI runner either flaps until someone disables it, or has a threshold so loose it catches nothing. Assert on the thing that causes the cost, not on the cost.

> **\[Tip\]**
>
> Query Store on SQL Server gives you the production-side equivalent: query the top regressed queries between two time windows and alert on plan changes. That catches the regressions no test can — the ones caused by data growth and parameter sniffing rather than by code ([Reading a query plan without being a DBA](07-03-indexes-and-query-plans.md#be-m172)).

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)

Applied in: [Integration testing an API](#be-m271)

### Observability, testing, and delivery

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Observability, testing, and delivery</p>

<a id="be-m271"></a>

#### Integration testing an API

`WebApplicationFactory` starts your real application in-process — real DI, real middleware, real routing, real model binding — with an `HttpClient` wired to it and no network involved.

``` csharp
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime {
    private readonly MsSqlContainer _db = new MsSqlBuilder().Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => {
            // Replace ONLY what you must: external systems and time.
            services.RemoveAll<DbContextOptions<AppDb>>();
            services.AddDbContext<AppDb>(o => o.UseSqlServer(_db.GetConnectionString()));
            services.RemoveAll<IStockService>();
            services.AddSingleton<IStockService, FakeStockService>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestClock));
        });
    }
    public async Task InitializeAsync() {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
    }
    public new async Task DisposeAsync() => await _db.DisposeAsync();
}
```

``` csharp
[Fact]
public async Task Placing_an_order_returns_201_and_persists_it() {
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = TestAuth.For(tenantId: 1);

    var res = await client.PostAsJsonAsync("/orders", new {
        customerId = 7, lines = new[] { new { sku = "A-1", qty = 2 } } });

    Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    Assert.NotNull(res.Headers.Location);

    var dto = await res.Content.ReadFromJsonAsync<OrderDto>();
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.True(await db.Orders.AnyAsync(o => o.Id == dto!.Id && o.TenantId == 1));
}
```

> **\[Tip\]**
>
> **Replace as little as possible.** Every substituted service is a difference between the test and production, and differences are where bugs hide. Substitute external systems you do not control, and time ([Testing time: TimeProvider](#be-m68)). Keep your own DI graph, your own middleware, and a real database ([Testing data access](#be-m224)) — those are exactly what integration tests exist to exercise.

> **\[Trap\]**
>
> **Tests must not share state.** Either a fresh database per test class, or wrap each test in a transaction that is rolled back. Tests that pass alone and fail in a suite — or pass in one order and fail in another — are almost always shared rows, and the debugging cost far exceeds the setup cost of isolating them.

### .NET Aspire

<p class="src">From <em>Backend Engineering</em> · C# & .NET · .NET Aspire</p>

<p class="pd">Microsoft&#x27;s opinionated stack for building, running and observing multi-service .NET applications. It is the biggest change to how .NET solutions are composed since the generic host, and it is worth learning properly rather than as a demo.</p>

<a id="be-a181"></a>

#### Testing with Aspire

`Aspire.Hosting.Testing` spins up the entire app model inside a test run: real databases, real broker, real inter-service HTTP. It is the highest-fidelity integration test available short of a deployed environment.

``` csharp
public class OrderFlowTests
{
    [Fact]
    public async Task Placing_an_order_reserves_stock()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MySolution_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(h => h.AddStandardResilienceHandler());

        await using var app = await appHost.BuildAsync();
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();
        await notifications.WaitForResourceAsync("orders-api", KnownResourceStates.Running)
                           .WaitAsync(TimeSpan.FromSeconds(60));

        var client = app.CreateHttpClient("orders-api");
        var res = await client.PostAsJsonAsync("/orders", new { sku = "ABC", qty = 2 });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        // then poll the inventory service to assert the event was consumed
    }
}
```

**Use Aspire testing for**

- A handful of critical cross-service journeys
- Proving the outbox and consumers actually work
- Smoke tests before a release

**Keep using WebApplicationFactory for**

- Per-service API tests — far faster
- Anything you run on every commit
- Tests where you want to swap a dependency for a fake

###### Integration tests against the real thing

``` csharp
[Fact]
public async Task Places_an_order_end_to_end()
{
    // spins up the WHOLE AppHost: every service, every container
    var app = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.Orders_AppHost>();

    app.Services.ConfigureHttpClientDefaults(c => c.AddStandardResilienceHandler());
    await using var host = await app.BuildAsync();
    await host.StartAsync();

    // wait for readiness rather than sleeping
    await host.ResourceNotifications
        .WaitForResourceHealthyAsync("orders-api")
        .WaitAsync(TimeSpan.FromMinutes(2));

    var client = host.CreateHttpClient("orders-api");
    var res = await client.PostAsJsonAsync("/orders", new { customerId, lines });

    Assert.Equal(HttpStatusCode.Created, res.StatusCode);
}
```

**Aspire testing fits**

- End-to-end tests across several services
- Verifying the wiring itself — discovery, config, health
- Tests that must exercise real containers
- A smoke test proving the system starts at all

**Prefer Testcontainers or WebApplicationFactory when**

- Testing one service against one database
- You need many tests — startup cost dominates
- The test does not care about other services
- You are not using Aspire in the first place

| Cost            | Detail                                                                               |
|-----------------|--------------------------------------------------------------------------------------|
| Startup time    | Tens of seconds to minutes for the whole graph. One or two of these, not two hundred |
| Resource use    | Every container, on the test machine and on CI agents                                |
| Flakiness       | More moving parts. **Always wait on health, never on a timer**                       |
| CI requirements | The agent needs a working container runtime                                          |

> **\[Note\]**
>
> **One of these is worth a great deal.** A single test that starts the entire system and places one order catches an enormous class of problems — a missing registration, a wrong connection string name, a migration that fails, a service that cannot reach another. Run it on every pull request, keep the rest of your integration tests narrow and fast, and you get most of the confidence for a fraction of the runtime.

### Time, money, and measurement

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Time, money, and measurement</p>

<a id="be-m68"></a>

#### Testing time: TimeProvider

`DateTime.UtcNow` is a hidden global dependency. Code that calls it cannot be tested for expiry, timeout, scheduling, or anything else time-dependent without `Thread.Sleep` — which makes suites slow and flaky. `TimeProvider` (.NET 8) is the injectable abstraction.

``` csharp
public sealed class TokenService(TimeProvider time) {
    public Token Issue(string subject) => new(subject, time.GetUtcNow().AddMinutes(15));
    public bool IsValid(Token t) => t.ExpiresAt > time.GetUtcNow();
}

// Production wiring - one line:
builder.Services.AddSingleton(TimeProvider.System);
```

``` csharp
// Tests: Microsoft.Extensions.TimeProvider.Testing
[Fact]
public void Token_expires_after_fifteen_minutes() {
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    var sut = new TokenService(time);

    var token = sut.Issue("nam");
    Assert.True(sut.IsValid(token));

    time.Advance(TimeSpan.FromMinutes(14));
    Assert.True(sut.IsValid(token));

    time.Advance(TimeSpan.FromMinutes(2));       // instant, no sleeping
    Assert.False(sut.IsValid(token));
}
```

It abstracts more than "now" — delays and timers route through it too, so a test can drive a background loop deterministically:

``` csharp
await Task.Delay(TimeSpan.FromMinutes(5), timeProvider, ct);   // fake-time aware
using var timer = timeProvider.CreateTimer(Tick, null, dueTime, period);
long start = timeProvider.GetTimestamp();                       // monotonic, not wall clock
var elapsed = timeProvider.GetElapsedTime(start);
```

> **\[Tip\]**
>
> **Retrofit it at the seams, not everywhere.** You do not need to purge `DateTime.UtcNow` from an entire legacy codebase. Inject `TimeProvider` into the handful of classes whose behaviour depends on time — expiry, retry, scheduling, rate limiting, cache TTL — and leave logging and audit stamps alone. Those are the classes that were untestable, and they are usually a small minority.

> **\[Trap\]**
>
> **Wall clock versus monotonic.** `GetUtcNow()` can jump backwards — NTP correction, a VM resuming from a snapshot, a manual clock change. Any measurement of *elapsed* time must use `GetTimestamp`/`GetElapsedTime` (or `Stopwatch`), never a subtraction of two wall-clock readings. A negative duration in a metrics dashboard is this bug ([DateTime, DateTimeOffset, DateOnly, TimeSpan](02-03-types.md#be-m52)).
