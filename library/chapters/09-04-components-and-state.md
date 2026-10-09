---
id: "9-4"
number: "9.4"
title: "Components and state"
part: 9
part_title: "Building a frontend"
words: 4139
minutes: 18
---

# 9.4 Components and state

> **Quick look.** Declarative UI built from components, and where each kind of state should live.
>
> - Describe what the screen shows for the current state; the framework updates the DOM.
> - Components take props in and send events out, composed into a tree.
> - Place state deliberately: local, shared, server state (a cache), URL state for anything linkable, form state.
> - Store the minimum and derive the rest, with one source of truth.
> - Data flows down, events flow up; lift state to a common parent when siblings share it.
> - Replace objects and arrays instead of mutating; frameworks detect change by re-rendering or with signals. Always validate on the server too.


### The big picture

Early interactive pages were written as instructions: when this button is clicked, find that list, remove its third item, change this label's text. With a few interactions that works. With hundreds, the code that keeps the screen in step with the data becomes the hardest part of the application, and every bug is a screen showing something that is no longer true.

Modern frontends turn this around. You describe what the interface should look like for any given state, and the framework works out how to change the DOM to match. The interface is built from components, small pieces that each take inputs and describe their part of the screen. This chapter explains those ideas, which React, Angular, Vue and Svelte all share. It covers what state is and where each kind belongs, why you should store as little as possible and compute the rest, and why data should flow in one direction. It then covers why updates must replace objects rather than change them, and the main ways frameworks notice what changed. The example builds a tiny reactive system in fifty lines, the idea behind Angular's signals.

### Before you start

**Builds on:** 9.1 How a browser shows a page, 4.1 Procedural, object-oriented and functional styles, 7.2 The relational model and SQL, 4.2 Abstraction and interfaces, 4.3 Composition and dependency injection, 8.6 Caching.

**Ideas explained later:**

- **React** (9.5): A JavaScript library for building interfaces from components, created at Facebook.
- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **React context** (9.5): A way to provide a value to a whole subtree of components without passing props.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does "UI is a function of state" mean?**

> For any given state, you describe what the interface should show, and the framework updates the DOM to match. You change state, not the screen, and the screen follows.

**2. A list is filtered by a search box. Should the filtered list be stored in state?**

> No. Store the full list and the search text, and compute the filtered list from them. Storing it separately means keeping two copies in step, and they will drift.

**3. What is unidirectional data flow?**

> Data flows down from parent components to children as inputs, and changes flow up as events. A child never changes its parent's data directly; it asks, and the owner of the data changes it.

**4. Why must state updates create new objects and arrays instead of changing them?**

> Frameworks usually detect changes by comparing references. Pushing onto an existing array leaves the reference the same, so nothing notices, and the screen goes stale.

**5. Where should the current filter and page of a search results list live?**

> In the URL, as query parameters. Then the page can be bookmarked, shared, reloaded and navigated with the back button, and the URL becomes the single source of truth for them.

**6. What is server state, and why treat it differently from other state?**

> Data that belongs to the backend and is shown in the frontend, such as flights and bookings. The frontend only holds a cached copy, which can go stale, so it needs loading, refreshing and error handling, like any cache.

**7. What is the difference between React's re-rendering model and signals?**

> React re-runs component functions when their state changes and compares the results to find DOM changes. Signals track exactly which pieces of UI read which values, and update only those when a value changes.

### From instructions to descriptions

The page in How a browser shows a page (9.1) built its list with explicit DOM calls. That is the imperative style from Procedural, object-oriented and functional styles (4.1): a sequence of instructions changing things. To keep the list correct as flights are added, delayed and cancelled, every change needs its own instructions, and every instruction must know exactly what the screen currently shows.

**Declarative UI** instead describes the result. You write a function of the current state that returns what the screen should look like: for these flights and this filter, a list with these items. When the state changes, the function runs again, or the parts of it that depend on what changed, and the framework updates the DOM to match the new description. Your code never needs to know what the screen showed before. It is the same shift as from loops to SQL in The relational model and SQL (7.2): say what, not how.

