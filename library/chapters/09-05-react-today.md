---
id: "9-5"
number: "9.5"
title: "React today"
part: 9
part_title: "Building a frontend"
words: 4190
minutes: 18
---

# 9.5 React today

> **Quick look.** React as it is written today: components, hooks and React 19.
>
> - Components are functions from props and state to JSX.
> - Setting state schedules a re-render with a new snapshot; replace, don't mutate.
> - Keys give list items stable identity; index keys break changing lists.
> - Hooks must run in the same order every render; effects are only for the world outside React.
> - Derive values during render and handle actions in event handlers; use a data library instead of fetching in effects.
> - React 19 adds actions, useActionState and useOptimistic; the React Compiler memoizes automatically. Next.js adds server components; plain SPAs start from Vite.


### The big picture

React, released by Facebook in 2013, is the most widely used way to build web interfaces. Its core idea is the one from Components and state (9.4): components are functions that take props and state and return a description of the UI, and React makes the DOM match. It is a library rather than a complete framework, so real apps combine it with a router, a way to fetch data and a build tool, and the choices have shifted a lot over the years.

This chapter covers React as it is written now. It explains JSX, state and re-rendering, and how React compares one render with the next, which is why list items need keys. It covers hooks and the rules they follow, effects and when not to use them, and custom hooks for reusing logic. Then it covers what React 19 added: actions that handle pending states and errors for forms, and the compiler that removes most manual performance tuning. It also covers server components, and the frameworks that use them, such as Next.js. The example component and its tests were run for this chapter, with React 19.3, Vitest and Testing Library.

### Before you start

**Builds on:** 9.4 Components and state, 9.1 How a browser shows a page, 8.4 A Node and TypeScript backend, 4.6 Testing.

**Ideas explained later:**

- **Performance** (12.4): How quickly a system does its work at a given load.
- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What happens when a component calls its state setter?**

> React schedules a re-render: it calls the component function again with the new state, compares the returned JSX with the previous result, and applies only the differences to the DOM. The variable holding the old state doesn't change during the current render.

**2. Why do list items need a key, and why is the array index a poor choice?**

> Keys tell React which item is which between renders, so it can keep each row's DOM and state when items are added, removed or reordered. An index changes when the list changes, so state and DOM get attached to the wrong items.

**3. Why must hooks be called in the same order on every render?**

> React identifies each hook by its position in the sequence of calls, not by name. Calling a hook inside a condition or loop shifts the positions, and state gets attached to the wrong hook.

**4. When should you not use useEffect?**

> To compute values from props or state: compute them during rendering. To respond to a user action: do it in the event handler. Effects are for keeping a component in step with something outside React, such as a timer, a subscription or a browser API.

**5. What does useActionState do?**

> It wraps an async action, typically a form submission, and gives you its latest result, a function to trigger it, and whether it is still pending. React handles the transitions, so the form can show progress and results without manual loading state.

**6. What is a React Server Component?**

> A component that runs only on the server, inside a framework such as Next.js. It can read data directly, from a database or API, and sends its rendered output to the browser without sending its code. Components marked `"use client"` run in the browser as usual.

**7. What does the React Compiler do?**

> It analyses components at build time and adds memoization automatically, so parts of the UI that didn't change aren't recomputed. Most hand-written `useMemo`, `useCallback` and `memo` becomes unnecessary.

### Components and JSX

A React component is a function that returns what to show. Its parameter is an object of props. The return value is written in **JSX**, a syntax extension that lets you write markup inside JavaScript or TypeScript:

```tsx
function FlightRow({ flight }: { flight: Flight }) {
  return <li className={flight.delay > 0 ? "late" : undefined}>{flight.number} to {flight.to}</li>;
}
```

JSX isn't HTML. It compiles into ordinary function calls that create objects describing elements, so it is checked by TypeScript like any other code, and files using it end in `.tsx`. Curly braces embed JavaScript expressions. A few attribute names differ from HTML because they are JavaScript property names: `className` for `class`, `htmlFor` for `for`. There are no template-specific loops or conditions; you use the language itself. `condition && <p>...</p>` shows something conditionally, a ternary chooses between two things, and `array.map` renders a list.

