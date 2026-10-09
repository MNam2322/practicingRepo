---
id: "13-2"
number: "13.2"
title: "Using them without losing skill"
part: 13
part_title: "Working with AI tools"
words: 3905
minutes: 17
---

# 13.2 Using them without losing skill

> **Quick look.** Using assistants heavily while keeping your own skills.
>
> - Generated code is your code: read everything and merge nothing you can't explain.
> - Resist automation bias; verify with the compiler, strict types, analyzers and running the code.
> - Write tests from the requirement: tests generated with the code share its assumptions.
> - Give context: goals, versions, existing code, conventions, errors and a repository instructions file; ask for a plan first.
> - Bound agents with least privilege, sandboxes, approvals, small commits and human review; check suggested packages exist.
> - Build skill: try first, ask for explanations and reviews, and practise without assistance.


### The big picture

Aviation learned something about automation decades before software did. Autopilots and flight management systems made flying safer and less tiring. They also brought a risk: crews who let automation do everything could lose their hand-flying skills, and could miss the moment it did something wrong. Aviation's answer wasn't to fly without automation. It was to keep pilots in the loop: monitor what the automation is doing, practise the skills it replaces, and know exactly when to take over. Regulators such as the US Federal Aviation Administration have urged airlines to give pilots regular manual flying practice for this reason.

Coding assistants raise the same question for programmers, and this chapter's answer is the same. Use them heavily, for what they do well. Verify everything they produce, with the tools from this guide: types, tests, reviews and scanners. Give them the context they need, and put boundaries around agents. And use them in ways that build your skill rather than replace it, which is what makes you able to catch their mistakes at all. The example shows the core habit. An assistant's suggestion passes its own example tests, and a property test then finds the case where it is wrong: the night the clocks change. This chapter ends the guide.

### Before you start

**Builds on:** 13.1 How coding assistants work, 2.3 Types, 9.3 TypeScript, 11.2 Builds and dependencies, 4.6 Testing, 11.6 Observability.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. An assistant's code compiles and its tests pass. Is it ready to merge?**

> Not yet. The tests may have been written to match the code, including its mistakes, and may miss the cases that matter. Read it, understand it, and test the actual requirement, including edge cases, before merging.

**2. What is automation bias?**

> The tendency to trust an automated system's output more than it deserves, and to stop checking it. It leads people to accept a confident suggestion even when their own knowledge says otherwise.

**3. What context should you give an assistant for a coding task?**

> The goal and the constraints, the versions of languages and frameworks, the relevant existing code and conventions, the exact error or failing test, and what "done" means. A repository instructions file can hold the standing parts.

**4. How can you use an assistant to learn without it doing your thinking?**

> Attempt the problem first, then compare. Ask it to explain, to give hints rather than answers, and to review your code. Re-implement important pieces yourself, and regularly work without it.

**5. What limits should a coding agent work within?**

> Least privilege: no production credentials, a sandbox for running commands, limited network and file access, approval for risky actions such as pushing or deleting, and small steps checkpointed in Git so work can be reviewed and undone.

**6. Why ask an assistant for a plan before code on a larger task?**

> A plan is quick to read and cheap to correct. Reviewing the approach first catches wrong assumptions before they are spread across hundreds of lines of generated code.

### Fast at what you understand

Assistants change the economics of writing code. Boilerplate, mechanical refactoring, translating code between languages, writing a first draft of tests, drafting SQL or regular expressions, and explaining an unfamiliar codebase all become much faster. So does exploring an API you have never used. For an experienced developer, that is a large gain.

The gain depends on understanding. When you know what correct code looks like, an assistant saves you typing, and you catch its mistakes in review. When you don't, it lets you produce code faster than you can judge it. Accepting generated code without reading it, sometimes called **vibe coding**, can be fine for a throwaway prototype. It is dangerous for anything that handles bookings, payments or passengers' data. The fundamentals in this guide are exactly what turns an assistant from a risk into a tool.

