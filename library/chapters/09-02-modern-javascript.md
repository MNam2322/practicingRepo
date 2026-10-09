---
id: "9-2"
number: "9.2"
title: "Modern JavaScript"
part: 9
part_title: "Building a frontend"
words: 4175
minutes: 18
---

# 9.2 Modern JavaScript

> **Quick look.** Modern JavaScript for a C# developer, focusing on where it differs and bites.
>
> - Primitives are copied, objects are shared.
> - Use ===, know the six falsy values, and use ?? rather than || for defaults.
> - Spread and destructuring copy shallowly.
> - map and filter build new arrays; prefer toSorted to in-place sort, and pass a comparer for numbers.
> - this depends on how a function is called: use arrow functions for callbacks.
> - Promises and async/await mirror tasks; never leave a promise floating. Use modules, a bundler, a linter and a formatter.


### The big picture

JavaScript was written in ten days in 1995 to make web pages a little interactive. Today it runs every web frontend, many backends through Node.js, and much of the tooling around them. The language has changed enormously. The JavaScript of jQuery-era code, with `var`, callbacks and `==`, is very different from modern JavaScript, with modules, classes, `async` and `await`, destructuring and immutable array methods.

This chapter is a tour of modern JavaScript for someone who knows C#. It focuses on where JavaScript differs and bites. That means the sharing of objects, the two kinds of equality, and which values count as false. It also means why `this` changes depending on how a function is called, how copying really works, and how promises relate to the tasks from Async and await (5.3). A table at the end maps familiar C# features to their JavaScript equivalents. The example script was run with Node, and its comments show the real output.

### Before you start

**Builds on:** 5.3 Async and await, 2.4 Values, variables and references, 1.2 Numbers inside the machine, 1.3 Text is numbers too, 2.6 Scope and lifetime, 2.3 Types.

**Ideas explained later:**

- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Vite** (9.5): A fast build tool and development server, the standard starting point for single-page React apps.
- **JSX** (9.5): A syntax for writing markup inside JavaScript or TypeScript, compiled into function calls.
- **React** (9.5): A JavaScript library for building interfaces from components, created at Facebook.
- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between == and === in JavaScript?**

> `===` compares without converting types: `0 === "0"` is false. `==` converts values to a common type first, with rules few people remember: `0 == "0"` and `[] == false` are both true. Use `===`.

**2. Which values are falsy in JavaScript?**

> `false`, `0`, `""`, `null`, `undefined` and `NaN` (and `-0` and `0n`). Everything else is truthy, including `"0"`, `[]` and `{}`.

**3. delay is 0. What do delay || 15 and delay ?? 15 give?**

> `15` and `0`. `||` replaces every falsy value, including a real 0. `??` replaces only `null` and `undefined`, which is usually what a default should do.

**4. Why can calling a method through a variable lose its this?**

> In JavaScript, `this` is decided by how a function is called, not where it was defined. `board.show()` sets `this` to `board`, but `const f = board.show; f()` calls it with no object, so `this` is undefined. Arrow functions don't have their own `this` at all; they use the surrounding one.

**5. Does { ...booking } copy the booking's nested objects?**

> No. Spread makes a shallow copy: nested objects and arrays are shared with the original. `structuredClone(booking)` makes a deep copy.

**6. What does [10, 9, 1].sort() return?**

> `[1, 10, 9]`. By default, `sort` converts items to strings and compares those. Pass a comparer for numbers: `(a, b) => a - b`. It also sorts the array in place; `toSorted` returns a sorted copy.

**7. What is the JavaScript equivalent of Task.WhenAll?**

> `Promise.all`, which rejects as soon as any promise rejects. `Promise.allSettled` waits for all of them and reports each result, success or failure.

### A language that grew up

**JavaScript** was created by Brendan Eich at Netscape in 1995. Despite the name, it has little to do with Java. It is standardized as **ECMAScript**, and since 2015, when a very large update known as ES2015 or ES6 arrived, a new edition has appeared every year. Features go through a public process at the standards committee, and browsers and Node.js implement them as they reach the final stage.

