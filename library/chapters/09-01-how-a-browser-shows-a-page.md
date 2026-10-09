---
id: "9-1"
number: "9.1"
title: "How a browser shows a page"
part: 9
part_title: "Building a frontend"
words: 3984
minutes: 17
---

# 9.1 How a browser shows a page

> **Quick look.** How a browser turns HTML into pixels, and why pages feel slow or janky.
>
> - HTML becomes the DOM, CSS rules compute styles, then layout, paint and compositing produce pixels.
> - JavaScript, input and drawing share one main thread; each frame has about 16 ms.
> - Batch layout reads and writes; animate transform and opacity; load scripts as modules or with defer.
> - Use textContent so data never becomes HTML (XSS); the same-origin policy keeps sites apart.
> - Proper HTML elements build the accessibility tree; clickable divs don't.
> - Pages can be server-rendered, single-page apps, or both with hydration.


### The big picture

A browser is one of the most complex programs most people run. In a fraction of a second it downloads text, turns it into a tree of objects, and works out where every box on the screen goes. It then paints millions of pixels and runs the page's JavaScript, all on a device that might be a cheap phone on a weak connection. Every frontend framework, from Angular to React, is built on top of this machinery. When a page feels slow or janky, the cause is almost always in how it uses it.

This chapter follows a page from HTML text to pixels: the DOM, CSS, layout, painting and compositing. It explains the main thread that runs your JavaScript and draws the page, and why one long task freezes both. It covers the budget of about 16 milliseconds per frame, and the ways code accidentally makes the browser redo expensive work. It also covers the browser's security rule that keeps sites apart, and the different ways pages can be delivered, from server-rendered HTML to single-page applications. The example page runs in any browser.

### Before you start

**Builds on:** 5.4 The event loop, 6.6 HTTP, 2.1 What a programming language is, 3.4 Trees, 8.2 API design, 6.1 Layers and packets.

**Ideas explained later:**

- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **React** (9.5): A JavaScript library for building interfaces from components, created at Facebook.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **CORS** (9.7): Cross-Origin Resource Sharing: headers with which a server lets other origins read its responses.
- **Next.js** (9.5): The most widely used framework built on React, with server rendering and server components.
- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the DOM?**

> The Document Object Model: the tree of objects the browser builds from a page's HTML. JavaScript reads and changes the page by reading and changing this tree, and the browser redraws to match.

**2. What are the main steps from HTML to pixels?**

> Parse HTML into the DOM and CSS into style rules, work out each element's styles, lay out every box's size and position, paint the boxes into layers, and composite the layers into the final image.

**3. Why does a long-running JavaScript function freeze the page?**

> The same main thread runs JavaScript, handles input and draws the page. While a function runs, clicks can't be processed and nothing new can be drawn, as with the event loop in The event loop (5.4).

**4. What is layout thrashing?**

> Alternately changing styles and reading layout values, such as an element's height, in a loop. Each read forces the browser to recalculate layout immediately. Doing all the writes first and then all the reads avoids it.

**5. What is the same-origin policy?**

> The browser's rule that a page's scripts may only read data from its own origin, meaning the same scheme, host and port. A page on one site can't read another site's responses unless that site explicitly allows it.

**6. What is the difference between server-side rendering and a single-page application?**

> With server-side rendering, the server sends ready-made HTML for each page. A single-page application sends a mostly empty page plus JavaScript that builds the interface in the browser and fetches data itself. Many modern frameworks combine the two, with hydration.

**7. Why is setting innerHTML from data dangerous?**

> Any HTML or script in the data becomes part of the page, which is how cross-site scripting attacks work. Setting `textContent`, or letting a framework insert text, treats the data as plain text.

### The browser's job

A **browser** fetches documents from servers and turns them into an interactive page. Chrome, Edge, Opera and most other browsers share one engine, Chromium's Blink. Safari uses WebKit, and Firefox uses Gecko. The engines differ in details, and all follow the same web standards, so what this chapter describes applies to all of them.