Components compose like elements: `<FlightRow flight={f} />`. Content between a component's tags arrives as the `children` prop, which lets components wrap other content, such as a dialog around its body.

### State and re-rendering

`useState` gives a component a piece of state: `const [search, setSearch] = useState("")`. It returns the current value and a setter. Calling the setter doesn't change the variable; it tells React the state has changed. React then re-renders: it calls the component function again, and this time `useState` returns the new value.

Each render sees a fixed snapshot of state. Code in an event handler that calls `setCount(count + 1)` three times adds one, not three, because `count` is the same snapshot each time. When a new value depends on the old one, pass a function instead: `setHeld(current => [...current, number])`, which always receives the latest value. State updates must replace objects and arrays rather than mutate them, as Components and state (9.4) explained, because React compares references to decide whether anything changed.

When a component re-renders, its children re-render too, by default. That is usually cheap, and the React Compiler, below, skips children whose inputs didn't change.

### How React updates the DOM

A render produces a tree of plain objects describing elements, often called the **virtual DOM**. React compares the new tree with the previous one, a process called **reconciliation**, and applies only the differences to the real DOM, in one batch, in a step called the commit.

Reconciliation compares trees position by position. For lists, position isn't enough: if a flight is inserted at the top, every row would appear to have changed. A **list key**, the `key` prop, gives each item a stable identity: `<FlightRow key={f.number} ... />`. With keys, React can tell that one row was added and the others merely moved, and it keeps each row's DOM nodes and state attached to the right item. Keys must be unique among siblings and stable across renders. The array index is a poor key for any list that can be reordered, filtered or inserted into, because it changes when the list does, and state such as a half-typed input ends up on the wrong row.

### Hooks

Functions whose names start with `use`, such as `useState`, are **hooks**: the way function components get state and other React features. React stores each component's hook data in a list and matches each call to its entry by its position in the sequence of calls. That is the reason for the **rules of hooks**: call hooks only at the top level of a component or of another hook, never inside conditions, loops or callbacks, so the sequence is identical on every render. The React linter plugin enforces them.

A handful of hooks cover most needs. `useState` holds state. `useEffect` runs code after rendering, to synchronize with something outside React. `useRef` holds a mutable value that survives renders without causing them, and gives access to DOM elements. `useContext` reads a value provided higher in the tree, the remedy for prop drilling from Components and state (9.4). A **React context** is created with `createContext` and supplied with a provider component. It suits values many components need and that change rarely, such as the current user, theme or language.

### Effects, and when not to use them

`useEffect(fn, dependencies)` runs `fn` after React has updated the DOM, and again whenever a value in its **dependency array** has changed since the last render. If `fn` returns a function, React calls it before running the effect again and when the component is removed: that is the cleanup, as Components and state (9.4) required. In development, React's Strict Mode deliberately runs each effect, cleanup and effect again when a component first appears, to expose effects that don't clean up properly.

Effects are for the outside world: timers, subscriptions, browser APIs, and non-React widgets. The React documentation's guidance, often summarized as "you might not need an effect", lists the common misuses. Values computed from props or state should be computed during rendering, as `visible` is in the example. Responses to a user's action belong in the event handler, not in an effect that watches for state the handler set. Fetching data in effects by hand invites race conditions and missing error handling; use a data library or a framework, as Talking to the backend (9.7) explains.

### Custom hooks

A **custom hook** is a function whose name starts with `use` and that calls other hooks. It lets several components share stateful logic, without sharing state: each component that calls it gets its own. The example's `useDebouncedValue` returns a value that only updates once its input has stopped changing for a moment, using state, an effect and a cleanup. Custom hooks are React's main tool for reuse, the equivalent of extracting a helper method.

### Actions in React 19

React 19, released at the end of 2024, added built-in support for the most common kind of async work in a UI: submitting something and waiting for the answer. An **action** is an async function that React runs inside a transition, tracking whether it is pending and applying its result when it finishes.

A form's `action` prop can take a function directly. `useActionState` wraps an action and returns its latest result, a function to trigger it, and a pending flag, which replaces the hand-written loading, error and result state that forms used to need. `useFormStatus` lets a nested button know whether its form is submitting. `useOptimistic` shows the expected result immediately, such as a seat marked as held, and rolls back automatically if the action fails. React 19 also added `use`, which reads a promise or context during rendering, and made `ref` an ordinary prop.