The result is a language with two layers: an old core with some famous quirks, kept for backward compatibility with every web page ever written, and a modern layer that avoids most of them. This chapter shows both, so you can read old code and write new code.

### Values: primitives and objects

JavaScript has a small set of **primitive values**: numbers, big integers, strings, booleans, `undefined`, `null` and symbols. Primitives are copied on assignment, like C# value types. Everything else is an object: plain objects, arrays, functions, dates, maps. Variables hold references to objects, and assignment shares them, exactly as with C# classes in Values, variables and references (2.4).

JavaScript has two "nothing" values. `undefined` means "never set": a missing property, a parameter nobody passed. `null` means "deliberately empty". The `typeof` operator reports a value's type as a string, with one famous mistake kept for compatibility: `typeof null` is `"object"`.

Every number is a double, as Numbers inside the machine (1.2) described, so `0.1 + 0.2` isn't `0.3` and integers above 2⁵³ lose precision. `BigInt` values, written with an `n` suffix like `9007199254740993n`, hold integers of any size, but can't be mixed with ordinary numbers in arithmetic. Strings are UTF-16, as in C#, with the same length surprises from Text is numbers too (1.3).

### Variables

`const` declares a variable that can't be reassigned, and `let` one that can. Both are block-scoped, like C# locals. Use `const` by default. `const` stops reassignment of the variable, not changes to the object it refers to: `const flight = {...}; flight.gate = "B12"` is allowed.

Old code uses `var`, which is scoped to the whole function rather than the block, and whose declaration is moved to the top of the function, called hoisting, as Scope and lifetime (2.6) mentioned. `var` in a loop is also behind the closure trap from Values, variables and references (2.4). Don't write new `var`.

### Equality and conversion

JavaScript converts types automatically in many operations, the coercion from Types (2.3). The `==` operator converts both sides to a common type before comparing, following rules that produce `0 == "0"` and `[] == false`. **Strict equality**, `===`, compares without converting. Always use `===` and `!==`. Linters enforce this.

`NaN` isn't equal to anything, not even itself, as in C#: test for it with `Number.isNaN`.

### Truthy and falsy

Where a boolean is expected, as in `if` or `&&`, any value is converted. Values that convert to false are **falsy**: `false`, `0`, `""`, `null`, `undefined` and `NaN`. Everything else is **truthy**, including `"0"`, `"false"`, an empty array and an empty object.

This leads to a common bug with defaults. `delay || 15` means "delay, unless it is falsy", so a real delay of 0 becomes 15. The nullish coalescing operator, `delay ?? 15`, only replaces `null` and `undefined`, like C#'s `??`. Optional chaining, `flight?.gate?.code`, works like C#'s `?.`, as Reading the code in this guide (0.1) described.

### Objects, destructuring and spread

Object literals are the everyday way to build data: `{ number: "BA117", gate: "A10" }`. Property names can be computed, and `{ number }` is shorthand for `{ number: number }`.

**Destructuring** unpacks values from objects and arrays into variables: `const { number, gate } = flight` and `const [first, second] = codes`. It can give defaults, `gate = "TBC"`, and rename, `{ gate: currentGate }`. It is C#'s deconstruction from Control flow and functions (2.5), generalized.

**Spread syntax**, three dots before a value, expands its contents in place. `{ ...flight, gate: "C3" }` makes a new object with every property of `flight` and one replaced, the equivalent of a C# `with` expression. `[...codes, "DXB"]` makes a new array with one more item. A **rest parameter**, the same dots in a function's parameter list or a destructuring pattern, gathers the remaining items: `function log(first, ...others)`.

Spread copies are shallow, like C#'s `with` expressions from Values, variables and references (2.4): nested objects are shared with the original. `structuredClone(value)` makes a full, deep copy of plain data.

### Arrays

Arrays are dynamic lists, like `List<T>`. Their functional methods correspond to LINQ: `filter` is `Where`, `map` is `Select`, `reduce` is `Aggregate`, and `some`, `every`, `find` and `includes` match `Any`, `All`, `First` and `Contains`. Unlike LINQ, they run immediately and build new arrays at each step.

