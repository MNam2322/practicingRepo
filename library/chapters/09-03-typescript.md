---
id: "9-3"
number: "9.3"
title: "TypeScript"
part: 9
part_title: "Building a frontend"
words: 4103
minutes: 18
---

# 9.3 TypeScript

> **Quick look.** TypeScript adds structural static types to JavaScript, checked at compile time only.
>
> - Types check your code, not the data that arrives: validate outside data and type it as unknown.
> - Let inference work; annotate parameters and public returns.
> - Unions with narrowing model closed sets; a never check makes switches exhaustive.
> - Brands keep look-alike ids apart; generics with keyof and utility types build precise types.
> - Avoid any; treat as and ! as unchecked promises; use as const and satisfies.
> - Turn on strict and noUncheckedIndexedAccess; generate API types from the backend's OpenAPI contract.


### The big picture

TypeScript is JavaScript with a static type system added on top. It was released by Microsoft in 2012, designed by Anders Hejlsberg, the lead designer of C#, and it now dominates serious frontend work and much of Node.js. Angular requires it, React projects mostly use it, and it is the main reason large JavaScript codebases stay maintainable.

For a C# developer, TypeScript feels familiar and subtly different. Its types are structural rather than nominal, as Types (2.3) explained. Its type system is much more expressive, able to describe unions of shapes, exact string values and types computed from other types. And it exists only at compile time: the types are removed before the code runs, so they are a powerful checking tool, not a run-time guarantee. This chapter covers the features you use every day and the settings that make the checking strict. It also covers the escape hatches that quietly switch checking off. The example was run with Node and type-checked, and the lines marked as compile errors were confirmed to be errors, with the real messages quoted.

### Before you start

**Builds on:** 2.3 Types, 2.2 Compilers, interpreters and JIT, 8.4 A Node and TypeScript backend, 8.5 Validation and errors at the boundary, 8.2 API design.

**Ideas explained later:**

- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **React** (9.5): A JavaScript library for building interfaces from components, created at Facebook.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is narrowing?**

> The compiler working out a more specific type inside a branch, from a check such as `typeof x === "string"`, `x !== null` or `switch (status.kind)`. After `if (status.kind === "delayed")`, the compiler knows `status` has a `delayMinutes` property.

**2. What is the difference between any and unknown?**

> `any` switches type checking off for that value: anything goes, including mistakes. `unknown` means "could be anything", and the compiler refuses to let you use it until you narrow it with a check. Use `unknown` for data from outside.

**3. How do you make the compiler complain when a switch misses a case of a union?**

> Add a default branch that passes the value to a function taking `never`. If every case is handled, the value's type there is `never`; if one is missing, it isn't, and the call doesn't compile.

**4. What does as Booking actually do at run time?**

> Nothing. A type assertion tells the compiler to trust you; it doesn't check or convert the value. If the value isn't really a `Booking`, the program fails later, at run time.

**5. What does satisfies do that a type annotation doesn't?**

> It checks that a value matches a type while keeping the value's own, more precise type. `const baggage = {...} satisfies Record<Cabin, number>` errors if a cabin is missing, yet still knows the exact keys.

**6. Why are TypeScript enums often avoided in new code?**

> They generate JavaScript code, so they don't work with type stripping, and they behave differently from the rest of the type system. A union of string literals, often derived from an `as const` array, does the same job with no generated code.

**7. Two types have the same properties but different names. Can a value of one be used where the other is expected?**

> Yes. TypeScript is structural: types are compatible when their shapes match. To make look-alike ids incompatible, add a "brand", a fake property that exists only in the type.

### What TypeScript is

**TypeScript** is a superset of JavaScript: every JavaScript program is valid TypeScript, and TypeScript adds optional type annotations and a compiler that checks them. The output is ordinary JavaScript with the types removed, as Compilers, interpreters and JIT (2.2) described. It can be produced by the TypeScript compiler, `tsc`, by fast tools such as esbuild that only strip types, or by running TypeScript directly on recent Node versions, as A Node and TypeScript backend (8.4) showed. The compiler itself was rewritten in Go for TypeScript 7, which made type checking several times faster.

Because the types disappear, TypeScript can't check what arrives at run time. The core habits follow from that: let the compiler check everything inside your code, and check data from outside with a schema, as Validation and errors at the boundary (8.5) described.

### Annotations and inference

A **type annotation** states a type after a colon: `let delay: number = 0`, `function book(flight: string): Booking`. Most of the time you don't write them, because the compiler infers types, as C# does with `var`. `const delays = [120, 45]` is inferred as `number[]`, and a function's return type is inferred from its `return` statements.