### The example

This component file has a flight board with a controlled search box, a derived list, keyed rows, immutable state updates, an event raised to the parent and a custom hook with a cleaned-up effect. It also has a booking form that uses a React 19 action.

```tsx
// FlightBoard.tsx
import { useActionState, useEffect, useState } from "react";

export type Flight = { number: string; to: string; delay: number };

export function FlightBoard({ flights, onHold }: { flights: Flight[]; onHold?: (number: string) => void }) {
  const [search, setSearch] = useState("");                    // local state
  const [held, setHeld] = useState<string[]>([]);
  const query = useDebouncedValue(search, 200);                // a custom hook: filter once typing pauses
  const visible = flights.filter(f => f.to.toLowerCase().includes(query.trim().toLowerCase()));   // derived, not stored

  function hold(number: string) {
    setHeld(current => [...current, number]);                  // a new array: never push onto state
    onHold?.(number);                                           // an event up to the parent
  }

  return (
    <section>
      <label>
        Destination
        <input value={search} onChange={e => setSearch(e.target.value)} />
      </label>
      <ul>
        {visible.map(f => (
          <FlightRow key={f.number} flight={f} held={held.includes(f.number)} onHold={hold} />
        ))}
      </ul>
      {visible.length === 0 && <p>No flights match.</p>}
    </section>
  );
}

function FlightRow({ flight, held, onHold }: { flight: Flight; held: boolean; onHold: (number: string) => void }) {
  return (
    <li>
      {flight.number} to {flight.to}
      {flight.delay > 0 && ` (+${flight.delay} min)`}
      <button disabled={held} onClick={() => onHold(flight.number)}>{held ? "Held" : "Hold seat"}</button>
    </li>
  );
}

export function useDebouncedValue<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);                            // cleanup: cancel if the value changes first
  }, [value, ms]);
  return debounced;
}

// React 19: a form action, with a pending flag and a result message
export function BookingForm({ book }: { book: (passenger: string) => Promise<string> }) {
  const [message, submit, pending] = useActionState(async (_previous: string, form: FormData) => {
    const passenger = String(form.get("passenger") ?? "").trim();
    if (!passenger) return "Enter a passenger name.";
    try { return `Booked: ${await book(passenger)}`; }
    catch { return "Booking failed. Please try again."; }
  }, "");

  return (
    <form action={submit}>
      <input name="passenger" aria-label="Passenger" />
      <button disabled={pending}>{pending ? "Booking..." : "Book"}</button>
      {message && <p role="status">{message}</p>}
    </form>
  );
}
```

These tests use Vitest and Testing Library, the usual pair for React. They find elements the way a user would, by label, role and visible text, as Components and state (9.4) recommended. All three pass.

```tsx
// FlightBoard.test.tsx: run with: npx vitest run   (with the jsdom environment and the React plugin configured)
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, test, vi } from "vitest";
import { BookingForm, FlightBoard, type Flight } from "./FlightBoard";

afterEach(cleanup);

const flights: Flight[] = [
  { number: "BA117", to: "New York", delay: 0 },
  { number: "QF1", to: "Sydney", delay: 45 },
];

test("filters by destination once typing pauses", async () => {
  render(<FlightBoard flights={flights} />);
  await userEvent.type(screen.getByLabelText("Destination"), "syd");
  await vi.waitFor(() => expect(screen.queryByText(/BA117/)).toBeNull());   // gone after the 200 ms pause
  expect(screen.getByText(/QF1 to Sydney/)).toBeTruthy();
});

test("holding a seat tells the parent and disables the button", async () => {
  const onHold = vi.fn();
  render(<FlightBoard flights={flights} onHold={onHold} />);
  await userEvent.click(screen.getAllByRole("button", { name: "Hold seat" })[1]!);
  expect(onHold).toHaveBeenCalledWith("QF1");
  expect(screen.getByRole("button", { name: "Held" })).toHaveProperty("disabled", true);
});

test("the booking form shows progress, then the result", async () => {
  let finish!: (reference: string) => void;
  const book = vi.fn(() => new Promise<string>(resolve => { finish = resolve; }));
  render(<BookingForm book={book} />);
  await userEvent.type(screen.getByLabelText("Passenger"), "Ada");
  await userEvent.click(screen.getByRole("button", { name: "Book" }));
  expect(await screen.findByRole("button", { name: "Booking..." })).toHaveProperty("disabled", true);
  finish("B1");
  expect((await screen.findByRole("status")).textContent).toBe("Booked: B1");
  expect(book).toHaveBeenCalledWith("Ada");
});
```

