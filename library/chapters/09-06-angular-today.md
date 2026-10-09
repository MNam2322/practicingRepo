---
id: "9-6"
number: "9.6"
title: "Angular today"
part: 9
part_title: "Building a frontend"
words: 3900
minutes: 17
---

# 9.6 Angular today

> **Quick look.** Modern Angular: Google's complete TypeScript framework.
>
> - Standalone components replace NgModules; built-in @if, @for (with track) and @switch replace *ngIf and *ngFor.
> - State lives in signals, computed signals and effects; signal inputs and outputs connect components.
> - Services are injected with inject(), much like .NET's container.
> - HttpClient returns RxJS observables; bridge with toSignal or resources, and avoid nested subscriptions.
> - Zoneless change detection is the default from Angular 21.
> - @defer and lazy routes load code on demand; the CLI builds, tests and migrates.


### The big picture

Angular is Google's complete framework for building web applications, and a common choice in enterprises, airlines included. Where React is a library you combine with others, Angular arrives with everything: components, a router, forms, an HTTP client, dependency injection, testing tools and a command-line tool that builds and generates code. It is written in and for TypeScript, and its structure will feel familiar to a C# developer: services, injection, decorators and strong conventions.

Angular has also changed a great deal in recent versions. Much older Angular code looks nothing like new Angular code. NgModules have given way to standalone components, `*ngIf` and `*ngFor` to a built-in `@if` and `@for` syntax, and much of RxJS in components to signals, the fine-grained reactivity from Components and state (9.4). Zone.js, which used to detect changes by watching every async operation, is no longer needed. This chapter covers modern Angular: components and templates, signals, dependency injection, HTTP and RxJS, change detection, and the tooling. The example components were run for this chapter with Angular 21, and the comments show what they rendered.

### Before you start

**Builds on:** 9.4 Components and state, 9.5 React today, 4.3 Composition and dependency injection, 8.3 A modern .NET backend, 9.1 How a browser shows a page, 8.5 Validation and errors at the boundary.

**Ideas explained later:**

- **WebSocket** (9.7): A two-way message channel between browser and server, upgraded from an HTTP connection.
- **Authentication** (10.2): Establishing who a caller is.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Runner** (11.4): The machine or container that runs a pipeline's jobs.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a standalone component?**

> A component that declares its own dependencies in its `imports` list, instead of belonging to an NgModule. It is the default since Angular 19, and new apps need no NgModules at all.

**2. What are signal, computed and effect in Angular?**

> A `signal` holds a value and notifies readers when it changes. A `computed` derives a value from other signals and recalculates only when they change. An `effect` runs code when signals it read change, for side effects outside the template.

**3. What replaced *ngFor, and why does it need track?**

> The built-in `@for` block. `track` gives each item a stable identity, like a React key, so Angular can keep each item's DOM when the list changes instead of rebuilding it.

**4. What did Zone.js do, and why is it no longer needed?**

> It patched every browser async API, such as timers and events, so Angular knew when anything might have changed and could check every component. With signals, Angular knows exactly what changed, so recent versions run without it, called zoneless change detection.

**5. How does a component get a service in modern Angular?**

> By calling `inject(FlightService)` in a field initializer or constructor. Constructor parameters still work, but `inject()` is the current style. A service marked `@Injectable({ providedIn: "root" })` has one shared instance for the whole app.

**6. What does HttpClient return, and how do you use it with signals?**

> An RxJS `Observable`, which emits the response when it arrives. `toSignal` turns it into a signal that templates can read, and newer resource APIs load data directly as signals.

**7. What does @defer do?**

> It marks part of a template to be loaded later, with its code split into a separate bundle: when it scrolls into view, when the browser is idle, or on interaction. It shrinks the initial download.

### Angular and AngularJS

**AngularJS** was released by Google in 2010, a JavaScript framework that popularized two-way data binding. In 2016 Google released **Angular**, a complete rewrite in TypeScript with a different architecture. The two share a name and little else, and AngularJS reached end of life in 2022. When people say Angular today, they mean the rewrite.

Angular has a new major version every six months, with long-term support for each version for 18 months, and publishes automated update commands that migrate code between versions. The changes since about version 16, in 2023, have been large enough that it is worth knowing both the older style, which you will meet in existing code, and the modern one, which this chapter follows.