Annotate function parameters, which can't be inferred, and public function return types, which makes intent explicit and error messages clearer. Let inference handle the rest.

A **type alias** names a type: `type Booking = { id: string; flight: string }`. An `interface` declaration does much the same for object shapes, and can be extended and merged. The two are interchangeable for most uses. Many teams use `type` everywhere for consistency, and `interface` when they want declaration merging or class `implements`.

### Unions and narrowing

A **union type**, written with `|`, says a value is one of several types: `string | null`, or `Scheduled | Delayed | Departed`. It is TypeScript's form of the discriminated unions from Types (2.3), and one of its most useful features.

A value of a union type can only be used in ways valid for every member, until the code checks which one it is. The compiler follows those checks and works out the more specific type inside each branch. This is **narrowing**. `typeof x === "string"`, `x !== null`, `"delayMinutes" in x`, `x instanceof Date` and comparisons on a shared tag property all narrow. When each member has a property with a distinct fixed value, such as `kind: "delayed"`, a `switch` on that property narrows to exactly one member in each case.

A **literal type** is a type with a single value: `"delayed"`, `42`, `true`. Unions of string literals, such as `"economy" | "premium" | "business"`, replace most uses of enums.

### Exhaustiveness

When a union grows a new member, every `switch` over it should be updated. An **exhaustiveness check** makes the compiler enforce that. The type `never` has no values. In the `default` branch of a `switch` that handles every member, the narrowed type of the value is `never`. Passing it to a function that only accepts `never`, often called `assertNever`, compiles only while every case is handled. Add a member and forget a case, and that line becomes an error, as the example confirms.

### Structural typing and brands

TypeScript checks shapes, not names: a value fits a type if it has the right properties. That is flexible, and it means two different concepts with the same shape are interchangeable by accident. A `FlightId` and an `AircraftId` that are both plain `string` types can be swapped without complaint.

A **branded type** fixes that. It adds a property that exists only in the type system, such as `{ readonly __brand: "FlightId" }`, intersected with `string`. No real value has that property, so you create branded values through a small function with a type assertion, ideally one that also validates. After that, the compiler keeps the two kinds of id apart, giving you the strongly typed ids of Types (2.3) in a structural language.

### Generics

Generics work as in C#: `function first<T>(items: T[]): T | undefined`. Constraints use `extends`: `K extends keyof T` means `K` must be one of `T`'s property names. The `keyof` operator produces the union of a type's keys, and `T[K]`, an indexed access type, is the type of property `K`. Together they let a function like `pluck(items, "km")` return exactly `number[]`, and reject keys that don't exist.

### Building types from types

TypeScript can compute new types from existing ones, which avoids writing near-duplicates by hand. Built-in **utility types** cover the common cases. `Partial<T>` makes every property optional, `Required<T>` makes every property required, `Readonly<T>` makes every property read-only, `Pick<T, K>` keeps some properties, `Omit<T, K>` drops some, and `Record<K, V>` builds an object type from a set of keys and a value type. `ReturnType<F>` and `Awaited<T>` extract result types.

Underneath them are two more general tools. A **mapped type** transforms each property of a type, as in `{ [K in keyof T]?: T[K] }`, which is how `Partial` is written. A **conditional type** chooses a type based on a test, as in `T extends string ? "text" : "other"`. Application code rarely needs to write these, but library types use them heavily, and reading them helps when an error message mentions them.

### unknown, any and assertions

**unknown** is the type of a value you know nothing about, such as the result of `JSON.parse`. The compiler won't let you use it until you narrow it. A **type guard** is a function whose return type is a predicate, `value is Booking`, which narrows its argument wherever it returns true. It is a clean way to check data from outside, though a schema library writes these checks for you.

**any** switches checking off completely. Every use of it compiles, including mistakes, and it spreads: whatever you compute from an `any` is `any` too. Keep it out of application code; linters can forbid it.

A **type assertion**, `value as Booking`, tells the compiler to treat a value as a type without checking. It is sometimes necessary, as in the brand functions, and it is always a promise the compiler can't verify. The **non-null assertion**, a `!` after an expression, is a type assertion that a value isn't null or undefined, as in C#. The example ends with an assertion that is a lie, and the program fails at run time.

### Two newer tools

An **as const** assertion tells the compiler to infer the narrowest possible type for a literal: `["economy", "premium", "business"] as const` is a read-only tuple of those exact strings, not `string[]`. Indexing it with `[number]` gives the union of its values, a common replacement for an enum that also gives you the list at run time.