The third test controls exactly when the fake booking finishes, so it can check the pending state first and the result afterwards, with no real timing involved.

### Performance and the React Compiler

Because a component's children re-render with it, large trees can do unnecessary work. The traditional tools were `memo`, which skips re-rendering a component whose props are unchanged, and the `useMemo` and `useCallback` hooks, which keep values and functions from being recreated on every render. They are easy to get wrong, and they clutter code.

The **React Compiler**, whose first stable version was released in October 2025, removes most of that work. It analyses components at build time and inserts memoization automatically, so only the parts affected by a change recompute. It relies on components following React's rules, being pure during render and never mutating props or state, which is one more reason to follow them.

The other big performance levers are structural. Keep state as low in the tree as possible, so changes re-render less. Render only the visible part of very long lists, called virtualization. And split code so each page loads only what it needs, with `lazy` and `Suspense`.

### Server components and frameworks

A plain React app is a single-page application from How a browser shows a page (9.1): the browser downloads the code, then fetches data and renders. React now also supports rendering on the server, in two forms.

Server-side rendering with hydration renders the first view to HTML on the server, then hydrates it in the browser. **React Server Components** go further. A server component runs only on the server. It can be `async`, read from a database or internal API directly, and sends only its rendered result to the browser, never its code. Components that need state, effects or event handlers are marked with `"use client"` at the top of their file, and run in the browser as usual. A server function, marked `"use server"`, can be called from the client like an ordinary async function, with the framework handling the request.

These features need a framework that integrates the server and the build. **Next.js** is the most widely used, and React Router, which absorbed the Remix framework, is another. For a single-page app with no server rendering, such as an internal tool behind a login, **Vite** with its React template is the standard starting point. Create React App, the old default, was officially deprecated in 2025.

### The rest of the stack

React leaves several choices to you, and a common set has emerged. For server state, TanStack Query or the framework's data loading, covered in Talking to the backend (9.7). For shared client state, React context, and for complex cases a small store such as Zustand, or Redux Toolkit in larger teams. For routing, React Router or the framework's own. For styling, CSS modules or Tailwind CSS, a set of utility classes applied directly in markup. For forms, React Hook Form with schema validation, often Zod, from A Node and TypeScript backend (8.4). For tests, Vitest and Testing Library as in the example, and Playwright for whole-app browser tests.

### In short

React components are functions from props and state to JSX, which compiles to plain function calls. Setting state schedules a re-render with a new snapshot; updates must replace, not mutate. React compares each render with the last and applies only the differences, and list keys give items stable identities. Hooks provide state, effects, refs and context, and must be called in the same order every render. Use effects only for the world outside React; derive values during rendering and handle actions in event handlers. React 19 adds actions, `useActionState` and `useOptimistic` for async work, and the React Compiler memoizes automatically. Frameworks such as Next.js add server components; plain SPAs start from Vite.

### What goes wrong

**Index keys on changing lists.** Rows keep the wrong state after an insert or sort. Use a stable id as the key.

**Mutating state.** `held.push(x); setHeld(held)` passes the same array, and nothing re-renders. Always pass a new array or object.

**Stale state in updates.** Several `setCount(count + 1)` calls in one handler add one. Use the function form, `setCount(c => c + 1)`.

**Effects for derived values.** An effect sets filtered state whenever the list changes, costing an extra render and a moment of wrong output. Compute it during render.

**Missing dependencies or cleanup.** An effect uses a value it doesn't list, and runs with a stale copy, or it leaves timers and subscriptions behind. Follow the linter's dependency rule and always clean up.

**Hooks in conditions.** A hook called inside an `if` breaks the order, and state jumps between hooks. Keep hooks at the top level.

**Fetching by hand in effects.** Responses arrive out of order and overwrite newer data, and errors go unhandled. Use a data library or the framework.

**Secrets in client components.** Code marked `"use client"`, and anything it imports, is sent to the browser. Keep keys and private logic in server-only code.