### Components and templates

An Angular component is a TypeScript class with a `@Component` decorator, which plays the role of a C# attribute: metadata attached to the class. The decorator gives the component's selector, the custom element name used to place it, such as `<app-flight-board>`; its **component template**, the HTML describing its view, inline or in a separate file; its styles; and its imports.

A **standalone component** lists in `imports` the other components, directives and pipes its template uses. Standalone components have been the default since Angular 19. Older apps grouped components into NgModules, which declared what belonged together; existing code is full of them, and new code doesn't need them.

Templates are HTML extended with bindings. `{{ flight.number }}` inserts a value as text, escaped so it can't become HTML. `[disabled]="isHeld(n)"` is a **property binding**, setting a DOM property from an expression. `(click)="hold(n)"` is an **event binding**, running code when the event fires. Components talk to their parents through inputs and outputs, as Components and state (9.4) described: `[flights]="flights()"` passes data down, and `(seatHeld)="..."` listens for events coming up.

Angular 17 added built-in control flow to templates. `@if` and `@else` show content conditionally, and `@switch` chooses between cases. `@for (flight of flights(); track flight.number)` repeats content for each item. Its `track` expression is required, and plays the role of a React list key from React today (9.5): it identifies each item, so Angular can keep its DOM when the list changes. An `@empty` block shows content when the list is empty. These blocks replace the older `*ngIf` and `*ngFor` directives.

### Signals

Signals, added in Angular 16, are now the centre of Angular's reactivity. They work like the small system in Components and state (9.4), with the missing pieces filled in.

`signal(initial)` creates a writable signal. Read it by calling it, `search()`, and change it with `set` or `update`, as in `held.update(current => [...current, number])`. `computed(() => ...)` creates a read-only signal derived from others, recalculated only when something it read has changed, and only when someone reads it. `effect(() => ...)` runs code whenever signals it read change, for side effects such as saving to local storage. `linkedSignal` is a writable signal that resets itself when a source changes, such as a selected flight that resets when the list of flights is replaced.

Component inputs and outputs have signal forms too. A **signal input**, `flights = input.required<Flight[]>()`, is a read-only signal set by the parent. `output<string>()` declares an event the component emits, and `model()` declares a two-way binding. Templates read signals by calling them, and Angular tracks those reads to know exactly which parts of which components need updating.

### Dependency injection

Angular has had dependency injection from the start, and it works much like .NET's, from Composition and dependency injection (4.3). An **Angular service** is a class marked `@Injectable`. With `providedIn: "root"`, one instance is shared across the app, like a singleton registration. Services can also be provided for a single component and its children, which gives each subtree its own instance, similar to a scope.

The modern way to receive a dependency is the `inject()` function: `private readonly http = inject(HttpClient)`, in a field initializer or constructor. Constructor parameters still work. `inject()` only works while Angular is creating something, called an injection context. It is also how the rest of Angular's features are reached: the router, the current route's parameters, forms and HTTP.

### HTTP and RxJS

Angular's `HttpClient`, added to an app with `provideHttpClient()`, makes typed requests: `http.get<Flight[]>(url)`. It returns an **Observable**, from the **RxJS** library. An observable is a stream of values over time. It is lazy: nothing happens until something subscribes, and each subscription runs it again. For an HTTP request, the stream emits the response once and completes. For other sources, such as keystrokes, WebSocket messages or router events, it can emit many values. RxJS operators transform streams, such as `map`, `filter`, `debounceTime`, and `switchMap`, which cancels the previous request when a new one starts.

RxJS is powerful and has a steep learning curve, and earlier Angular code used it for nearly all state. Modern Angular uses signals for state and keeps RxJS for streams of events and for HTTP. The two meet through interop functions. `toSignal(observable)` subscribes and exposes the latest value as a signal, unsubscribing when the component is destroyed. `toObservable(signal)` goes the other way. Newer resource APIs, `resource` and `httpResource`, load data straight into signals with loading and error states, and are becoming the preferred way to fetch data into components.

HTTP **interceptors** are functions that process every request and response, like middleware from A modern .NET backend (8.3) on the client side. They typically add authentication headers, correlation ids or retry logic.

### Change detection