A page is built from three languages. **HTML**, HyperText Markup Language, describes content and structure: headings, paragraphs, lists, forms, images. **CSS**, Cascading Style Sheets, describes presentation: colours, fonts, sizes and layout. JavaScript, covered in Modern JavaScript (9.2), adds behaviour. The browser downloads them over HTTP, from HTTP (6.6), usually several resources at once.

### From HTML to the DOM

The browser reads the HTML text and parses it, with the kind of tokenizing and tree building from What a programming language is (2.1). The result is the **DOM**, the Document Object Model: a tree of objects, one per element, with the text inside them as leaves. `<ul>` becomes a node whose children are its `<li>` nodes. It is the tree structure from Trees (3.4).

The DOM is the live model of the page. JavaScript can find nodes, read them, change their text and attributes, add and remove them, and listen for events on them. When the DOM changes, the browser updates the screen to match. Every frontend framework is, in the end, a way of changing the DOM efficiently.

HTML parsing is forgiving: unclosed tags and misplaced elements are repaired rather than rejected, so almost any text produces some page. The parser also recognizes elements that fetch more resources, such as stylesheets, scripts and images, and starts downloading them.

### From CSS to styles

CSS is a list of rules. Each has a selector, which picks elements, such as `li.late`, and declarations, which set properties, such as `color: #b00`. Several rules can match the same element. The "cascade" in the name is the set of rules that decides which one wins, by importance, specificity and order. Many properties, such as font and colour, are inherited from parent elements.

For every element, the browser computes a final value for every property. That is style calculation. It must be redone for affected elements whenever classes, styles or the DOM change.

### Layout, paint and composite

With the tree and the styles known, the browser works out geometry. **Layout**, also called reflow, calculates each box's size and position from the rules of the layout model in use: normal document flow, flexbox or grid. Changing one element's size can move everything after it, so layout can be expensive on large pages.

Then comes **paint**: turning boxes into pixels, drawing backgrounds, borders, text and images, into one or more layers. Finally **compositing** combines the layers into the final image, usually on the graphics processor. Moving or fading a layer that is already painted only needs compositing, which is cheap. That is why animations of `transform` and `opacity` are smooth, while animating `width` or `top`, which forces layout and paint every frame, often isn't.

The sequence from receiving HTML to the first pixels is called the **critical rendering path**. Anything the browser must wait for before it can draw, such as a large stylesheet or a script that blocks parsing, delays the first view of the page.

### Scripts and the main thread

JavaScript runs on the browser's **main thread**, the same thread that parses HTML, calculates styles, runs layout and handles user input. It is the single-threaded event loop from The event loop (5.4). While a script runs, nothing else can happen on that thread: clicks wait, and no new frame can be drawn.

By default, a `<script>` tag stops HTML parsing until the script has downloaded and run, because the script might change the document. Scripts marked `defer`, and module scripts with `type="module"`, download in parallel and run after parsing finishes, in order. Scripts marked `async` run as soon as they arrive. Modern pages load their code as modules or with `defer`.

A display typically refreshes 60 times a second, so to keep animations and scrolling smooth, the browser has about 16.7 milliseconds per frame for everything: running scripts, styles, layout, paint and composite. That is the **frame budget**. A task on the main thread longer than 50 ms is called a long task; it makes the page feel unresponsive. Heavy computation belongs in a web worker, a separate thread with no access to the DOM, as The event loop (5.4) described for Node's worker threads.

### Layout thrashing

Browsers are lazy about layout, which is usually good: after you change several styles, they recalculate layout once, just before drawing the next frame. But reading a layout value, such as an element's `offsetHeight`, forces the browser to bring layout up to date right now, so it can answer.

Code that changes a style and then reads a layout value, over and over in a loop, forces a full layout on every iteration. This is **layout thrashing**, and it can turn a millisecond of work into hundreds. The fix is to batch: do all the reads first, then all the writes, or the other way round. Frameworks batch DOM updates for you, which is one of their benefits.

### Seeing it for real