### Components

A **component** is a reusable piece of interface: a flight row, a seat map, a date picker, a whole booking page. It bundles markup, styling and behaviour, and it hides its internals behind a small interface, as Abstraction and interfaces (4.2) described.

A component's inputs are its **props**, short for properties, such as the flight to show. Its outputs are events it raises, such as "seat selected". Components contain other components, forming a **component tree** that mirrors the DOM tree: a page contains a board, which contains rows. It is composition, from Composition and dependency injection (4.3), applied to the screen.

Good components are small and focused. A row that shows a flight shouldn't also fetch flights. A common split separates presentational components, which only display what they are given and raise events, from container components, which own state and data loading and pass it down. Presentational components are easy to reuse and to test.

### Kinds of state

State is any data that can change while the app runs and affects what is shown. It comes in kinds, and each has a natural home.

**Local state** belongs to one component and nobody else cares about it: whether a dropdown is open, the text in a box being typed. Keep it in that component.

Shared client state is needed by many components: the signed-in user, a basket, a theme setting. Keep it in one place near the top of the tree, in a shared service or a store.

**Server state** is data that belongs to the backend: flights, bookings, seat maps. The frontend only holds a copy, which can go stale, so it is really a cache, with all of the concerns from Caching (8.6): loading, refreshing, invalidating after changes, and handling errors. Talking to the backend (9.7) covers it.

**URL state** lives in the address bar: the current page, the selected flight, search filters, sort order. Putting it there makes the view bookmarkable, shareable and reloadable, and makes the back button work. If users would expect a link to reproduce what they see, it belongs in the URL.

Form state is the in-progress values, validation messages and touched fields of a form, usually owned by the form until it is submitted.

### Store less, compute more

The most useful rule for state is to keep as little as possible. Anything that can be computed from other state is **derived state**, and should be computed, not stored. The filtered list of flights is computed from the full list and the filter. A basket's total is computed from its items. A "can submit" flag is computed from the form's validity.

Storing derived values means keeping two copies of the truth in step, and every place that changes one must remember the other. Sooner or later one is missed, and the screen shows a total that doesn't match its items. This is the update anomaly from Schema design and migrations (7.5), in the frontend. Each piece of state should have a **single source of truth**: one owner, from which everything else is derived.

### Data flows one way

In a **unidirectional data flow**, data moves down the component tree as props, and changes move up as events. A seat map receives the seats as props. When the user clicks a seat, the map doesn't change the seats itself; it raises "seat 12A selected", and the component that owns the seat data updates it. The new data then flows down again, and the map redraws.

This makes changes easy to follow: to find out why something changed, find its owner. When two sibling components need the same state, move it up to their nearest common parent and pass it down to both. That is called **lifting state up**. Sometimes state has to pass through many layers of components that don't use it, just to reach one that does. The resulting **prop drilling** is a sign to use the framework's way of providing values to a whole subtree, such as React context or Angular dependency injection. It can also be a sign to move the state into a shared service or a **state store**, a single object holding shared application state with defined ways to change it, such as Redux or NgRx.

### Replace, don't mutate

Frameworks need to know when state has changed, and the cheap way to check is to compare references, as in Values, variables and references (2.4). If the reference is the same, nothing changed. So state updates should create new objects and arrays rather than changing existing ones: `[...flights, newFlight]` rather than `flights.push(newFlight)`, and `{ ...flight, delay: 30 }` rather than `flight.delay = 30`. The spread syntax and copying array methods from Modern JavaScript (9.2) exist largely for this.

Mutating in place is the classic cause of a screen that doesn't update: the data changed, but the reference didn't, so nothing noticed. Immutable updates also make changes easy to reason about, as Immutability and pure functions (4.4) argued: an old value is never changed under anyone's feet.

### How frameworks notice changes

Frameworks use three broad strategies to turn state changes into DOM changes.

Re-render and compare, React's approach, runs a component's function again whenever its state or props change, producing a new description of its UI. The framework compares the new description with the previous one and applies only the differences to the DOM. It is simple to reason about, and it can do unnecessary work, re-running components whose output didn't change, which React's compiler and memoization reduce, as React today (9.5) explains.