Some older array methods change the array in place: `sort`, `reverse`, `splice`, `push` and `pop`. Newer versions return a changed copy instead: `toSorted`, `toReversed`, `toSpliced` and `with`, added in ES2023. Frameworks that detect changes by comparing references, as Components and state (9.4) explains, need the copying versions. And `sort` without a comparer compares items as strings, as Searching and sorting (3.7) warned.

### Functions and this

Functions are values, as Procedural, object-oriented and functional styles (4.1) described. They can be declared with `function`, or written as an **arrow function**, `(a, b) => a + b`, the equivalent of a C# lambda. Functions capture the variables around them as closures, as in C#.

The keyword `this` is where JavaScript differs most from C#. Inside an ordinary function, `this` is not fixed when the function is written. It is set by how the function is called: `board.show()` calls `show` with `this` set to `board`. Take the same function out, `const show = board.show`, and call it on its own, and `this` is `undefined`. This **this binding** rule surprises everyone, especially when a method is passed as a callback, as with event handlers.

Arrow functions don't have their own `this`; they use the `this` of the surrounding code, like a captured variable. That makes them the natural choice for callbacks inside methods. `fn.bind(obj)` creates a copy of a function with `this` fixed.

### Classes and prototypes

JavaScript objects inherit from other objects directly. Each object has a link to another object, its **prototype**, and a property not found on the object is looked up on its prototype, and on the prototype's prototype, and so on. That chain is how methods are shared.

The `class` syntax, added in ES2015, is a friendlier way to set up that chain, and reads much like C#: a `constructor`, methods, getters and setters, `static` members and `extends` for inheritance. A **private field**, written with a `#` prefix, such as `#held`, is truly inaccessible from outside the class, enforced by the language. TypeScript's `private` keyword, covered in TypeScript (9.3), only exists at compile time.

### Modules, collections and generators

Modern code is split into ES modules with `import` and `export`, as A Node and TypeScript backend (8.4) described; browsers load them with `<script type="module">`.

`Map` and `Set` are proper hash-based collections, as in Hash tables (3.3), and unlike plain objects, a `Map` accepts any value as a key and keeps insertion order. `WeakMap` holds keys without keeping them alive, like the weak references in Memory management and garbage collection (2.7).

A **generator function**, written `function*`, produces values lazily with `yield`, like a C# iterator from Control flow and functions (2.5).

### Promises and async

A promise, from The event loop (5.4), is JavaScript's equivalent of a `Task`: a value that will arrive later, or a failure. `async` functions return promises, and `await` pauses a function until a promise settles, without blocking the thread, just as in Async and await (5.3). Because JavaScript runs on one thread, continuations always resume on that thread.

`Promise.all` waits for several promises and fails as soon as one fails, like `Task.WhenAll`. `Promise.allSettled` waits for all of them and reports each outcome. `Promise.race` settles with the first to settle, and `Promise.any` with the first success. A rejected promise that nothing handles crashes Node and logs an error in browsers, so await every promise.

### Errors and dates

`throw` can throw any value, not just `Error` objects, so a `catch` can't assume what it caught. Throw `Error` or a subclass, and check with `instanceof`.

The built-in `Date` type is a notorious source of bugs. Months count from 0, so `new Date(2026, 8, 28)` is 28 September. It mixes local time and UTC in confusing ways, and parsing strings without a time zone behaves inconsistently. Exchange times as ISO 8601 strings in UTC, as API design (8.2) recommended. A new built-in API called Temporal, with separate types for instants, dates, times and time zones, is arriving in browsers and Node.js to replace `Date`; until it is everywhere, a library such as date-fns helps.

### Strict mode

Early JavaScript silently allowed many mistakes, such as assigning to an undeclared variable, which created a global one. **Strict mode**, turned on with `"use strict"` at the top of a file, turns those into errors and makes `this` undefined in plain function calls rather than the global object. ES modules and classes are always strict, so modern code gets it automatically.

### Seeing it for real

This script touches every topic in this chapter. It runs with Node, or in pieces in a browser console.