**Change detection** is how a framework decides which parts of the view need updating after something happens. Angular originally used Zone.js, a library that patches every asynchronous browser API: timers, promises, event listeners, network requests. After any of them fired, Angular knew that something might have changed, and checked every component's bindings, from the top of the tree down. It worked without developers thinking about it, and it did a lot of unnecessary checking, while code that ran outside those APIs sometimes didn't update the screen at all.

The `OnPush` change detection strategy narrowed the checking to components whose inputs had changed or that asked for a check. Signals complete the picture: because Angular knows which template read which signal, it can update exactly what depends on a change. Recent versions therefore run without Zone.js, called zoneless change detection, which is the default for new apps from Angular 21. Apps are smaller, faster and easier to debug, and the rule for developers is simple: keep state that the template shows in signals.

### Seeing it for real

These two components are a flight board with a search box and hold buttons, and a page that loads departures through a service with `HttpClient`. They use standalone components, signal inputs and outputs, `computed`, `@for` with `track`, `@if`, `@empty`, `inject()` and `toSignal`, and run with zoneless change detection.

```typescript
// flight-board.component.ts
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from "@angular/core";

export type Flight = { number: string; to: string; delay: number };

@Component({
  selector: "app-flight-board",
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label>
      Destination
      <input [value]="search()" (input)="search.set(asInput($event).value)" />
    </label>
    <ul>
      @for (flight of visible(); track flight.number) {
        <li>
          {{ flight.number }} to {{ flight.to }}
          @if (flight.delay > 0) { (+{{ flight.delay }} min) }
          <button [disabled]="isHeld(flight.number)" (click)="hold(flight.number)">
            {{ isHeld(flight.number) ? "Held" : "Hold seat" }}
          </button>
        </li>
      } @empty {
        <li>No flights match.</li>
      }
    </ul>
  `,
})
export class FlightBoardComponent {
  readonly flights = input.required<Flight[]>();            // a signal input, set by the parent
  readonly seatHeld = output<string>();                     // an event up to the parent

  protected readonly search = signal("");                   // local state
  protected readonly held = signal<string[]>([]);
  protected readonly visible = computed(() => {             // derived state, recomputed only when its inputs change
    const query = this.search().trim().toLowerCase();
    return this.flights().filter(f => f.to.toLowerCase().includes(query));
  });

  protected isHeld(number: string) { return this.held().includes(number); }

  protected hold(number: string) {
    this.held.update(current => [...current, number]);      // a new array, as with React
    this.seatHeld.emit(number);
  }

  protected asInput(event: Event) { return event.target as HTMLInputElement; }
}
```

```typescript
// app.component.ts
import { Component, Injectable, inject, signal } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { toSignal } from "@angular/core/rxjs-interop";
import { type Flight, FlightBoardComponent } from "./flight-board.component";

@Injectable({ providedIn: "root" })                         // one shared instance for the whole app
export class FlightService {
  private readonly http = inject(HttpClient);                // dependency injection with inject()
  departures(airport: string) { return this.http.get<Flight[]>(`/api/airports/${airport}/departures`); }
}

@Component({
  selector: "app-root",
  imports: [FlightBoardComponent],                           // a standalone component lists what its template uses
  template: `
    <h1>Departures</h1>
    <app-flight-board [flights]="flights()" (seatHeld)="lastHeld.set($event)" />
    @if (lastHeld(); as number) { <p role="status">Holding a seat on {{ number }}</p> }
  `,
})
export class AppComponent {
  private readonly service = inject(FlightService);
  protected readonly flights = toSignal(this.service.departures("LHR"), { initialValue: [] as Flight[] });   // an Observable as a signal
  protected readonly lastHeld = signal<string | null>(null);
}