Assistants are weakest where their training and context are thinnest, as How coding assistants work (13.1) explained. That means design decisions that depend on things they can't see, such as your organization, its constraints and its history. It also means subtle concurrency and security code, from Parts 5 and 10, where plausible and correct differ most. And it means new versions and APIs after their knowledge cutoff, and your internal frameworks and conventions, unless you supply them.

### You own the code

The rule that makes everything else work is simple: code an assistant wrote is your code. You are responsible for it in review, in production and at three in the morning. So review it as you would a pull request from a capable new colleague whose work you can't yet fully trust: read every line, and don't merge anything you can't explain.

That takes some discipline against **automation bias**, the tendency to trust automated output more than it deserves, particularly when it is fluent and confident. Assistants are always fluent and confident. Aviation has a name for where that leads over time: **automation complacency**, when crews stop actively monitoring the automation because it is usually right, and so miss the moment it isn't. It helps to keep changes small, so each is quick to review properly. **Code review** by another person still applies to generated code, with the same standards as any other change.

### Verify, with the tools you have

Everything in this guide that catches mistakes also catches an assistant's mistakes.

The compiler and strict types catch invented members and wrong signatures, from Types (2.3) and TypeScript (9.3). Analyzers and warnings as errors, from Builds and dependencies (11.2), catch dangerous patterns. Tests are the most important tool, with one caution: tests generated together with the code tend to share its assumptions, so they can pass while both are wrong. Write, or at least read, the tests that state the requirement. Property-based testing, from Testing (4.6), checks a rule across many generated inputs instead of a few hand-picked ones, and is particularly good at finding the edge cases that plausible code misses, as the example shows. Running the code, and watching its logs and traces from Observability (11.6), catches what static checks can't.

Check facts the model might have invented. Look up unfamiliar APIs in the official documentation. Before adding any dependency it suggests, confirm the package exists, is the one you meant and is maintained, since attackers register the names assistants invent. And run the same security scans and secret scanning on generated code as on any other, from CI/CD pipelines (11.4).

### Seeing it for real

An assistant was asked for a function that returns when online check-in opens: 24 hours before departure. It suggested the function below, with two example tests, and both pass. A property test then checks the actual rule against 10,000 random departure times in 2026, in a time zone with daylight saving time, and the script also checks whether three suggested packages exist.