```javascript
// modern.mjs: run with: node modern.mjs   (or paste pieces into a browser console)

// 1. Values: primitives are copied, objects are shared
let a = 5, b = a; b++;
const flight = { number: "BA117", gate: "A10" }, alias = flight; alias.gate = "B12";
console.log(a, flight.gate);                                   // 5 B12

// 2. Equality: === compares without converting; == converts first
console.log(0 == "0", 0 === "0", null == undefined, null === undefined);   // true false true false
console.log(NaN === NaN, Number.isNaN(NaN), [] == false);                  // false true true

// 3. Truthy and falsy: only these six values count as false
console.log([false, 0, "", null, undefined, NaN].map(Boolean).every(v => v === false), Boolean("0"), Boolean([]));   // true true true
const delay = 0;
console.log(delay || 15, delay ?? 15);                         // 15 0: || replaces every falsy value, ?? only null and undefined

// 4. Numbers are all doubles, as in 1.2
console.log(0.1 + 0.2, 2 ** 53 + 1, 2n ** 53n + 1n);           // 0.30000000000000004 9007199254740992 9007199254740993n

// 5. Destructuring, spread and rest
const { number, gate = "TBC", ...rest } = { number: "QF1", terminal: 3, status: "boarding" };
console.log(number, gate, rest);                               // QF1 TBC { terminal: 3, status: 'boarding' }
const moved = { ...flight, gate: "C3" };                       // a new object, with one property replaced
console.log(flight.gate, moved.gate);                          // B12 C3
const [first, ...others] = ["LHR", "JFK", "SIN"];
console.log(first, others);                                    // LHR [ 'JFK', 'SIN' ]

// 6. Arrays: map, filter and reduce return new values instead of changing the original
const delays = [120, 45, 300, 80, 15];
console.log(delays.filter(d => d > 60).map(d => d / 60), delays.reduce((sum, d) => sum + d, 0));   // [ 2, 5, 1.3333333333333333 ] 560
console.log(delays.toSorted((x, y) => x - y), delays);         // [ 15, 45, 80, 120, 300 ] [ 120, 45, 300, 80, 15 ]
console.log([10, 9, 1].sort());                                // [ 1, 10, 9 ]: the default sort compares as strings

// 7. this depends on how a function is called; arrow functions keep the surrounding this
const board = {
  name: "Departures",
  regular() { return this?.name; },
  arrow: () => typeof this,                                    // the module's this, which is undefined
};
const detached = board.regular;
console.log(board.regular(), detached(), board.arrow());       // Departures undefined undefined

// 8. Closures capture variables; let gives each loop pass its own
const fns = [];
for (let i = 0; i < 3; i++) fns.push(() => i);
console.log(fns.map(f => f()));                                // [ 0, 1, 2 ]

// 9. Classes are syntax over prototypes
class Seat {
  #held = false;                                               // a truly private field
  constructor(code) { this.code = code; }
  hold() { this.#held = true; return this; }
  get held() { return this.#held; }
}
const seat = new Seat("12A").hold();
console.log(seat.held, Object.getPrototypeOf(seat) === Seat.prototype);   // true true

// 10. Map and Set, and copying
const gates = new Map([["BA117", "B12"]]);
gates.set("QF1", "C3");
console.log(gates.get("QF1"), new Set(["LHR", "JFK", "LHR"]).size);       // C3 2
const nested = { flight: "BA117", legs: [{ from: "LHR" }] };
const shallow = { ...nested }, deep = structuredClone(nested);
nested.legs[0].from = "LGW";
console.log(shallow.legs[0].from, deep.legs[0].from);          // LGW LHR: spread copies one level only

// 11. Generators produce values lazily
function* flightNumbers() { let n = 100; while (true) yield `BA${n++}`; }
const gen = flightNumbers();
console.log(gen.next().value, gen.next().value);               // BA100 BA101

// 12. Promises and async/await, as in 5.3 and 5.4
const wait = (ms, value) => new Promise(resolve => setTimeout(() => resolve(value), ms));
const results = await Promise.allSettled([wait(20, "fare"), Promise.reject(new Error("seat map down")), wait(10, "gate")]);
console.log(results.map(r => r.status === "fulfilled" ? r.value : `failed: ${r.reason.message}`));
// [ 'fare', 'failed: seat map down', 'gate' ]
try { JSON.parse("{bad json}"); } catch (e) { console.log(e instanceof SyntaxError, e.name); }   // true SyntaxError
```