Save this as `page.html` and open it in a browser with the developer tools console open. It builds part of the DOM from data, handles clicks with one listener, measures layout thrashing and counts frames.

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Departures</title>
  <style>
    li { padding: 4px; }
    li.late { color: #b00; }
  </style>
</head>
<body>
  <h1>Departures</h1>
  <ul id="board"></ul>
  <script type="module">
    // 1. Build part of the DOM from data. textContent treats text as text, never as HTML.
    const flights = [
      { number: "BA117", to: "New York", delay: 0 },
      { number: "QF1", to: "Sydney <b>via Singapore</b>", delay: 45 },
    ];
    const board = document.getElementById("board");
    for (const f of flights) {
      const item = document.createElement("li");
      item.textContent = `${f.number} to ${f.to}${f.delay ? ` (+${f.delay} min)` : ""}`;
      item.dataset.number = f.number;                 // becomes the attribute data-number="..."
      if (f.delay) item.classList.add("late");
      board.append(item);
    }
    console.log(board.children.length, "items;", board.querySelector("b") === null ? "no HTML was injected" : "HTML was injected");
    // 2 items; no HTML was injected   (the <b> shows as literal text on the page)

    // 2. One listener on the list handles clicks on every item: event delegation
    board.addEventListener("click", event => {
      const item = event.target.closest("li");
      if (item) console.log(`clicked ${item.dataset.number}`);
    });
    board.children[1].click();                        // clicked QF1

    // 3. Layout thrashing: reading layout after every write forces a new layout each time
    for (let i = 0; i < 300; i++) board.append(document.createElement("li"));
    const items = [...board.children];
    let t = performance.now();
    for (const li of items) { li.style.width = "300px"; void li.offsetHeight; }   // write, read, write, read...
    const interleaved = performance.now() - t;
    t = performance.now();
    for (const li of items) li.style.width = "301px";                              // all the writes first...
    const heights = items.map(li => li.offsetHeight);                              // ...then all the reads
    const batched = performance.now() - t;
    console.log(`interleaved ${interleaved.toFixed(1)} ms, batched ${batched.toFixed(1)} ms, ${heights.length} heights read`);
    // interleaved 67.0 ms, batched 4.7 ms, 302 heights read   (from a run in headless Chrome)

    // 4. The frame budget: requestAnimationFrame runs just before the browser draws each frame
    let frames = 0;
    const start = performance.now();
    const tick = () => { if (++frames < 30) requestAnimationFrame(tick); else console.log(`30 frames in ${(performance.now() - start).toFixed(0)} ms`); };
    requestAnimationFrame(tick);                      // 30 frames in about 500 ms on a 60 Hz display
  </script>
</body>
</html>
```

Event delegation, in step 2, uses one listener on a parent instead of one per item. Clicks bubble up from the clicked element through its ancestors, so the parent sees them all, including clicks on items added later.

### Measuring what users feel

Google's **Core Web Vitals** are three widely used measures of how a page feels. Largest Contentful Paint (LCP) is how long until the main content, such as a large image or heading, appears; good is under 2.5 seconds. Interaction to Next Paint (INP) is how long the page takes to respond visibly to clicks and key presses, across the whole visit; good is under 200 ms. Cumulative Layout Shift (CLS) measures how much content jumps around as the page loads, such as a button moving just as you tap it; good is under 0.1.

The Performance and Lighthouse panels in the browser's developer tools show what the main thread was doing, frame by frame, and flag long tasks, forced layouts and slow resources.

### The same-origin policy

A browser runs code from every site you visit, often in several tabs at once. Without rules, a malicious page could read your email from another tab. The **same-origin policy** prevents that. An **origin** is the combination of scheme, host and port: `https://www.example.com:443`. A page's scripts may freely read data from their own origin, but not responses from a different origin, unless that origin explicitly allows it with CORS, covered in Talking to the backend (9.7).

Cookies follow related rules, set with the attributes from HTTP (6.6). Much of web security is built on this policy, and on the ways attackers try to get around it, as Common web vulnerabilities (10.4) explains. The example's use of `textContent` instead of `innerHTML` is one such defence: inserting data as HTML would let any markup or script in it run as part of the page.

### Accessibility

Alongside the DOM, browsers build an **accessibility tree**: the version of the page presented to assistive technologies such as screen readers, voice control and switch devices. It comes from the same HTML. A real `<button>` is announced as a button, can be reached with the keyboard and activated with Enter or Space. A `<div>` with a click handler is none of those things unless you add them by hand.

Using the right HTML element for each job, labelling form fields, giving images text alternatives and keeping keyboard focus visible make a page usable by far more people. In many countries, it is also a legal requirement for public-facing services.

### How pages are delivered

There are several ways to get from a URL to a working interface, and modern frameworks mix them.

With **server-side rendering** (SSR), the server builds the HTML for each page and sends it complete. The page appears quickly and works without JavaScript, and search engines can read it. Classic ASP.NET MVC and Razor Pages work this way. Static site generation does the same at build time for pages that rarely change.

A **single-page application** (SPA) sends a nearly empty HTML page plus a bundle of JavaScript. The script builds the interface in the browser and fetches data from APIs, from API design (8.2). Moving between "pages" happens without full reloads, which feels like an app. The cost is a slower first view, because nothing appears until the JavaScript has downloaded and run, and more work on the user's device.

Modern frameworks, such as Next.js for React and Angular's own SSR, combine the two. The server renders the first view as HTML, and then the JavaScript attaches itself to that existing HTML and takes over, adding event handlers and state. That second step is **hydration**. Users see content quickly and still get an app-like experience. React today (9.5) and Angular today (9.6) come back to this.

### In short

A browser parses HTML into the DOM, applies CSS rules to compute styles, lays out boxes, paints them and composites layers into pixels. JavaScript changes the page by changing the DOM, on the same main thread that handles input and drawing, so long tasks freeze everything, and each frame has a budget of about 16 ms. Batch layout reads and writes, animate `transform` and `opacity`, and load scripts as modules or with `defer`. The same-origin policy keeps sites apart, `textContent` keeps data from becoming HTML, and proper HTML elements build an accessibility tree. Pages can be server-rendered, built in the browser as a single-page app, or both, with hydration.

### What goes wrong

**Inserting data as HTML.** Setting `innerHTML` from user or API data lets any markup in it run as part of the page. Use `textContent`, or a framework's normal text binding.

**Long tasks on the main thread.** A big loop or huge JSON parse freezes scrolling and clicks. Break work up, or move it to a web worker.

**Layout thrashing.** A loop that writes styles and reads sizes takes hundreds of milliseconds. Batch reads and writes.

**Animating layout properties.** Animating `width`, `height`, `top` or `left` forces layout on every frame and stutters. Animate `transform` and `opacity`.

**Render-blocking resources.** A large stylesheet or synchronous script in the page head delays everything. Load scripts with `defer` or as modules, and keep critical CSS small.

**Layout shift.** Images without sizes and late-loading banners push content around as users try to tap it. Reserve space with explicit dimensions.

**Clickable divs.** Elements built from `<div>` and click handlers can't be reached by keyboard or understood by screen readers. Use buttons, links and form elements.

### Cost model

A frame at 60 Hz allows about 16.7 ms for everything, and less on 120 Hz displays. A forced layout on a large page can cost several milliseconds, so thirty of them in a loop can miss several frames. Compositing an already painted layer is cheap; repainting large areas and re-running layout are not. JavaScript is parsed and compiled before it runs, so each extra 100 KB of script costs noticeable time on a mid-range phone, which is why bundle size matters. On mobile networks, each round trip before the first render, from Layers and packets (6.1), adds 50 to 300 ms.

### In other languages (optional)

**Blazor.** Runs C# in the browser through WebAssembly, or on the server with UI updates sent over a live connection, and still updates the same DOM.

**Server-rendered .NET.** ASP.NET Core Razor Pages and MVC render HTML on the server, and libraries such as htmx add interactivity by swapping in HTML fragments from the server instead of building the page in JavaScript.

**Native and cross-platform apps.** .NET MAUI, Flutter and React Native don't use the DOM, but follow the same ideas: a tree of views, layout, painting, a main UI thread with a frame budget, and an accessibility tree.

### Key terms

- **Browser**: A program that fetches web documents and turns them into interactive pages.
- **HTML**: HyperText Markup Language: the language that describes a page's content and structure.
- **CSS**: Cascading Style Sheets: the language that describes a page's presentation and layout.
- **DOM**: Document Object Model: the tree of objects a browser builds from a page's HTML.
- **Layout**: Calculating the size and position of every box on the page. Also called reflow.
- **Paint**: Turning laid-out boxes into pixels, in one or more layers.
- **Compositing**: Combining painted layers into the final image, usually on the graphics processor.
- **Critical rendering path**: The sequence of steps and resources needed before the browser can show the first pixels.
- **Main thread**: The browser thread that runs a page's JavaScript, handles input and renders.
- **Frame budget**: The time available to produce each frame, about 16.7 ms at 60 frames per second.
- **Layout thrashing**: Alternating style changes and layout reads, forcing a new layout each time.
- **Event delegation**: Handling events for many elements with one listener on a common parent.
- **Core Web Vitals**: Three measures of how a page feels to users: loading, responsiveness and visual stability.
- **Origin**: The scheme, host and port of a URL, which together define a site for security purposes.
- **Same-origin policy**: The browser rule that a page's scripts may only read data from their own origin.
- **Accessibility tree**: The version of a page presented to screen readers and other assistive technologies.
- **Server-side rendering**: Building a page's HTML on the server for each request. Often shortened to SSR.
- **Single-page application**: An app that loads once and then builds and updates its interface in the browser. Often shortened to SPA.
- **Hydration**: Attaching client-side JavaScript to server-rendered HTML so the page becomes interactive.

### Where this leads

The language that runs on the main thread is in Modern JavaScript (9.2), and its typed version in TypeScript (9.3). Organizing an interface into pieces that update the DOM efficiently is in Components and state (9.4). Fetching data across origins is in Talking to the backend (9.7), and attacks that target browsers are in Common web vulnerabilities (10.4).

### Check yourself

1. In `page.html`, replace `textContent` with `innerHTML` and reload. Describe what changed on the page, and why that is dangerous with real data.
2. Add a button to `page.html` that adds a new flight to the board, and check that clicking it still works through the one delegated listener.
3. Record a performance profile in your browser's developer tools while the page loads. Find the forced layouts in the interleaved loop.
4. Run Lighthouse on a page you work on and note its Largest Contentful Paint and Cumulative Layout Shift. Find the element each one points to.
5. Using only the keyboard, try to complete a main task in a web app you know. Note every control you can't reach or activate.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A passenger name that ran a script

A seat-map tooltip shows passenger names with `innerHTML`. A name like `<img src=x onerror=alert(1)>` runs script for every agent who hovers.

*Insert data as text*

```typescript
// Before: tooltip.innerHTML = `Seat ${seat.code}: ${seat.passengerName}`;
tooltip.textContent = `Seat ${seat.code}: ${seat.passengerName}`;   // data never becomes HTML

// if you must build markup, create elements:
const b = document.createElement("strong");
b.textContent = seat.passengerName;
tooltip.replaceChildren("Seat " + seat.code + ": ", b);
```

> **Lesson:** Data inserted as HTML becomes code. Use textContent or the framework's escaping, never raw innerHTML.

#### Example 2: A janky seat map

Scrolling a 400-seat map stutters. The code reads each seat's position, then changes its style, in a loop.

*Batch reads, then writes*

```typescript
// Before: for (const s of seats) { const top = s.offsetTop; s.style.transform = `translateY(${top * k}px)`; }
// each read after a write forces a fresh layout: "layout thrashing"

const tops = seats.map(s => s.offsetTop);                       // all reads
requestAnimationFrame(() => seats.forEach((s, i) =>              // all writes, once per frame
  s.style.transform = `translateY(${tops[i] * k}px)`));
```

> **Lesson:** Interleaving layout reads and writes forces repeated layout. Read everything, then write everything, and animate transform.