```typescript
// verify.ts: run with: node verify.ts   (checking code an assistant suggested, instead of trusting it)
process.env.TZ = "Europe/London";              // a time zone with daylight saving, like many airlines' home bases

// The assistant's suggestion for "online check-in opens 24 hours before departure"
function checkInOpensSuggested(departure: Date): Date {
  const opens = new Date(departure);
  opens.setDate(opens.getDate() - 1);          // "one day earlier", on the local calendar
  return opens;
}
const hoursBefore = (f: (d: Date) => Date, d: Date) => (d.getTime() - f(d).getTime()) / 3_600_000;

// 1. The two example tests the assistant wrote alongside it: both pass
for (const d of ["2026-06-15T09:00:00Z", "2026-12-01T18:30:00Z"])
  console.log(`example test, departure ${d}: opens ${hoursBefore(checkInOpensSuggested, new Date(d))} hours before`);   // 24, 24

// 2. A property test: the rule must hold for every departure time, so try many at random (4.6)
function random(seed: number) { return () => { seed = (seed * 1664525 + 1013904223) % 4294967296; return seed / 4294967296; }; }
function checkProperty(f: (d: Date) => Date, runs = 10_000) {
  const rand = random(2026);
  const start = Date.UTC(2026, 0, 1), span = 365 * 24 * 3_600_000;
  for (let i = 0; i < runs; i++) {
    const departure = new Date(start + Math.floor(rand() * span / 60_000) * 60_000);   // any minute of 2026
    const h = hoursBefore(f, departure);
    if (h !== 24) return `fails for departure ${departure.toISOString()}: opens ${h} hours before`;
  }
  return `holds for ${runs} random departures`;
}
console.log(`property test of the suggestion: ${checkProperty(checkInOpensSuggested)}`);

// 3. The fix: subtract an exact duration, not a calendar day
const checkInOpens = (departure: Date) => new Date(departure.getTime() - 24 * 3_600_000);
console.log(`property test of the fix: ${checkProperty(checkInOpens)}`);

// 4. Check that suggested packages exist before installing anything (results at the time of writing)
for (const name of ["zod", "fast-check", "efcore-query-helpers-ts"]) {
  const response = await fetch(`https://registry.npmjs.org/${name}`, { method: "HEAD" });
  console.log(`package ${name}: ${response.status === 200 ? "exists" : response.status === 404 ? "does not exist: don't install a lookalike" : response.status}`);
}
```

Its output, at the time of writing:

```text
example test, departure 2026-06-15T09:00:00Z: opens 24 hours before
example test, departure 2026-12-01T18:30:00Z: opens 24 hours before
property test of the suggestion: fails for departure 2026-03-29T03:54:00.000Z: opens 23 hours before
property test of the fix: holds for 10000 random departures
package zod: exists
package fast-check: exists
package efcore-query-helpers-ts: does not exist: don't install a lookalike
```

The suggestion is the kind of code assistants produce often: short, readable, idiomatic and subtly wrong. Subtracting a calendar day in local time gives 23 hours on the night the clocks go forward, 29 March 2026 in the UK, and 25 on the night they go back, the time zone trap from Numbers inside the machine (1.2). Both example tests used ordinary dates, so they passed. The property test found the problem because it states the rule itself, exactly 24 hours, for any departure. The fix subtracts a duration instead. The test also made the requirement precise: if the business had meant "the same clock time the day before", the original would have been right, and the test would have been the thing to change. Writing the property is where you decide.

The package check is the defence against invented dependencies from How coding assistants work (13.1). The third name is exactly the kind an assistant might suggest. It didn't exist when this was written, and if it exists when you run the script, find out who published it before trusting it.

### Give it what it can't see

Most poor suggestions come from missing context, and most of that context is cheap to supply. State the goal, and what "done" means. Give the versions: .NET 10, EF Core 10, Angular 21. Include the relevant existing code, the interfaces it must fit and the conventions it must follow. Paste the exact error message or failing test. Say what not to do, such as adding dependencies or changing public APIs.

For larger tasks, ask for a plan first, and review it before any code is written: a wrong assumption is much cheaper to correct in five lines of plan than in five hundred lines of code. Ask for alternatives and their trade-offs when there is a real design choice. Ask it to write the tests, and check that they test the requirement, not the implementation. And iterate: treat the first answer as a draft.

Most assistants read a **repository instructions file** from the repository, such as `CLAUDE.md`, `AGENTS.md` or `.github/copilot-instructions.md`. It is the place for standing context: how to build and test, architecture rules, conventions, what to avoid. Keep it short and current, and review changes to it like code.

### Working with agents

Agents, from How coding assistants work (13.1), can carry out whole tasks: implementing a feature, fixing a failing test, upgrading a package across a solution. They need the same boundaries as any new team member with access to your systems, and a few more.

Scope each task tightly, with clear **acceptance criteria**, the conditions that must hold for the task to count as done, ideally expressed as tests that must pass. Run agents with least privilege, from Thinking like an attacker (10.1). That means no production credentials, and a sandbox or container for commands, from Containers and Docker (11.3). It also means approval for actions that are risky or hard to undo, such as pushing, deleting, or calling external services. Have the agent work on a branch, committing in small steps, so every change can be reviewed and reverted with Git, from Version control with Git (11.1). Remember prompt injection: an agent that reads issues, web pages or dependency code can be steered by instructions hidden in them. Keep **human in the loop** review at the points that matter, above all before anything reaches the main branch or production.

### Learning with an assistant, not instead of it

Skills you stop using fade. That is fine for skills you will never need again, and a problem for the ones you need in order to judge the assistant's work: reading code closely, reasoning about concurrency and failure, designing data, debugging. The goal is to use assistants in ways that build these skills.

Some habits help. Try first, then compare: write your own attempt, even a rough one, before asking, and then study the differences. Ask for explanations rather than answers: why does this deadlock, what does this query plan mean, what would go wrong here. Ask for hints, or to be questioned about your design, instead of for the solution. Ask it to review your code rather than to write it. When it does write something important, read it line by line until you could have written it, and re-implement key pieces by hand now and then. And, like pilots practising manual flying, work without assistance regularly: the "Check yourself" exercises in this guide are meant to be done that way. This is **deliberate practice**: working just beyond what you can already do, with feedback, rather than repeating what is easy.

### In a team

Teams need shared rules, not only individual habits. Follow your organization's policy on which tools are approved, and on what code and data may be sent to them. Customer data, secrets and confidential code often may not. Hold generated code to the same review and quality standards as any other. Be open about where assistants were used, as your team's practice requires. Be aware that licensing and intellectual property questions about generated code are still evolving, and follow your organization's guidance. And measure what matters: working, maintainable software reaching users, not lines of code produced.

### Where this leaves you

As assistants write more of the typing, the human part of software work shifts towards what they can't do reliably. People decide what should be built and state it precisely. They design the boundaries, data and failure behaviour, and judge whether code is correct, secure and fast enough. And they take responsibility for what reaches production. Every one of those depends on understanding what happens underneath. That is what this guide set out to give you, from the bits and bytes of Part 1 to the systems of Part 12.

A working program is a stack of ideas. Numbers and text become bytes in memory. Code becomes instructions through compilers and runtimes, and data structures decide what is cheap and what is expensive. Designs keep code changeable, concurrency keeps machines busy, and networks and protocols carry requests between machines. Databases keep data correct, backends and frontends turn it into services, security keeps them trustworthy, pipelines ship them, and architecture and reliability keep them running. Assistants don't remove any of those layers; they make it faster to build on them, and easier to build on them badly. Knowing the layers is what lets you tell the difference.

Keep using the guide. Each chapter's skip check is a quick way to find the gaps worth revisiting. The glossary collects every term in one place, and the "Check yourself" exercises are the practice that makes it stick.

### In short

Use assistants heavily for what they do well, and remember that generated code is your code. Read everything, and merge nothing you can't explain. Resist automation bias. Verify with the compiler, strict types, analyzers, tests written from the requirement, property-based tests, running the code, documentation and dependency checks, because tests generated with the code share its assumptions. Supply context: goals, versions, existing code, conventions, errors and a repository instructions file, and ask for plans before code on larger tasks. Bound agents with least privilege, sandboxes, approvals, small commits and human review. Build skill rather than replace it: try first, ask for explanations and reviews, and practise without assistance.

### What goes wrong

**Merging what you can't explain.** Plausible code with a subtle bug reaches production, and nobody understands it well enough to fix it quickly. Read and understand every line, or don't merge it.

**Trusting generated tests.** Tests written with the code encode its mistakes and pass. Write or review tests from the requirement, including edge cases and properties.

**Missing context.** Suggestions use the wrong version, ignore conventions or duplicate existing code. Give versions, code and rules, and keep an instructions file.

**Huge generated changes.** A thousand-line change is reviewed in a minute, which means it isn't reviewed. Work in small steps.

**Installing suggested packages blindly.** An invented or malicious package enters the build. Check existence, publisher and maintenance first.

**Unbounded agents.** An agent with broad credentials follows injected instructions. Sandbox it, limit its permissions and approve risky actions.

**Confidential data in unapproved tools.** Customer data or secrets are pasted into a chat. Follow your organization's AI policy.

**Letting skills fade.** After a year of accepting suggestions, debugging without help feels impossible. Try first, study the differences and practise without assistance.

### Cost model

Assistants shift cost from writing code to checking it. Generating a function takes seconds; reviewing it properly takes minutes, and that review is the part that can't be skipped. The saving is largest for code that is quick to verify, such as boilerplate, mechanical changes, tests and code in well-known patterns. It is smallest, or negative, for code that is slow to verify, such as concurrency, security and subtle business rules, where a plausible bug can cost days in production. Property tests and strict types cost little to add and make every future change, human or generated, cheaper to verify.

### In other languages (optional)

**Every language.** The habits in this chapter are language-independent. Languages with strong static types, such as C# and TypeScript with strict settings, give the compiler more ways to catch generated mistakes than dynamically typed code does, which is one practical reason to keep types strict.

**Property testing tools.** FsCheck and CsCheck bring property-based testing to .NET, fast-check to TypeScript, Hypothesis to Python and jqwik to Java; each shrinks a failing case to the smallest input that still fails.

### Key terms

- **Vibe coding**: Accepting AI-generated code without reading or understanding it.
- **Automation bias**: Trusting an automated system's output more than it deserves, and no longer checking it.
- **Automation complacency**: No longer actively monitoring automation because it is usually right, and so missing its failures.
- **Acceptance criteria**: The conditions that must hold for a task to count as done, ideally expressed as tests.
- **Code review**: Another person reading a change before it is merged, to check its correctness, design and safety.
- **Repository instructions file**: A file in a repository that gives AI assistants standing context, such as build steps and conventions.
- **Human in the loop**: Keeping a person responsible for reviewing and approving an automated system's important actions.
- **Deliberate practice**: Practising just beyond your current ability, with feedback, to build skill.

### Where this leads

This is the last chapter of the guide. To revisit the foundations, start again from Bits and bytes (1.1), or use the skip checks to find the chapters worth rereading. The verification tools this chapter relies on are in Testing (4.6), TypeScript (9.3) and CI/CD pipelines (11.4), and how assistants work underneath is in How coding assistants work (13.1).

### Check yourself

1. In `verify.ts`, change the time zone to one without daylight saving, such as `Asia/Singapore`, and rerun. Explain why the property test now passes the suggestion, and why that makes it more dangerous, not less.
2. Write a property test for a function of your own, stating a rule that must hold for every input, and run it with FsCheck or fast-check.
3. Ask an assistant to write a function and its tests for a rule you know well, then write your own tests from the rule and see whether its code passes them.
4. Write a repository instructions file for a project you work on, with build and test commands, architecture rules and conventions, in under a page.
5. Pick one exercise from an earlier chapter's "Check yourself" and do it without any assistant, then use one to review your answer.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Reviewing a generated change you can't explain

An assistant produces a 400-line refactor that passes tests. You understand about half of it.

*A review routine*

```text
1. Ask for a plan first, then small commits that each do one thing
2. Read every line; anything you can't explain, ask about or rewrite
3. Write tests from the requirement yourself (generated tests share the code's assumptions)
4. Run it: the compiler, analyzers, the app, and a real database
5. Check every new package and API against its documentation
```

> **Lesson:** Generated code is your code. Merge nothing you couldn't defend in review.

#### Example 2: Giving the assistant the context it needs

Suggestions keep using outdated EF Core APIs and the wrong logging library.

*A repository instructions file*

```markdown
# CLAUDE.md / copilot-instructions.md
- .NET 10, C# 14, EF Core 10, ASP.NET Core minimal APIs
- Logging: ILogger with message templates (no Serilog-specific APIs in code)
- Data access: EF Core for writes, Dapper for reporting queries in /Reporting
- Never use .Result/.Wait(); always pass CancellationToken
- Tests: xUnit + Testcontainers (SQL Server), no InMemory provider
```

> **Lesson:** Assistants only know what they're shown. Write down versions and conventions once and keep them in the repo.