### From C# to JavaScript

| C# | JavaScript |
|---|---|
| `var x = 5;` | `const x = 5;` or `let x = 5;` |
| `$"{a} and {b}"` | `` `${a} and ${b}` `` (a template literal) |
| `a?.b`, `a ?? b` | the same: `a?.b`, `a ?? b` |
| `x => x * 2` | `x => x * 2` |
| `record with { Gate = "B12" }` | `{ ...record, gate: "B12" }` |
| `Where`, `Select`, `Aggregate` | `filter`, `map`, `reduce` |
| `Dictionary<K,V>`, `HashSet<T>` | `Map`, `Set` |
| `Task`, `await`, `Task.WhenAll` | `Promise`, `await`, `Promise.all` |
| `try` / `catch (IOException e)` | `try` / `catch (e) { if (e instanceof ...) }` |
| `yield return` | `yield` in a `function*` |
| `private` field | `#field` |
| `using` namespaces | `import` from modules |

A **template literal**, written with backticks, is JavaScript's string interpolation: `` `${number} is late` ``.

### Tooling

Browsers can run modern JavaScript directly, but production code usually passes through tools first.

A **bundler**, such as Vite or the older webpack, combines many module files and their npm packages into a few files the browser can load efficiently. It also removes unused code, minifies what is left, and handles TypeScript and JSX. Vite itself uses esbuild and Rollup internally. A **linter**, ESLint in almost every project, catches bugs and bad patterns, such as `==`, unused variables and floating promises. A formatter, usually Prettier, keeps layout consistent so reviews focus on substance.

To support older browsers, code can be transpiled, from Compilers, interpreters and JIT (2.2), into older syntax, and missing built-in functions can be added with a **polyfill**: code that implements a newer feature on top of older ones. Current browsers update themselves automatically, so most projects now target only recent versions.

### In short

Modern JavaScript is an old core with quirks plus a large modern layer. Primitives are copied and objects are shared. Use `===`, know the six falsy values, and use `??` rather than `||` for defaults. Destructuring and spread unpack and copy data, but copies are shallow. Array methods like `map` and `filter` build new arrays; prefer `toSorted` over the in-place `sort`, and always pass a comparer for numbers. `this` depends on how a function is called, so use arrow functions for callbacks. Classes sit on prototypes, with `#` for private fields. Promises and `async` mirror tasks. Use modules, a bundler, a linter and a formatter.

### What goes wrong

**Loose equality.** `==` gives surprising results across types. Use `===` everywhere and let the linter enforce it.

**`||` for defaults.** A real 0, empty string or `false` is replaced by the default. Use `??`.

**Lost `this`.** A method passed as a callback runs with `this` undefined and throws. Use an arrow function or `bind`.

**Accidental mutation.** `sort` or `reverse` changes an array that other code, or a framework, still depends on. Use `toSorted` and `toReversed`, or copy first.

**Shallow copies.** Changing a nested object in a "copy" changes the original too. Copy nested levels explicitly, or use `structuredClone`.

**Numeric sort without a comparer.** `[10, 9, 1].sort()` sorts as text. Always pass `(a, b) => a - b` for numbers.

**Floating promises.** An async call without `await` fails silently, or crashes the process later. Await every promise, and lint for floating ones.

**`Date` confusion.** Zero-based months and mixed local and UTC times produce off-by-one dates. Exchange ISO strings in UTC and use a date library or Temporal.

### Cost model

Modern JavaScript engines JIT-compile hot code, as Compilers, interpreters and JIT (2.2) described, and are fast for ordinary application code. Array methods like `map` and `filter` allocate a new array at each step, which is fine for hundreds of items and noticeable for millions. Spread and `structuredClone` cost time proportional to what they copy. In browsers, the bigger cost is usually the download: every kilobyte of script must be downloaded, parsed and compiled before it runs, which is why bundlers remove unused code and why large dependencies deserve a second look.