**Fine-grained reactivity**, used by Angular's signals, Solid, Vue and Svelte, tracks dependencies precisely. Each piece of state is a **reactive signal**: a value that records who reads it. Anything that reads a signal while running, a computed value or a piece of the UI, is registered as depending on it. When the signal changes, only those dependents run again. Nothing else is touched.

Compile-time approaches, like Svelte's, analyse components when building the app and generate code that updates exactly the right DOM nodes, often on top of signals.

### Seeing it for real

This is a working reactive system in about fifty lines of TypeScript. It has signals, computed values and effects, plus a small component that turns props into output, and it shows derived state, immutable updates and the mutation trap. Run it with Node.

```typescript
// signals.ts: run with: node signals.ts
// A tiny reactive system: the core idea behind Angular signals, Solid, Vue's refs and Preact signals

type Subscriber = () => void;
let running: Subscriber | null = null;                 // the effect currently running, if any

function signal<T>(initial: T) {
  let value = initial;
  const subscribers = new Set<Subscriber>();
  return Object.assign(
    () => {                                            // reading a signal...
      if (running) subscribers.add(running);           // ...records that whoever is running depends on it
      return value;
    },
    {
      set(next: T) {
        if (Object.is(next, value)) return;            // the same value: nothing to do
        value = next;
        for (const s of [...subscribers]) s();         // tell everything that read it
      },
    },
  );
}

function effect(fn: () => void) {
  const run = () => {
    const outer = running;
    running = run;
    try { fn(); } finally { running = outer; }
  };
  run();
}

function computed<T>(fn: () => T) {
  const result = signal<T>(undefined as T);
  effect(() => result.set(fn()));                      // recompute whenever something it read changes
  return () => result();
}

// State, derived state and a view
type Flight = { number: string; to: string; delay: number };
const flights = signal<Flight[]>([
  { number: "BA117", to: "New York", delay: 0 },
  { number: "QF1", to: "Sydney", delay: 45 },
  { number: "SQ321", to: "Singapore", delay: 120 },
]);
const onlyLate = signal(false);                        // UI state, as from a checkbox
let filters = 0;
const visible = computed(() => {                       // derived state: computed from other state, never stored separately
  filters++;
  return onlyLate() ? flights().filter(f => f.delay > 0) : flights();
});

function Board(props: { title: string; flights: Flight[] }): string {   // a component: props in, output out
  const items = props.flights.map(f => `${f.number}${f.delay ? ` +${f.delay}` : ""}`);
  return `${props.title}: ${items.join(", ") || "none"}`;
}

let renders = 0;
effect(() => { renders++; console.log(Board({ title: "Departures", flights: visible() })); });
// Departures: BA117, QF1 +45, SQ321 +120

onlyLate.set(true);                                    // an event changes state; the view follows by itself
// Departures: QF1 +45, SQ321 +120
flights.set(flights().map(f => (f.number === "BA117" ? { ...f, delay: 30 } : f)));   // an immutable update: a new array
// Departures: BA117 +30, QF1 +45, SQ321 +120
onlyLate.set(true);                                    // the same value: no filtering, no render

flights().push({ number: "EK1", to: "Dubai", delay: 10 });   // mutating in place: the signal can't see it
console.log(`after push: ${renders} renders, ${filters} filters, board shows ${visible().length} flights`);
// after push: 3 renders, 3 filters, board shows 3 flights
flights.set([...flights()]);                           // a new array reference: now the change is noticed
// Departures: BA117 +30, QF1 +45, SQ321 +120, EK1 +10
console.log(`${renders} renders, ${filters} filters`);  // 4 renders, 4 filters
```

Notice that nothing in the code tells the board to redraw. The effect read `visible`, which read `onlyLate` and `flights`, so changing either reruns exactly the work that depends on them. Setting `onlyLate` to the value it already had does nothing at all. The `push` changed the data but not the reference, so the system had no way to know, and the board stayed stale until a new array was set.