The **satisfies** operator checks a value against a type without changing the value's inferred type. `const baggageKg = {...} satisfies Record<Cabin, number>` fails to compile if a cabin is missing or misspelled, yet `baggageKg` keeps its exact type. An annotation, `: Record<Cabin, number>`, would check too, but would widen the value's type to the annotation.

### Seeing it for real

This file runs with Node, which strips the types, and passes a strict type check. The commented-out lines are real compile errors: each one was un-commented and checked, and the messages are below.

```typescript
// types.ts: run with: node types.ts      check with: npx tsc --noEmit (strict settings, see tsconfig below)

// 1. A closed set of cases, and narrowing: the compiler knows which case you are in
type Scheduled = { kind: "scheduled"; departs: string };
type Delayed = { kind: "delayed"; departs: string; delayMinutes: number };
type Departed = { kind: "departed"; departedAt: string };
type FlightStatus = Scheduled | Delayed | Departed;          // a union type

function describe(status: FlightStatus): string {
  switch (status.kind) {
    case "scheduled": return `on time at ${status.departs}`;
    case "delayed": return `delayed ${status.delayMinutes} min`;   // here status is known to be Delayed
    case "departed": return `departed at ${status.departedAt}`;
    default: return assertNever(status);                      // add a case to the union and this stops compiling
  }
}
function assertNever(value: never): never { throw new Error(`unexpected: ${JSON.stringify(value)}`); }
console.log(describe({ kind: "delayed", departs: "08:25", delayMinutes: 45 }));   // delayed 45 min

// 2. Structural typing: shapes match, whatever they are called; a "brand" makes look-alike ids distinct
type FlightId = string & { readonly __brand: "FlightId" };
type AircraftId = string & { readonly __brand: "AircraftId" };
const flightId = (s: string) => s as FlightId;
const aircraftId = (s: string) => s as AircraftId;
function assign(flight: FlightId, aircraft: AircraftId) { return `${flight} on ${aircraft}`; }
console.log(assign(flightId("BA117"), aircraftId("G-VIIA")));   // BA117 on G-VIIA
// assign(aircraftId("G-VIIA"), flightId("BA117"));           // compile error: the arguments are swapped

// 3. Generics with keyof: the result type follows the key you pass
function pluck<T, K extends keyof T>(items: T[], key: K): T[K][] { return items.map(item => item[key]); }
const legs = [{ from: "LHR", to: "SIN", km: 10880 }, { from: "SIN", to: "SYD", km: 6300 }];
const distances = pluck(legs, "km");                          // number[]
console.log(distances.reduce((a, b) => a + b, 0));           // 17180
// pluck(legs, "gate");                                       // compile error: "gate" is not a key of a leg

// 4. Utility types build new types from existing ones
type Booking = { id: string; flight: string; passenger: string; seat?: string };
type NewBooking = Omit<Booking, "id">;                        // everything except id
type BookingPatch = Partial<Pick<Booking, "passenger" | "seat">>;   // optional passenger and seat
const request: NewBooking = { flight: "BA117", passenger: "Ada" };
const patch: BookingPatch = { seat: "12A" };
const seatsByCabin: Readonly<Record<"economy" | "business", number>> = { economy: 180, business: 40 };
console.log(request.flight, patch.seat, seatsByCabin.business);   // BA117 12A 40

// 5. unknown must be checked before use; a type guard narrows it
function isBooking(value: unknown): value is Booking {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.id === "string" && typeof v.flight === "string" && typeof v.passenger === "string";
}
const fromNetwork: unknown = JSON.parse('{"id":"B1","flight":"BA117","passenger":"Ada"}');
// console.log(fromNetwork.passenger);                        // compile error: fromNetwork is unknown
if (isBooking(fromNetwork)) console.log(fromNetwork.passenger);   // Ada: narrowed to Booking

// 6. as const and satisfies: exact literal types, checked against a shape
const cabins = ["economy", "premium", "business"] as const;
type Cabin = (typeof cabins)[number];                         // "economy" | "premium" | "business"
const baggageKg = { economy: 23, premium: 32, business: 64 } satisfies Record<Cabin, number>;
console.log(baggageKg.business);                              // 64; a missing or misspelled cabin is a compile error

// 7. Indexing may miss: with noUncheckedIndexedAccess, items[i] might be undefined
const codes = ["LHR", "JFK"];
const third = codes[2];                                       // type: string | undefined
console.log(third?.toLowerCase() ?? "no third code");         // no third code

// 8. An assertion is a promise to the compiler that nothing checks at run time
const unchecked = JSON.parse("{}") as Booking;                // compiles, but it is a lie
try { console.log(unchecked.flight.toUpperCase()); }
catch (e) { console.log(`run time: ${(e as Error).constructor.name}`); }   // run time: TypeError
```