// main.ts: bootstrapApplication(AppComponent, { providers: [provideHttpClient()] });
// Zoneless change detection is the default for new apps from Angular 21; add provideZonelessChangeDetection() before that.
```

With the HTTP response faked in a test, the page rendered this at each step:

```text
before the response:  Departures  Destination  No flights match.
after the response:   Departures  Destination  BA117 to New York [Hold seat]  QF1 to Sydney (+45 min) [Hold seat]
after typing "syd":   Departures  Destination  QF1 to Sydney (+45 min) [Hold seat]
after clicking Hold:  Departures  Destination  QF1 to Sydney (+45 min) [Held, disabled]  Holding a seat on QF1
after typing "xyz":   Departures  Destination  No flights match.  Holding a seat on QF1
```

Nothing in either component tells the view to refresh. Typing sets the `search` signal, which invalidates `visible`, which updates the list. Clicking updates `held` in the child, and the emitted event sets `lastHeld` in the parent. With zoneless change detection, those signal changes are the only reason anything updates.

### Loading less, sooner

A `@defer` block creates a **deferrable view**: part of a template, such as a seat map below the fold, that is loaded later. The component code inside it is split into a separate bundle, and loaded on a trigger: when it scrolls into view, when the browser is idle, on hover or click, or after a timer. `@placeholder` and `@loading` blocks show content meanwhile. It is one of the simplest ways to shrink a page's initial download.

The router can load whole feature areas lazily too, with `loadComponent` and `loadChildren` in route definitions, so each part of a large app is downloaded only when the user goes there.

Angular also supports server-side rendering and hydration, from How a browser shows a page (9.1), with the `@angular/ssr` package. Incremental hydration combines hydration with `@defer` triggers, so parts of a server-rendered page become interactive only when needed.

### Forms

Angular has two established forms systems. Template-driven forms bind inputs with `ngModel` and suit simple forms. Reactive forms build the form's structure in the component class, with `FormGroup` and `FormControl` objects and validators, and suit complex, dynamic forms with rules that span fields. Both report validity and errors per control. A signal-based forms API is in development in recent versions. As always, validation in the browser is a convenience, and the server checks again, as Validation and errors at the boundary (8.5) required.

### Tooling

The **Angular CLI**, the `ng` command, creates projects, generates components and services, runs a development server, builds for production, runs tests and applies version migrations with `ng update`. Its build uses esbuild and Vite, and is fast. New projects are TypeScript with strict mode on. Templates are compiled ahead of time and type-checked against their components, which catches binding mistakes at build time. Unit tests run with Vitest in new projects, replacing the long-standing Karma runner, and Testing Library works with Angular as with React.

### Angular or React?

Both are mature and fully capable, and the choice usually comes from the team and the organization. Angular is a complete, opinionated platform: one way to do routing, forms, HTTP, injection and testing, with coordinated upgrades and automated migrations. That suits large teams and long-lived enterprise applications, where consistency across many developers matters. React is a smaller core with a vast ecosystem of choices, and dominant frameworks such as Next.js on top. That gives more flexibility and a bigger hiring pool, in exchange for more decisions. Underneath, their ideas have converged: components, one-way data flow, derived state, and, in Angular's case directly and in React's through its compiler, precise tracking of what changed.

### In short

Angular is Google's complete TypeScript framework, a 2016 rewrite unrelated to AngularJS. Modern Angular uses standalone components with inline or file templates, property and event bindings, and the built-in `@if`, `@for` with `track`, and `@switch` blocks. State lives in signals, computed signals and effects, with signal inputs and outputs between components. Services are injected with `inject()`, much like .NET's container. `HttpClient` returns RxJS observables, bridged to signals with `toSignal` or loaded through resources. Signals allow zoneless change detection, the default from Angular 21. `@defer` and lazy routes load code on demand, and the CLI builds, tests and migrates.

### What goes wrong

**Mixing old and new styles.** NgModules, `*ngFor`, constructor injection and signals scattered across one codebase make it hard to follow. Migrate with the CLI's automated schematics and pick one style for new code.

**State outside signals in zoneless apps.** A plain field changed in a callback doesn't update the view. Keep displayed state in signals.

**Forgotten subscriptions.** Manually subscribed observables keep running after a component is destroyed, leaking memory and firing on dead screens. Use `toSignal`, the `async` pipe or `takeUntilDestroyed`.

**Nested subscriptions.** Subscribing inside another subscription's callback creates races and leaks. Use RxJS operators such as `switchMap`, or signals and resources.

**Effects for derived values.** An `effect` that sets another signal whenever a source changes recreates stored derived state. Use `computed` or `linkedSignal`.

**Missing or unstable `track`.** Tracking by index or by a new object each time rebuilds the DOM on every change. Track by a stable id.

**Huge initial bundles.** Everything loads up front. Use lazy routes and `@defer`.

### Cost model

Signal reads and writes cost little, and zoneless change detection updates only the views that depend on what changed, unlike Zone.js-based checking, which could walk the whole component tree after every event. Ahead-of-time compilation turns templates into efficient instructions at build time, so no template compiler is shipped to the browser. The framework adds tens of kilobytes compressed to a bundle before your own code. Lazy routes and `@defer` move feature code out of the initial download, often cutting it by more than half in large apps.

### In other languages (optional)

**React.** The same component ideas with JSX instead of templates, hooks instead of decorators and services, and re-rendering with a compiler instead of signals, as React today (9.5) described.

**Vue.** Templates and signal-style reactivity, closer to Angular's templates but lighter, with an optional router and state library.

**Blazor.** For a C# team, Blazor offers components, dependency injection and templates in Razor syntax, running in the browser through WebAssembly or on the server.

**RxJS elsewhere.** The same reactive-streams model exists as Reactive Extensions in .NET (System.Reactive), where it began, and in many other languages.

### Key terms

- **AngularJS**: Google's original JavaScript framework from 2010, replaced by Angular and now retired.
- **Angular**: Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **Component template**: The HTML with bindings that describes an Angular component's view.
- **Standalone component**: An Angular component that lists its own dependencies in imports, without an NgModule.
- **Property binding**: Setting a DOM property from an expression in a template, written [property]="expression".
- **Event binding**: Running code when a DOM event fires, written (event)="statement".
- **Signal input**: A component input declared with input(), read as a signal inside the component.
- **Angular service**: A class marked @Injectable that Angular's dependency injection creates and provides.
- **Observable**: A lazy stream of values over time, from RxJS, which runs when subscribed.
- **RxJS**: The Reactive Extensions library for JavaScript, used by Angular for HTTP and event streams.
- **Interceptor**: A function that processes every HTTP request and response in an Angular app.
- **Change detection**: How a framework decides which parts of the view to update after something happens.
- **Zone.js**: A library that patches browser async APIs so Angular knows when to check for changes.
- **Deferrable view**: A @defer block whose content and code are loaded later, on a trigger.
- **Angular CLI**: The ng command that creates, builds, tests and updates Angular projects.

### Where this leads

Fetching data, handling errors and live updates in any framework is in Talking to the backend (9.7). Securing an Angular or React frontend, including where to keep tokens, is in Authentication and authorization (10.2) and Common web vulnerabilities (10.4). Building and deploying frontends in pipelines is in CI/CD pipelines (11.4).

### Check yourself

1. Add a "Release" button to held rows in `FlightBoardComponent` that removes the flight from `held` without mutating the array.
2. Replace the `computed` search with one that also hides flights delayed by more than two hours when a new `hideVeryLate` signal is true.
3. Rewrite `AppComponent` to load departures with `httpResource`, and show "Loading..." and an error message using the resource's status.
4. Wrap a heavy part of a page in a real Angular app in `@defer (on viewport)` with a placeholder, and compare the initial bundle size before and after.
5. In an existing Angular codebase, find one `*ngFor` or NgModule and migrate it with the CLI's control flow or standalone migration schematic.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A computed total with signals

An Angular booking summary recalculates totals in several places and sometimes shows stale numbers.

*computed() instead of manual updates*

```typescript
@Component({
  selector: "app-summary",
  template: `@for (l of lines(); track l.id) { <p>{{ l.name }}: {{ l.price }}</p> }
             <strong>Total: {{ total() }}</strong>`,
})
export class SummaryComponent {
  lines = input.required<Line[]>();
  total = computed(() => this.lines().reduce((s, l) => s + l.price, 0));   // recalculated only when lines change
}
```

> **Lesson:** Signals track dependencies exactly. Derive values with computed rather than updating them by hand.

#### Example 2: Nested subscriptions that leaked

A component subscribes to route params, then inside that subscribes to an HTTP call, and never unsubscribes.

*Compose and convert*

```typescript
private route = inject(ActivatedRoute);
private api = inject(BookingApi);

booking = toSignal(
  this.route.paramMap.pipe(
    map(p => p.get("id")!),
    switchMap(id => this.api.get(id)),     // cancels the previous request when the id changes
  ),
);   // unsubscribes automatically with the component
```

> **Lesson:** Flatten streams with switchMap and bridge to signals with toSignal; no manual subscriptions to forget.