### Cost model

A re-render costs the time to run the affected component functions and compare their output: microseconds for a small component, milliseconds for a large tree, which is why state placement and the compiler matter. DOM changes are applied in one batch per update. React itself adds roughly 50 KB compressed to a bundle before your own code. Server rendering moves work to the server and shows content sooner, while hydration costs browser time roughly proportional to the size of the interactive page. Server components send no code for the parts that don't need interaction, which can cut bundle size a lot.

### In other languages (optional)

**Blazor.** C# components with parameters, `@code` blocks and `StateHasChanged`, rendered by comparing trees much as React does, in the browser with WebAssembly or on the server.

**Preact and Solid.** Preact is a tiny React-compatible library. Solid uses JSX but with fine-grained signals instead of re-rendering, so component functions run only once.

**Vue and Svelte.** Template-based alternatives with signal-style reactivity, as Components and state (9.4) described.

**React Native.** The same components and hooks, rendering to native iOS and Android views instead of the DOM.

### Key terms

- **React**: A JavaScript library for building interfaces from components, created at Facebook.
- **JSX**: A syntax for writing markup inside JavaScript or TypeScript, compiled into function calls.
- **Virtual DOM**: The tree of plain objects a render produces, describing what the DOM should look like.
- **Reconciliation**: Comparing a new render with the previous one to find the DOM changes needed.
- **List key**: A stable, unique key prop that identifies an item in a list across renders.
- **Hook**: A function starting with use that gives a component state or other React features.
- **Rules of hooks**: Hooks must be called at the top level, in the same order on every render.
- **Dependency array**: The list of values whose change makes an effect run again.
- **React context**: A way to provide a value to a whole subtree of components without passing props.
- **Custom hook**: A function starting with use that combines other hooks to share stateful logic.
- **Action**: In React 19, an async function whose pending state and result React tracks for you.
- **React Compiler**: A build-time tool that adds memoization to React components automatically.
- **React Server Components**: Components that run only on the server and send their rendered output, not their code.
- **Next.js**: The most widely used framework built on React, with server rendering and server components.
- **Vite**: A fast build tool and development server, the standard starting point for single-page React apps.

### Where this leads

The other major framework, with signals and a full toolkit, is in Angular today (9.6). Fetching and caching server data in React apps is in Talking to the backend (9.7). Browser security concerns such as cross-site scripting, which React's JSX escapes by default, are in Common web vulnerabilities (10.4). Testing whole apps in a real browser, with tools such as Playwright, fits the test layers from Testing (4.6) and runs in CI/CD pipelines (11.4).

### Check yourself

1. In `FlightBoard.tsx`, change the row key to the array index, add a text input to each row, type in the second row, then filter the list. Explain what happens to the typed text.
2. Add a "Release" button to held rows that removes the flight from `held` without mutating the array, and write a test for it.
3. Replace `useDebouncedValue`'s effect with a version that forgets the cleanup, type quickly in a test, and count how many times the filter value changes.
4. Use `useOptimistic` in `BookingForm` to show "Booking Ada..." immediately, and check that a failing `book` function replaces it with the error message.
5. Create a new React app with Vite's React and TypeScript template, and add the React Compiler to it following the current React documentation.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Fetching in useEffect with a race

Typing quickly in a flight search shows results for an older query that arrived last.

*Let a server-state library handle it*

```typescript
import { useQuery, keepPreviousData } from "@tanstack/react-query";

function useFlights(q: string) {
  return useQuery({
    queryKey: ["flights", q],                         // every input in the key
    queryFn: ({ signal }) => fetch(`/api/flights?q=${encodeURIComponent(q)}`, { signal }).then(r => r.json()),
    enabled: q.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });
}
```

> **Lesson:** Hand-written fetching in effects invites races and stale results. Use a library that keys, cancels and caches.

#### Example 2: Index keys that moved the wrong row's input

Removing a passenger from the middle of a list leaves the deleted passenger's typed name in the next row.

*Stable keys*

```typescript
{passengers.map(p => (
  <PassengerRow key={p.id} passenger={p} />     // not key={index}
))}
```

> **Lesson:** Keys give list items identity. Index keys break as soon as items are inserted, removed or reordered.