The compiler's messages for the four deliberate mistakes:

```text
swapped ids:   TS2345: Argument of type 'AircraftId' is not assignable to parameter of type 'FlightId'.
unknown key:   TS2345: Argument of type '"gate"' is not assignable to parameter of type '"from" | "km" | "to"'.
unknown used:  TS18046: 'fromNetwork' is of type 'unknown'.
missing case:  TS2345: Argument of type 'Departed' is not assignable to parameter of type 'never'.
```

The last one is the exhaustiveness check at work: with the `departed` case removed, the value reaching `assertNever` can still be a `Departed`, and the compiler says so.

### Configuration

A project's **tsconfig.json** sets which files to compile and how strictly. These settings give the most checking:

```json
{
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "verbatimModuleSyntax": true,
    "erasableSyntaxOnly": true,
    "target": "es2022",
    "module": "nodenext",
    "noEmit": true
  }
}
```

`strict` turns on a family of checks, the most important being strict null checks, which make `null` and `undefined` separate from other types, like C#'s nullable reference types from Types (2.3). `noUncheckedIndexedAccess` makes array and dictionary lookups include `undefined` in their type, because a lookup can miss. `verbatimModuleSyntax` and `erasableSyntaxOnly` keep the code compatible with type stripping, as A Node and TypeScript backend (8.4) explained. Frameworks such as Angular and Vite-based React templates generate a sensible `tsconfig.json` for you; check that `strict` is on.

### Types for JavaScript libraries

Many npm packages are written in TypeScript and ship their own types. Others are plain JavaScript and publish types separately, in a **declaration file**, a `.d.ts` file that describes a module's types with no code. The DefinitelyTyped project publishes these under the `@types` scope: `@types/express`, `@types/node`. When a package has no types at all, you can write a small `.d.ts` file for the parts you use, rather than falling back to `any`.

### Sharing types with the backend

Frontends and backends need to agree on the shapes of requests and responses. Writing the same types by hand on both sides lets them drift. Better options are generating TypeScript types from the backend's OpenAPI document, from API design (8.2), with tools such as openapi-typescript or NSwag, or, with a TypeScript backend, sharing a package of types and schemas between the two. Remember that shared types still describe what should arrive, not what does: validate responses at the edge if a mismatch would be costly.

### In short

TypeScript adds a static, structural type system to JavaScript and removes it before running, so it checks your code but not the data that arrives. Let inference do most of the work, and annotate parameters and public returns. Unions and narrowing model closed sets of cases, and a `never` check makes switches exhaustive. Brands keep look-alike ids apart, generics with `keyof` keep functions precise, and utility types build new types from old. Use `unknown` for outside data, avoid `any`, and treat `as` and `!` as unchecked promises. Use `as const` and `satisfies`, turn on `strict` and `noUncheckedIndexedAccess`, and generate API types from the backend's contract.

### What goes wrong

**Trusting types for outside data.** A response typed as `Booking` arrives with a different shape and fails deep in the UI. Validate at the edge, or use type guards or schemas.

**`any` spreading through the code.** One `any` from an untyped library or a lazy cast makes everything derived from it unchecked. Use `unknown` and narrow, and forbid `any` with a linter rule.

**Assertions as fixes.** `as Booking` and `!` are added to silence errors, and hide real bugs. Fix the type or check the value instead.

**Strictness switched off.** A project without `strict` lets `null` flow everywhere unchecked. Turn it on, and fix errors file by file.

**Switches that go stale.** A new status is added and one screen silently shows nothing for it. Use exhaustiveness checks.

**Look-alike ids swapped.** Two string ids are passed in the wrong order and the compiler can't tell. Brand them.

**Types that don't match the API.** Hand-written frontend types drift from the backend. Generate them from the OpenAPI document.

**No type check in the build.** Code is bundled with a type-stripping tool and never checked. Run `tsc --noEmit` in the pipeline.

### Cost model

TypeScript's types cost nothing at run time: they are removed before the code runs. The costs are at build time and in the editor. Type checking a large project can take seconds to minutes, far less since the compiler was rewritten in Go for version 7. Very complex computed types slow the compiler and produce unreadable errors, so keep application types simple and leave advanced type programming to libraries. The payoff is large: whole categories of bugs, such as typos in property names, missing null checks and forgotten cases, are caught before the code ever runs.