Real signal libraries add several things this one leaves out. They forget dependencies that are no longer read, batch several changes into one update, compute lazily only when something reads the value, and avoid showing intermediate states in which one value has updated and another hasn't.

### Effects

A **reactive effect**, usually just called an effect, is code that runs when state changes and does something outside the framework's own rendering: saving to local storage, starting a timer, subscribing to a live feed, calling a browser API, logging. It is the imperative shell from Immutability and pure functions (4.4), at the edge of an otherwise declarative UI.

Effects are necessary and easy to overuse. Using an effect to compute derived state, "when the list changes, set the filtered list", recreates the two-copies problem with extra steps and extra renders. Compute it instead. Effects that start something must also clean up: unsubscribe, clear the timer or cancel the request when the component goes away or the input changes, or they leak and fire on screens that no longer exist.

### Forms

Forms are where most frontend state lives. In a **controlled input**, the component's state holds the field's value, and the input displays that state and reports every change back; the state is the single source of truth. An uncontrolled input keeps its own value in the DOM, and the code reads it when needed, such as on submit. Controlled inputs make validation, formatting and dependent fields easy. Uncontrolled ones are simpler and faster for large forms, and form libraries often mix the two.

Validation in the browser is for convenience: it gives instant feedback without a round trip. It never replaces validation on the server, as Validation and errors at the boundary (8.5) insisted, because a client can always be bypassed. Show errors next to their fields, in text that screen readers announce, not only in colour.

### Designing and testing components

A few habits keep components healthy. Give props precise types, from TypeScript (9.3), and prefer a union type for a variant, such as `size: "small" | "large"`, over a growing set of boolean flags. Let components accept child content, called children in React and content projection in Angular, instead of adding a prop for every possible piece. Keep side effects and data loading out of presentational components.

Test components the way users use them: render the component, find elements by their visible text or accessible role, click and type, and check what appears. Testing Library, available for React, Angular and others, is built around this idea. Tests that check internal state or implementation details break whenever the code is refactored, as Testing (4.6) warned.

Many organizations build a **design system**: a shared set of components, styles and rules, such as buttons, form fields and dialogs, used across all their products. It keeps interfaces consistent, and each component's accessibility is solved once. Microsoft's Fluent UI and Google's Material are public examples.

### In short

Declarative UI describes what the screen should show for the current state, and the framework updates the DOM to match. Components are reusable pieces with props in and events out, composed into a tree. Put each kind of state where it belongs: local, shared, server state as a cache, URL state for anything linkable, and form state. Store the minimum and derive the rest, with one source of truth. Let data flow down and events flow up, and lift state to a common parent when siblings share it. Replace objects and arrays instead of mutating them. Frameworks notice changes by re-rendering and comparing, or with signals that track exactly who depends on what.

### What goes wrong

**Stored derived state.** A filtered list or total is stored and falls out of step with its source. Compute it from the source instead.

**Mutation in place.** `push`, `splice` or setting a property on state leaves the reference unchanged, and the screen doesn't update. Create new objects and arrays.

**State in the wrong place.** Filters kept only in component memory are lost on reload and can't be shared. Put linkable state in the URL.

**Effects for everything.** Chains of effects that set state that triggers other effects become impossible to follow and cause extra renders. Derive values directly, and keep effects for the outside world.

**Effects without cleanup.** Timers and subscriptions keep running after a component is gone. Always clean up.

**Two-way changes everywhere.** Children changing shared data directly make it impossible to tell who changed what. Keep data flowing down and events up.

**Giant components.** One component fetches, validates, formats and renders an entire page. Split it into containers and small presentational pieces.

**Browser-only validation.** A form checks its input and the server trusts it. Validate on the server too.

### Cost model

The DOM operations themselves are the expensive part, as How a browser shows a page (9.1) described, so frameworks work to minimize them. Re-render-and-compare costs time proportional to the size of the components that re-run, so a change high in the tree that re-renders a thousand rows can cost several milliseconds. Fine-grained signals cost a little memory per dependency, and update time proportional to what actually depends on the change. Immutable updates allocate new objects, cheap for normal UI data, and they make change checks as cheap as comparing two references. Derived values should be memoized when expensive to compute, which computed signals do automatically.