### In other languages (optional)

**TypeScript.** Adds static types on top of exactly this language, and removes them before running, as TypeScript (9.3) explains. Every quirk in this chapter still applies at run time.

**C#.** The table above shows the close parallels. The main differences are C#'s static types and value types, and JavaScript's `this` rules, coercion and single thread.

**Python.** Also dynamic, but strongly typed: `"1" + 1` is an error rather than `"11"`. Its truthiness rules are similar, with empty lists and dictionaries counting as false.

### Key terms

- **JavaScript**: The programming language of the web, also used on servers through Node.js.
- **ECMAScript**: The official standard that defines JavaScript, with a new edition every year.
- **Primitive value**: A simple value that is copied on assignment: a number, bigint, string, boolean, undefined, null or symbol.
- **Strict equality**: Comparing with === and !==, which never converts types first.
- **Falsy**: A value that counts as false: false, 0, "", null, undefined or NaN.
- **Truthy**: Any value that isn't falsy, including "0", [] and {}.
- **Destructuring**: Unpacking values from objects or arrays into variables, as in const { number } = flight.
- **Spread syntax**: Three dots that expand an object's or array's contents in place, used for copying and combining.
- **Rest parameter**: Three dots in a parameter list or pattern that gather the remaining items into an array or object.
- **Template literal**: A string in backticks that can include ${expressions} and span lines.
- **Arrow function**: A compact function, (a, b) => a + b, that uses the surrounding code's this.
- **This binding**: How JavaScript sets this, based on how a function is called rather than where it is defined.
- **Prototype**: The object another object inherits properties from, forming a lookup chain.
- **Private field**: A class field written with #, which the language makes inaccessible from outside the class.
- **Generator function**: A function written with function* that produces values lazily with yield.
- **Strict mode**: A stricter set of JavaScript rules, on automatically in modules and classes.
- **Bundler**: A tool that combines modules and packages into a few optimized files for the browser.
- **Linter**: A tool that checks code for bugs and bad patterns without running it.
- **Polyfill**: Code that implements a newer built-in feature for environments that lack it.

### Where this leads

Types on top of this language are in TypeScript (9.3). Organizing interfaces built with it is in Components and state (9.4), and the two major frameworks in React today (9.5) and Angular today (9.6). Fetching data with promises and `fetch` is in Talking to the backend (9.7). JavaScript on the server was in A Node and TypeScript backend (8.4).

### Check yourself

1. Predict the output of `console.log(null == 0, null >= 0, undefined == 0)` in Node before running it, then explain each result.
2. Write a function `withGate(flight, gate)` that returns a new flight with a new gate and a copied, not shared, `legs` array. Test that changing the result doesn't change the original.
3. In `modern.mjs`, make `board.regular` work when detached by using `bind`, then by rewriting the call site with an arrow function.
4. Sort an array of flight objects by delay, then by number, without changing the original array.
5. Convert a callback-style function, such as `setTimeout` or `fs.readFile`, into a promise-returning one, and use it with `await`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Zero treated as "no value"

A fare form shows the default baggage allowance when the user picks 0 kg, because `allowance || 23` treats 0 as missing.

*Use ?? for defaults*

```typescript
const kg = input.allowance ?? 23;     // only null/undefined fall back; 0 stays 0
const label = input.note ?? "";      // "" stays "" too
```

> **Lesson:** || replaces every falsy value (0, "", false). ?? replaces only null and undefined.

#### Example 2: Prices sorted as strings

A fare list shows 1000 before 200 after sorting.

*Always pass a comparer*

```typescript
const fares = [200, 1000, 35];
fares.sort();                               // [1000, 200, 35]  (compared as text!)
const sorted = fares.toSorted((a, b) => a - b);   // [35, 200, 1000], original untouched
```

> **Lesson:** Array.sort compares strings by default and mutates in place. Use toSorted with a numeric comparer.