### In other languages (optional)

**C#.** Nominal typing, reified generics and run-time type information, so a `Booking` really is a `Booking` at run time. TypeScript's unions, literal types and mapped types have no direct C# equivalent, though C# is gaining closed type hierarchies and pattern matching covers much of the same ground.

**JavaScript with JSDoc.** Types can be written as comments in plain JavaScript files and checked by the TypeScript compiler, which some projects prefer to avoid a build step.

**Python.** Type hints checked by mypy or pyright play a similar role: optional, checked by tools, ignored at run time.

**Flow.** Facebook's earlier type checker for JavaScript, now rarely chosen for new projects.

### Key terms

- **TypeScript**: JavaScript with a static type system added, checked at compile time and removed before running.
- **Type annotation**: An explicit type written after a colon, as in let delay: number.
- **Type alias**: A name for a type, declared with type.
- **Union type**: A type that is one of several types, written with |, such as string | null.
- **Literal type**: A type with exactly one value, such as "delayed" or 42.
- **Narrowing**: The compiler working out a more specific type inside a branch from a check.
- **Exhaustiveness check**: Using never to make the compiler reject a switch that misses a case of a union.
- **Branded type**: A type with a fake property that exists only in the type system, keeping look-alike values apart.
- **Utility type**: A built-in type that builds a new type from another, such as Partial or Omit.
- **Mapped type**: A type that transforms each property of another type.
- **Conditional type**: A type chosen by a test on another type, written with extends and ?.
- **unknown**: The type of a value you know nothing about, which must be narrowed before use.
- **any**: The type that switches type checking off for a value.
- **Type guard**: A function returning value is T, which narrows its argument where it returns true.
- **Type assertion**: Telling the compiler to treat a value as a type, with as, without any run-time check.
- **Non-null assertion**: A ! after an expression, asserting it isn't null or undefined, unchecked.
- **as const**: An assertion that makes the compiler infer the narrowest, read-only type for a literal.
- **satisfies**: An operator that checks a value against a type while keeping the value's own inferred type.
- **tsconfig.json**: The file that configures a TypeScript project's compiler settings.
- **Declaration file**: A .d.ts file describing a JavaScript module's types, with no code.

### Where this leads

Organizing a typed interface into components and state is in Components and state (9.4). TypeScript in the two main frameworks is in React today (9.5) and Angular today (9.6). Typed calls to the backend, and handling what really comes back, are in Talking to the backend (9.7). Types generated from an API's OpenAPI document come from the contract described in API design (8.2).

### Check yourself

1. Add a `Cancelled` case with a `reason` to `FlightStatus` in `types.ts`, run `tsc`, and follow the error to the switch that needs updating.
2. Change `flightId` into a function that throws unless the string matches two letters and one to four digits, so branded ids are also validated ones.
3. Write a type `Nullable<T>` that makes every property of `T` also accept `null`, as a mapped type, and use it on `Booking`.
4. Turn on `noUncheckedIndexedAccess` in a real TypeScript project and fix the errors it reports in one file.
5. Generate TypeScript types from an OpenAPI document with openapi-typescript and compare them with hand-written types in a frontend you know.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Two ids that were both strings

A function takes `(bookingId: string, flightId: string)` and a caller swaps them.

*Branded types*

```typescript
type BookingId = string & { readonly __brand: "BookingId" };
type FlightId  = string & { readonly __brand: "FlightId" };

const asBookingId = (s: string) => s as BookingId;   // only at the boundary, after validation

function cancel(booking: BookingId, flight: FlightId) { /* ... */ }
cancel(flightId, bookingId);   // compile error
```

> **Lesson:** Structural typing treats all strings alike. Brands make look-alike ids distinct to the compiler.

#### Example 2: A switch that missed a new status

The backend adds a `Diverted` flight status; the UI shows a blank badge.

*Exhaustive check with never*

```typescript
type Status = "scheduled" | "delayed" | "cancelled" | "diverted";

function badge(s: Status): string {
  switch (s) {
    case "scheduled": return "On time";
    case "delayed":   return "Delayed";
    case "cancelled": return "Cancelled";
    case "diverted":  return "Diverted";
    default: { const unreachable: never = s; throw new Error(`Unhandled ${unreachable}`); }
  }
}
// generate Status from the backend's OpenAPI so new values break the build
```

> **Lesson:** Unions plus a never check turn a forgotten case into a compile error.