### In other languages (optional)

**Blazor.** Components in C# with parameters and event callbacks, re-rendered and compared much like React, with `StateHasChanged` triggering updates.

**WPF, MAUI and XAML.** Data binding and `INotifyPropertyChanged` form an older, fine-grained reactive system: views bind to properties, and property change events update them. MVVM separates view state into view models.

**SwiftUI, Jetpack Compose and Flutter.** Native UI frameworks that adopted the same declarative model: views as functions of state, recomposed when state changes.

**Vue and Svelte.** Vue's `ref` and `computed`, and Svelte 5's runes, are signals, very close to the example above.

### Key terms

- **Declarative UI**: Describing what the interface should show for the current state, and letting a framework update the DOM.
- **Component**: A reusable piece of interface with inputs, outputs, markup and behaviour.
- **Props**: A component's inputs, passed in by its parent.
- **Component tree**: The hierarchy of components that make up an interface.
- **Local state**: State that belongs to one component, such as whether a menu is open.
- **Server state**: Data owned by the backend of which the frontend holds a cached copy.
- **URL state**: State kept in the address bar, such as filters and the current page.
- **Derived state**: Values computed from other state rather than stored.
- **Single source of truth**: One owner for each piece of state, from which everything else is derived.
- **Unidirectional data flow**: Data moving down the component tree as props, and changes moving up as events.
- **Lifting state up**: Moving shared state to the nearest common parent of the components that need it.
- **Prop drilling**: Passing props through many components that don't use them to reach one that does.
- **State store**: A single object holding shared application state, with defined ways to change it.
- **Fine-grained reactivity**: Tracking exactly which values each piece of UI reads, and updating only those pieces on change.
- **Reactive signal**: A value that records what reads it and notifies those readers when it changes.
- **Reactive effect**: Code that runs when the state it read changes, to act on the world outside the UI.
- **Controlled input**: A form field whose value is held in component state and displayed from it.
- **Design system**: A shared set of components, styles and rules used across an organization's products.

### Where this leads

The re-render-and-compare model in practice is in React today (9.5), and signals in practice in Angular today (9.6). Server state, loading and caching data from the backend is in Talking to the backend (9.7). Keeping frontend code secure, including where state such as tokens may be stored, is in Common web vulnerabilities (10.4).

### Check yourself

1. Add a `search` signal to `signals.ts` that filters flights by destination, and check that typing a new search re-runs the filter but setting the same text doesn't.
2. Add a `totalDelay` computed value, the sum of delays of the visible flights, and print it in the board.
3. Make the effect in `signals.ts` read `onlyLate` directly as well as `visible`, and count how many times it runs for one change. Explain the result.
4. In an app you know, list the state on one screen and sort it into local, shared, server, URL and form state. Find one piece that is stored but could be derived.
5. Find a screen in a real app whose filters are lost when you reload. Decide which query parameters would fix it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A total that fell out of sync

A cart stores `items` and `total` separately. Removing an item sometimes leaves the old total.

*Derive, don't store*

```typescript
// Before: const [total, setTotal] = useState(0);  // must be updated everywhere items change
const [items, setItems] = useState<Item[]>([]);
const total = items.reduce((sum, i) => sum + i.price * i.qty, 0);   // always correct
```

> **Lesson:** Store the minimum state and derive the rest during render. Two copies of a fact drift apart.

#### Example 2: Filters that vanished on refresh

Agents filter the bookings list, share the link with a colleague, and the colleague sees an unfiltered list.

*Keep linkable state in the URL*

```typescript
const [params, setParams] = useSearchParams();   // React Router
const status = params.get("status") ?? "all";

function setStatus(s: string) {
  setParams(p => { p.set("status", s); return p; });   // shareable, survives refresh and back button
}
```

> **Lesson:** Anything a user might bookmark or share belongs in the URL, not component state.
