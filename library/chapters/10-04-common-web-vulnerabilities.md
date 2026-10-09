---
id: "10-4"
number: "10.4"
title: "Common web vulnerabilities"
part: 10
part_title: "Security"
words: 6965
minutes: 30
---

# 10.4 Common web vulnerabilities

> **Quick look.** The small number of ways web apps are attacked, and the standard defence for each.
>
> - Broken access control: deny by default and check ownership on every request.
> - Injection (SQL, shell commands, XSS): keep data apart from code with parameters, argument lists and output encoding.
> - Add a strict Content Security Policy and security headers (nosniff, HSTS, Referrer-Policy).
> - CSRF: SameSite cookies, anti-forgery tokens and origin checks.
> - SSRF: allowlist destinations; resolve and check file paths; allow only local redirects.
> - Bind to input DTOs to stop over-posting; deserialize only known types.
> - Built-in rate limiting is per instance unless backed by a shared store; keep dependencies patched.


### The big picture

Web applications are attacked in a surprisingly small number of ways. The same families of flaw have topped vulnerability lists for twenty years. An access check is missing. Data gets treated as code. A server fetches or reads whatever it is told to. And a browser is tricked into acting on a user's behalf. Once you know the families, you start seeing them in code review. And almost every one has a standard defence that modern frameworks already provide, if you don't switch it off.

This chapter works through the main families as the OWASP Top 10 groups them. It covers broken access control, injection, cross-site scripting, cross-site request forgery, server-side request forgery, path traversal, open redirects, unsafe deserialization, misconfiguration and vulnerable components. For each, it covers how the attack works, what it achieves, and the defence, including where ASP.NET Core, React and Angular handle it for you. The example runs six real attacks against a careless and a hardened version of the same small site. The cross-site scripting pages were also loaded in a real browser, to confirm that the injected scripts run on one and not the other.

### Before you start

**Builds on:** 10.1 Thinking like an attacker, 10.2 Authentication and authorization, 9.7 Talking to the backend, 7.2 The relational model and SQL, 1.7 The operating system, 8.5 Validation and errors at the boundary.

**Ideas explained later:**

- **Code review** (13.2): Another person reading a change before it is merged, to check its correctness, design and safety.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Nonce** (10.5): A value used only once with a given key, such as the 12-byte input to AES-GCM.
- **AWS** (11.5): Amazon Web Services, the largest cloud provider, launched in 2006.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the root cause shared by SQL injection, command injection and cross-site scripting?**

> Untrusted data is mixed into something that gets interpreted as code: SQL, a shell command or HTML. The defence is to keep data and code separate, with parameters, argument lists and context-aware output encoding.

**2. What is the difference between reflected and stored cross-site scripting?**

> Reflected XSS puts the attacker's script in a request, such as a search term in a link, and the page echoes it back to whoever opens that link. Stored XSS saves the script, such as in a review, and serves it to every visitor.

**3. How does a Content Security Policy help against XSS?**

> It tells the browser which sources of script, and other content, the page may use. With a strict policy, injected inline scripts and event handlers don't run, even if encoding was missed somewhere. It is a second layer, not a replacement for encoding.

**4. Why can't an attacker's site read your API's responses, yet still cause harm with cross-site request forgery?**

> The same-origin policy stops the attacker's page reading responses, but the browser still sends requests to your site, with its cookies attached, when the attacker's page submits a form. If your site acts on such requests, the damage is done without reading anything.

**5. What is server-side request forgery, and why is it dangerous in the cloud?**

> Making a server fetch a URL the attacker chooses, such as an internal admin service. In the cloud, a metadata address such as 169.254.169.254 can hand out the server's credentials, so an SSRF flaw can become full access to the cloud account.

**6. How do you safely serve a file whose name comes from a request?**

> Better, don't: map an id to a known file. Otherwise resolve the full path, then check it is still inside the intended folder before opening it, so names like `../secrets.env` are refused.

**7. What is an open redirect, and how do you prevent it?**

> A page that redirects to any address given in a parameter, such as `returnUrl`. Attackers use your trusted domain to send victims to phishing pages. Allow only local paths, or a fixed list of destinations.

### A list everyone uses

**OWASP**, the Open Worldwide Application Security Project, is a non-profit community that publishes free security guidance and tools, including the ASVS checklist from Thinking like an attacker (10.1). Its best-known publication is the **OWASP Top 10**, a list of the most critical kinds of web application risk, revised every few years from data on real applications. Broken access control has been at the top in recent editions. Injection, cryptographic failures, insecure design, misconfiguration, vulnerable components, authentication failures, integrity failures, logging failures and server-side request forgery make up most of the rest.

Behind that list are a handful of root causes: trusting input, mixing data with code, missing checks, and unsafe defaults. The sections below go family by family.

### Broken access control

**Broken access control** means users can do or see things they shouldn't. The insecure direct object reference from Authentication and authorization (10.2), reading another passenger's booking by changing its id, is the most common form. Others include admin functions protected only by not being linked from the menu, and a `role` field in a request that the server believes. Missing tenant filters are another, as are CORS policies that let any site make authenticated calls, from Talking to the backend (9.7).

The defences come from that chapter: deny by default, check every request on the server, and build ownership and tenant conditions into every query. Test access control deliberately, too. For each endpoint, write a test that tries it as a user who shouldn't have access.

### Injection

**Injection** happens whenever untrusted data is pasted into something that another interpreter will parse as code. SQL injection, from The relational model and SQL (7.2), is the classic. **Command injection** does the same with an operating system shell: building `"convert " + fileName + " out.pdf"` and running it through a shell lets a file name like `x; rm -rf /` run a second command. The same pattern appears with LDAP queries, NoSQL query objects, XPath, template engines and regular expressions.

The defence is always to keep data and code apart through an API designed for it. For SQL, use parameters. For processes, pass arguments as a list, as `ProcessStartInfo.ArgumentList` does, and never through a shell, as The operating system (1.7) described. For other interpreters, use their parameter or escaping mechanism. Input validation, from Validation and errors at the boundary (8.5), is a useful extra layer, but not the fix: a perfectly valid name like `O'Brien` still breaks a query built by concatenation.

### Cross-site scripting

**Cross-site scripting** (XSS) is injection into a web page. Untrusted text ends up in HTML without being encoded, so the browser parses it as markup and runs any script inside. The script runs as part of your site, with your origin, from How a browser shows a page (9.1). It can read the page, make requests with the user's cookies, change what the user sees, and capture what they type. That is why a single XSS flaw can mean full account takeover.

It comes in three forms. In **reflected XSS**, the script is part of a request, typically a link: a search page that echoes the search term back unencoded runs whatever script the link contained. In **stored XSS**, the script is saved, in a review, a name or a message, and served to everyone who views it. In DOM-based XSS, the page's own JavaScript takes data, such as part of the URL, and writes it into the page with `innerHTML` or similar.

The main defence is **output encoding**: converting characters that are special in the output's context into harmless forms, such as `<` into `&lt;` in HTML. Characters that are special depend on the context: HTML text, HTML attributes, JavaScript strings and URLs each need their own encoding. Modern frameworks do this by default. Razor encodes `@value`, React encodes values in JSX, from React today (9.5), and Angular encodes template interpolation, from Angular today (9.6). So XSS in modern apps usually comes through their escape hatches: `Html.Raw` in Razor, `dangerouslySetInnerHTML` in React, `bypassSecurityTrustHtml` in Angular, and `innerHTML` in plain JavaScript. When users really must provide formatted text, clean it with a maintained sanitizer library, such as DOMPurify in the browser or HtmlSanitizer in .NET, which removes everything not on an allowlist of safe tags and attributes.

A **Content Security Policy** (CSP) adds defence in depth. It is a response header listing the sources the page may load scripts, styles, images and frames from. A strict policy, such as `script-src 'self'`, stops inline scripts and inline event handlers from running at all, so an injected `<script>` or `onerror` attribute does nothing even where encoding was missed. Policies that need inline scripts can allow specific ones with a random value per response, called a nonce. HttpOnly cookies, from Authentication and authorization (10.2), limit the damage further, because scripts can't read them.

### Cross-site request forgery

**Cross-site request forgery** (CSRF) makes a user's browser send a request to your site from someone else's page. The victim, signed in to the airline, visits an attacker's page. That page contains a hidden form that posts to the airline's "transfer miles" endpoint, and submits itself. The browser sends the request, with the airline's cookies attached. The same-origin policy stops the attacker reading the response, but the transfer has already happened.

Defences stack. **SameSite** cookies, from Authentication and authorization (10.2), stop browsers attaching the cookie to cross-site posts: `Lax`, now the default in major browsers, blocks cross-site form posts, and `Strict` blocks all cross-site requests. An **anti-forgery token** is a random value tied to the user's session and included in each form or request; an attacker's page can't read it, so it can't include it. ASP.NET Core adds and checks these automatically for Razor forms, and provides antiforgery middleware for minimal APIs. Checking the `Origin` header against your own origins catches the rest. APIs that authenticate with bearer tokens in a header, rather than cookies, aren't exposed to CSRF, because browsers never attach those headers automatically.

### Server-side request forgery

**Server-side request forgery** (SSRF) turns your server into the attacker's proxy. A feature that fetches a URL supplied by the user, such as importing a partner's logo, generating a preview or calling a webhook, can be pointed at addresses the attacker can't reach directly. Those include internal services, admin interfaces, databases and cloud metadata endpoints. In the cloud, the metadata address `169.254.169.254` can return temporary credentials for the server's identity. In 2019, an SSRF flaw used that way led to the theft of data on over 100 million Capital One customers.

Naive defences fail easily. A check that the URL's host isn't "localhost" is bypassed with `127.0.0.1`, an IPv6 form, a decimal-encoded address, a domain that resolves to an internal address, or a redirect from an allowed site to a forbidden one. Robust defences allow only an explicit list of destinations where possible. Otherwise, resolve the host and refuse private, loopback and link-local addresses, check again after any redirect or disable redirects, and restrict at the network level which destinations the server can reach at all. On AWS, requiring version 2 of the metadata service blocks the simple forms of the metadata attack.

### Path traversal

**Path traversal** uses file names containing `../` to escape the folder a feature is meant to read from, and read configuration files, keys or other users' uploads. Encoded forms such as `%2e%2e%2f`, and backslashes on Windows, get past simple string checks. The defence is to resolve the full path first, with `Path.GetFullPath` in .NET or `path.resolve` in Node, then check that it still starts with the intended folder plus a separator. Better still, never use names from requests as paths: store files under generated ids and look up the id.

### Open redirects

An **open redirect** is a page that redirects to any address given in a parameter, such as the `returnUrl` after sign-in. It looks harmless, since it only sends users elsewhere. But a link that starts with your trusted domain and lands on a convincing fake sign-in page is ideal for phishing, and in OAuth flows, from OAuth 2 and OpenID Connect (10.3), redirects can leak authorization codes. Allow only local paths: in ASP.NET Core, `Url.IsLocalUrl` and `Results.LocalRedirect` do this correctly, rejecting protocol-relative addresses such as `//evil.example`, which a check for a leading `/` alone would miss.

### Seeing it for real

This file starts a careless and a hardened version of the same small site, plus an internal service that should never be reachable from outside. It then attacks both versions with the six attacks above. It needs only Node, in a folder whose `package.json` says `"type": "module"`.

```typescript
// attacks.ts: run with: node attacks.ts   (a careless and a hardened version of one site, and six real attacks)
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, sep } from "node:path";

// Files on disk: a public folder, and a secret next to it
const root = join(tmpdir(), "attacks-demo"), publicDir = join(root, "public");
mkdirSync(publicDir, { recursive: true });
writeFileSync(join(publicDir, "timetable.txt"), "BA117 08:25 LHR-JFK");
writeFileSync(join(root, "secrets.env"), "DB_PASSWORD=hunter2");

// An internal-only service that must never be reachable from outside
const internal = createServer((_req, res) => res.end("internal admin: 12,408 passenger records"));
await new Promise<void>(r => internal.listen(0, "127.0.0.1", r));
const internalUrl = `http://127.0.0.1:${(internal.address() as { port: number }).port}/admin`;

const escapeHtml = (s: string) => s.replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
const body = async (req: IncomingMessage) => { let raw = ""; for await (const c of req) raw += c; return raw ? JSON.parse(raw) : {}; };

function start(hardened: boolean): Promise<string> {
  const reviews: string[] = [];
  const server = createServer(async (req: IncomingMessage, res: ServerResponse) => {
    const url = new URL(req.url!, "http://localhost");
    const text = hardened ? escapeHtml : (s: string) => s;               // the one difference for XSS: encode output
    const page = (html: string) => {
      if (hardened) res.setHeader("Content-Security-Policy", "default-src 'self'; script-src 'self'; frame-ancestors 'none'");
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" }).end(`<!doctype html><body>${html}</body>`);
    };

    if (url.pathname === "/search")                                         // 1. reflected XSS
      return page(`<h1>Results for ${text(url.searchParams.get("q") ?? "")}</h1>`);
    if (url.pathname === "/reviews" && req.method === "POST") { reviews.push((await body(req)).text); return res.end("saved"); }
    if (url.pathname === "/reviews")                                        // 2. stored XSS
      return page(reviews.map(r => `<p>${text(r)}</p>`).join(""));

    if (url.pathname === "/files") {                                       // 3. path traversal
      const requested = resolve(publicDir, url.searchParams.get("name") ?? "");
      if (hardened && !requested.startsWith(publicDir + sep)) return res.writeHead(404).end("not found");
      try { return res.end(readFileSync(requested, "utf8")); } catch { return res.writeHead(404).end("not found"); }
    }

    if (url.pathname === "/logo" && req.method === "POST") {                // 4. server-side request forgery
      const target = new URL((await body(req)).url);
      if (hardened && !(target.protocol === "https:" && target.hostname === "cdn.partner.example"))
        return res.writeHead(400).end("only partner logos are allowed");
      try { return res.end(await (await fetch(target)).text()); } catch { return res.writeHead(502).end("fetch failed"); }
    }

    if (url.pathname === "/login-done") {                                  // 5. open redirect
      const back = url.searchParams.get("returnUrl") ?? "/";
      const safe = back.startsWith("/") && !back.startsWith("//") && !back.includes("\\");
      return res.writeHead(302, { Location: hardened && !safe ? "/" : back }).end();
    }

    if (url.pathname === "/miles/transfer" && req.method === "POST") {     // 6. cross-site request forgery
      const signedIn = req.headers.cookie?.includes("session=ada");
      if (!signedIn) return res.writeHead(401).end("sign in");
      if (hardened && req.headers.origin !== "https://www.airline.example") return res.writeHead(403).end("cross-site request refused");
      return res.end("transferred 50,000 miles");
    }
    res.writeHead(404).end("not found");
  });
  return new Promise(r => server.listen(0, "127.0.0.1", () => r(`http://127.0.0.1:${(server.address() as { port: number }).port}`)));
}

async function attack(name: string, base: string) {
  console.log(`\n${name}`);
  const show = (label: string, value: string) => console.log(`  ${label.padEnd(22)} ${value.replace(/\s+/g, " ").slice(0, 96)}`);
  const xss = "<script>document.title='pwned'</script>";
  show("reflected XSS:", await (await fetch(`${base}/search?q=${encodeURIComponent(xss)}`)).text());
  await fetch(`${base}/reviews`, { method: "POST", body: JSON.stringify({ text: "<img src=x onerror=\"document.title='pwned'\">" }) });
  show("stored XSS:", await (await fetch(`${base}/reviews`)).text());
  show("path traversal:", await (await fetch(`${base}/files?name=${encodeURIComponent("../secrets.env")}`)).text());
  show("SSRF:", await (await fetch(`${base}/logo`, { method: "POST", body: JSON.stringify({ url: internalUrl }) })).text());
  const redirect = await fetch(`${base}/login-done?returnUrl=${encodeURIComponent("https://evil.example/fake-login")}`, { redirect: "manual" });
  show("open redirect:", `Location: ${redirect.headers.get("location")}`);
  const forged = await fetch(`${base}/miles/transfer`, { method: "POST", headers: { Cookie: "session=ada", Origin: "https://evil.example" } });
  show("CSRF:", `${forged.status} ${await forged.text()}`);
}

const careless = await start(false), hardened = await start(true);
await attack("careless version", careless);
await attack("hardened version", hardened);
console.log(`\nopen these to watch the scripts run, or not:\n  ${careless}/reviews\n  ${hardened}/reviews`);
if (!process.env.KEEP_RUNNING) process.exit(0);
```

The output from a real run:

```text
careless version
  reflected XSS:         <!doctype html><body><h1>Results for <script>document.title='pwned'</script></h1></body>
  stored XSS:            <!doctype html><body><p><img src=x onerror="document.title='pwned'"></p></body>
  path traversal:        DB_PASSWORD=hunter2
  SSRF:                  internal admin: 12,408 passenger records
  open redirect:         Location: https://evil.example/fake-login
  CSRF:                  200 transferred 50,000 miles

hardened version
  reflected XSS:         <!doctype html><body><h1>Results for &lt;script&gt;document.title=&#39;pwned&#39;&lt;/script&gt;
  stored XSS:            <!doctype html><body><p>&lt;img src=x onerror=&quot;document.title=&#39;pwned&#39;&quot;&gt;</p>
  path traversal:        not found
  SSRF:                  only partner logos are allowed
  open redirect:         Location: /
  CSRF:                  403 cross-site request refused
```

The script pages were then loaded in headless Chrome. On the careless version, both injected scripts ran and changed the page title to "pwned". On the hardened version, neither ran: the encoded text displayed harmlessly, and the Content Security Policy would have blocked inline scripts anyway. Run it with `KEEP_RUNNING=1 node attacks.ts` and open the printed addresses to see the same in your own browser.

The hardened version's SSRF check is the simple kind: an exact allowlist of one host over HTTPS. That is the strongest form. Checks that try to block bad destinations instead, rather than allow good ones, need all the extra care described above.

### Unsafe deserialization

Some serializers can create objects of any type named in the data. Deserializing untrusted input with them lets an attacker choose which classes get created and which of their methods run as a side effect, which can lead to running arbitrary code. .NET's old `BinaryFormatter` was the notorious example. It is removed from .NET 9, and throws if called. JSON serializers are safe by default, but become dangerous when configured to honour type names in the data, as with Newtonsoft.Json's `TypeNameHandling.All`. Deserialize untrusted data only into specific, known types, as Validation and errors at the boundary (8.5) recommended.

### Misconfiguration and headers

Many breaches need no code flaw at all: an admin panel with its default password, a debug page left on in production, detailed errors, directory listings, a cloud storage bucket readable by anyone, or a database open to the internet.

**Security headers** tell browsers to apply protections to your pages. `Content-Security-Policy` controls sources of content, as above, and its `frame-ancestors` directive controls who may put your page in a frame. `Strict-Transport-Security` tells browsers to use only HTTPS for your domain from now on, as TLS (6.5) described. `X-Content-Type-Options: nosniff` stops browsers guessing a response's type, so an uploaded text file isn't run as script. `Referrer-Policy` limits which URLs leak to other sites.

Framing matters because of **clickjacking**. An attacker's page shows your page inside an invisible frame, positioned so that a click the victim means for the attacker's page lands on your "confirm" button. `frame-ancestors 'none'`, or the older `X-Frame-Options: DENY`, prevents it.

### Vulnerable components

Most of an application's code is other people's: frameworks, libraries and their dependencies. A flaw in one of them is a flaw in your application. In December 2021, the Log4Shell vulnerability in log4j, a Java logging library used almost everywhere, let attackers run code on servers just by getting a crafted string logged, and affected hundreds of thousands of systems. Keep an inventory of dependencies, scan them for known vulnerabilities in every build, update them on a routine, and patch critical issues within days. Builds and dependencies (11.2) covers the tools, and A Node and TypeScript backend (8.4) covered the supply-chain risks.

### File uploads

Uploads combine several risks. Limit their size and number. Check their type from the content, not just the file name or the `Content-Type` the client sent. Store them outside the web root, under generated names, so an uploaded file can never be served as a script or page. Serve them back with `Content-Disposition: attachment` and `nosniff`. For risky formats, scan them, or convert them to a safe format, such as re-encoding images.

### In short

A few root causes explain most web vulnerabilities. Broken access control is prevented by denying by default and checking ownership on every request. Injection, including SQL, commands and XSS, is prevented by keeping data apart from code: parameters, argument lists and context-aware output encoding, which frameworks do unless you use their escape hatches. Add a strict Content Security Policy. Stop CSRF with SameSite cookies, anti-forgery tokens and origin checks. Allowlist the destinations of server-side fetches, resolve and check file paths, and allow only local redirects. Deserialize only into known types. Set security headers, remove defaults and debug features, keep components patched and handle uploads carefully.

### What goes wrong

**Escape hatches used casually.** `Html.Raw`, `dangerouslySetInnerHTML`, `bypassSecurityTrustHtml` or `innerHTML` with user data reopens XSS. Avoid them, or pass the content through a sanitizer first.

**Validation instead of encoding.** A filter strips `<script>` and misses `<img onerror>`. Encode for the output context every time.

**Shell commands built from strings.** A file name runs a second command. Pass argument lists and never go through a shell.

**Blocklist checks for SSRF.** "Not localhost" is bypassed by other forms of the same address, DNS tricks or redirects. Allowlist destinations and restrict the network.

**String checks on paths.** A check for `..` misses encoded and backslash forms. Resolve the full path, then check its prefix.

**Redirects to any `returnUrl`.** Your domain becomes a phishing tool. Allow only local paths with `IsLocalUrl` or `LocalRedirect`.

**Missing security headers.** No CSP, HSTS or frame protection leaves browsers' defences unused. Set them for every response.

**Unpatched dependencies.** A published vulnerability in a library goes unfixed for months. Scan every build and update on a schedule.

### Cost model

The defences here are nearly free at run time: output encoding costs microseconds per page, a CSP is one header, an anti-forgery check is a comparison, and path and redirect checks are a few string operations. A strict CSP costs development effort instead, because inline scripts and styles have to move into files or carry nonces. Dependency scanning adds seconds to minutes per build. The costs of the flaws, by contrast, are counted in breaches: XSS and access control flaws lead to account takeovers and data exposure, and SSRF and injection can hand over entire systems.

### In other languages (optional)

**ASP.NET Core.** Razor encodes output, and antiforgery tokens are built in for forms and available for minimal APIs. `LocalRedirect` and `Url.IsLocalUrl` prevent open redirects. Security headers can be added with a few lines of middleware, or a package such as NetEscapades.AspNetCore.SecurityHeaders.

**Node.js.** Helmet sets security headers for Express; template engines encode by default unless raw output is requested; DOMPurify sanitizes HTML.

**Python and Java.** Django and Spring Security include CSRF protection, output encoding and security header settings, with the same escape hatches to avoid, such as Django's `mark_safe`.

**Everywhere.** The OWASP Cheat Sheet Series gives concise, language-specific guidance for every vulnerability in this chapter.

### Key terms

- **OWASP**: The Open Worldwide Application Security Project, a community that publishes free security guidance and tools.
- **OWASP Top 10**: OWASP's list of the most critical kinds of web application security risk.
- **Broken access control**: Users being able to see or do things they shouldn't, because permission checks are missing or wrong.
- **Injection**: Untrusted data inserted into something an interpreter parses as code, such as SQL or a shell command.
- **Command injection**: Injection into an operating system command, letting an attacker run other commands.
- **Cross-site scripting**: Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.
- **Reflected XSS**: Cross-site scripting where the script comes from the request, such as a link, and is echoed back.
- **Stored XSS**: Cross-site scripting where the script is saved by the site and served to every viewer.
- **Output encoding**: Converting special characters for the context data is written into, so they display instead of being parsed.
- **Content Security Policy**: A response header telling the browser which sources of scripts and other content a page may use. Often shortened to CSP.
- **Cross-site request forgery**: Making a user's browser send an unwanted, authenticated request to a site from another site. Often shortened to CSRF.
- **Anti-forgery token**: A secret value included in forms and requests that another site can't know, used to stop CSRF.
- **Server-side request forgery**: Making a server fetch an address the attacker chooses, often an internal one. Often shortened to SSRF.
- **Path traversal**: Using ../ and similar tricks in a file name to read files outside the intended folder.
- **Open redirect**: A redirect to any address given in a request, which attackers use for phishing.
- **Unsafe deserialization**: Deserializing untrusted data in a way that lets the data choose which types are created.
- **Security headers**: HTTP response headers that switch on browser protections, such as CSP and HSTS.
- **Clickjacking**: Tricking users into clicking on your page shown invisibly inside another site's frame.

### Where this leads

Protecting data with cryptography, and keeping keys and secrets out of code, is in Secrets and cryptography basics (10.5). Scanning dependencies and code in the pipeline is in Builds and dependencies (11.2) and CI/CD pipelines (11.4). Logging and alerting on attacks is in Observability (11.6).

### Check yourself

1. Add a DOM-based XSS page to `attacks.ts` that writes `location.hash` into the page with `innerHTML`, attack it, then fix it with `textContent`.
2. Make the hardened `/logo` endpoint accept any HTTPS host, but refuse addresses that resolve to private or loopback ranges. Then try to get past your check with a redirect.
3. Remove the `!back.startsWith("//")` part of the redirect check and show which address gets through.
4. Add a strict Content Security Policy to an ASP.NET Core or React app you work on, in report-only mode first, and list what it would block.
5. Search a codebase you know for `Html.Raw`, `dangerouslySetInnerHTML`, `bypassSecurityTrust` and `innerHTML`, and check where each one's data comes from.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: An SSRF through a "fetch this image" feature

Users can set a profile picture by URL. Someone submits `http://169.254.169.254/latest/meta-data/iam/` and receives cloud credentials.

*Allowlist and resolve before fetching*

```csharp
static readonly HashSet<string> AllowedHosts = ["images.example-cdn.com", "lh3.googleusercontent.com"];

async Task<byte[]> FetchAvatarAsync(Uri url, CancellationToken ct)
{
    if (url.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(url.Host))
        throw new ValidationException("Image host not allowed");
    var addrs = await Dns.GetHostAddressesAsync(url.Host, ct);
    if (addrs.Any(a => IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || IsPrivate(a)))
        throw new ValidationException("Address not allowed");
    return await http.GetByteArrayAsync(url, ct);   // plus a size limit and no redirects
}
```

> **Lesson:** Server-side fetches of user-supplied URLs need an allowlist and an address check, and block the metadata endpoint at the network too.

#### Example 2: An open redirect after login

`/login?returnUrl=https://evil.example` sends users to a phishing page after a real sign-in.

*Only local redirects*

```csharp
static bool IsLocal(string? url) =>
    !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

app.MapPost("/login", async (LoginForm f, HttpContext http) =>
{
    // ... sign in ...
    return Results.LocalRedirect(IsLocal(f.ReturnUrl) ? f.ReturnUrl! : "/");   // "//evil.com" and "https://..." rejected
});
```

> **Lesson:** Redirect targets are input. Allow only local paths or an explicit list.

### API Security

<p class="src">From <em>.NET Backend Deep Dive</em> · API Security</p>

<a id="dn-106"></a>

#### Mass assignment / over-posting

```csharp
// The client posts {"total": 10, "isAdmin": true, "status": "Paid"}
[HttpPost] public async Task Create(Order order)   // binding straight to an entity
```

Bind to an explicit input DTO containing only fields the client may set, then map. This is one of the strongest arguments against returning or accepting EF entities at the API boundary.

<a id="dn-107"></a>

#### Rate limiting (.NET 7+, built in)

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Per-user rather than global, so one tenant can't starve the rest
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetTokenBucketLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress!.ToString(),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 200, TokensPerPeriod = 50,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10)
            }));

    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "60";   // tell clients when to return
        await ctx.HttpContext.Response.WriteAsync("Rate limit exceeded", ct);
    };
});
```

Note this is **per-instance** unless backed by a distributed store — with five pods your effective limit is five times what you configured.

<a id="dn-108"></a>

#### The rest of the checklist

| Risk | Control |
|---|---|
| SQL injection | Parameterized queries / EF. Never string-concatenate, including into `FromSqlRaw` |
| Secrets in logs | Redaction at the app *and* collector (Part 1.4) |
| CORS wide open | Never `AllowAnyOrigin()` together with `AllowCredentials()` — the browser blocks it anyway, and it signals the config was never thought through |
| Unbounded input | Cap request body size, array lengths, and JSON nesting depth |
| Timing attacks on tokens | `CryptographicOperations.FixedTimeEquals` |
| Password storage | Never hand-rolled. Argon2id or PBKDF2 with a high iteration count via ASP.NET Identity |
| Enumeration | Identical response and timing for "user not found" and "wrong password" |
| SSRF | Never fetch a user-supplied URL without an allowlist |
| Dependency vulnerabilities | `dotnet list package --vulnerable --include-transitive` in CI |

##### Interview answer
> "The controls that matter most are resource-based authorization to prevent IDOR, binding to input DTOs rather than entities to prevent over-posting, short-lived tokens with refresh rotation since JWTs can't be revoked, and validating audience and issuer properly. Rate limiting is per-instance unless it's distributed. And logs and error responses must never carry secrets or stack traces — errors go out as ProblemDetails with a trace ID."

---

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a188"></a>

#### The vulnerabilities that actually hit .NET apps

| Risk                      | In .NET terms                                  | Defence                                                                                |
|---------------------------|------------------------------------------------|----------------------------------------------------------------------------------------|
| Broken access control     | Missing `[Authorize]`, IDOR on `/orders/{id}`  | Deny by default; resource-based checks; tenant filters                                 |
| Injection                 | String-built SQL, LDAP or shell commands       | Parameters everywhere; never concatenate input                                         |
| Insecure deserialization  | `BinaryFormatter`, `TypeNameHandling.All`      | System.Text.Json with concrete types; never deserialize untrusted polymorphic payloads |
| SSRF                      | Fetching a URL supplied by the user            | Allow-list hosts; block link-local `169.254.169.254`                                   |
| Mass assignment           | Binding straight to an entity                  | Bind to a DTO with only the fields you accept                                          |
| Vulnerable dependencies   | Old transitive NuGet packages                  | `dotnet list package --vulnerable --include-transitive` in CI                          |
| Security misconfiguration | Detailed errors in prod, CORS `AllowAnyOrigin` | Environment-aware config; explicit origin lists                                        |

``` csharp
// rate limiting is now built in - use it on auth and expensive endpoints
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("login", l =>
    {
        l.PermitLimit = 5; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0;
    });
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
app.MapPost("/login", Login).RequireRateLimiting("login");
```

> **\[Note\]**
>
> **Security headers.** `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, and HSTS. Cheap to add, and they close whole classes of attack.

###### The five that recur, with the .NET-specific form of each

| Vulnerability                         | .NET-specific form                                                                 | Defence                                                                                        |
|---------------------------------------|------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------|
| **Insecure deserialisation**          | `BinaryFormatter`, `NetDataContractSerializer`, `TypeNameHandling.All` in Json.NET | Never deserialise untrusted data with type information. `BinaryFormatter` is removed in .NET 9 |
| **Broken object-level authorisation** | `GET /orders/{id}` with no ownership check                                         | Resource-based authorisation + a query filter + RLS                                            |
| **Mass assignment**                   | Binding directly to an EF entity                                                   | Bind to a DTO — an allow-list by construction                                                  |
| **SSRF**                              | `HttpClient` fetching a user-supplied URL (webhooks, image import, PDF render)     | Allow-list; resolve and reject private ranges; re-check after redirects                        |
| **Secrets in source or logs**         | A connection string in `appsettings.json`, committed                               | Secret scanning over *history*; workload identity instead of secrets                           |

``` csharp
// insecure deserialisation - the one that gives remote code execution
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.All      // <- REMOTE CODE EXECUTION
};
JsonConvert.DeserializeObject<object>(untrustedInput, settings);
// the payload names a TYPE, and the deserialiser constructs it. There are
// published gadget chains that reach Process.Start from here.

// System.Text.Json does not have this feature, which is a large part of
// why it is the default. Polymorphism is opt-in and CLOSED:
[JsonPolymorphic]
[JsonDerivedType(typeof(CardPayment), "card")]     // an explicit allow-list
public abstract record Payment;
```

``` csharp
// SSRF defence - the check people write and the check they need
static async Task<bool> IsSafeAsync(Uri uri, CancellationToken ct)
{
    if (uri.Scheme != Uri.UriSchemeHttps) return false;

    // resolve, and check EVERY address it resolves to
    var addrs = await Dns.GetHostAddressesAsync(uri.Host, ct);
    foreach (var ip in addrs)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        if (b[0] == 10) return false;                              // 10/8
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false; // 172.16/12
        if (b[0] == 192 && b[1] == 168) return false;              // 192.168/16
        if (b[0] == 169 && b[1] == 254) return false;              // LINK-LOCAL:
                                                                   // cloud metadata
    }
    return true;
}
// and disable automatic redirects, or a permitted URL can 302 you to
// 169.254.169.254 and the check is bypassed entirely:
new SocketsHttpHandler { AllowAutoRedirect = false }
```

> **\[Trap\]**
>
> **Common trap.** Secret scanning must run over *history*, not the working tree. A connection string committed and then deleted is still in every clone of the repository, forever. Deleting the line does nothing — the credential must be **revoked and rotated**. Run `gitleaks detect` or GitHub secret scanning across the full history once, deal with what it finds, then enable push protection so it cannot happen again.

### Security in Depth

<p class="src">From <em>Backend Engineering</em> · APIs · Security in Depth</p>

<p class="pd">Authentication and authorization were Part IV. These are the rest.</p>

<a id="be-e537"></a>

#### The vulnerabilities that actually appear

**Broken object-level authorization (BOLA/IDOR)** — OWASP API \#1, covered in IV.3. Check ownership per resource, every time.

**Broken function-level authorization** — the endpoint exists and isn't role-checked. `DELETE /admin/users/42` returning success for a normal user. Test with a low-privilege token against every admin route.

**Mass assignment** — binding request JSON straight onto a domain entity:

``` csharp
// DANGEROUS
[HttpPatch("users/{id}")]
public async Task<IActionResult> Update(int id, User user) { ... }
```

A client sends `{"name":"Nam","isAdmin":true}` and you just granted admin. **Always bind to an explicit DTO** containing only the fields a client may set. This is the single strongest argument for DTOs over entity binding, and it's why ServiceStack's request-DTO-first model is structurally safer here.

**Injection** — SQL, NoSQL, command, LDAP. Parameterized queries always. The subtle one in APIs: a `sort` or `filter` parameter interpolated into a query. Allow-list column names; never pass user input into an ORDER BY.

**SSRF** — your server fetches a URL the user supplied. Attacker points it at `169.254.169.254` and reads cloud credentials. Allow-list destinations, block private ranges, resolve DNS first and validate the resolved IP, and disable redirects.

**Excessive data exposure** — returning the full entity and letting the client filter. The client sees `passwordHash` and `internalNotes` in the JSON even if the UI hides them. Shape responses server-side, with DTOs.

**Unrestricted resource consumption** — no pagination limit, no upload size cap, no query complexity limit. `?limit=1000000` shouldn't be possible. GraphQL needs explicit depth and complexity limits for the same reason.

**XXE** — XML parsers resolving external entities can read local files. Disable DTD processing if you accept XML at all.

**Zip bombs and decompression** — a 1 MB gzip body expanding to 10 GB. Cap the decompressed size, not just the request size.

<a id="be-e538"></a>

#### Baseline hardening

``` yaml
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
Content-Security-Policy: default-src 'none'    (for API responses)
Cache-Control: no-store                        (on anything with PII)
```

Plus:

- Validate `Content-Type` on writes; reject unexpected types (`415`)
- Cap request body size at the server, not just in code
- Never reflect user input into an error message unencoded
- Log authentication failures, authorization denials, and rate-limit hits — these are your attack signal
- **Never log the `Authorization` header, tokens, or request bodies containing PII.** Redact at the logging layer, not by remembering.

### Security & Governance

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Security & Governance</p>

<p class="pd">Who may see what, proved rather than assumed. Privileges, encryption, row-level security, injection, and the auditing and retention obligations that come with holding other people&#x27;s data.</p>

<a id="be-d471"></a>

#### SQL injection, from the database side

Injection is usually discussed as an application concern. It is worth understanding what the database does, because that explains why parameterisation works and why escaping does not.

``` text
  STRING CONCATENATION - the SQL text itself changes

    "SELECT * FROM users WHERE name = '" + input + "'"
    input:  ' OR 1=1 --
    result: SELECT * FROM users WHERE name = '' OR 1=1 --'
            the parser sees a DIFFERENT QUERY. There is no defence downstream.

  PARAMETERISED - the SQL text is fixed before any value is seen

    prepare:  SELECT * FROM users WHERE name = $1     <- parsed, planned
    execute:  $1 = "' OR 1=1 --"                      <- a VALUE, never parsed

    the parse tree is built before the parameter exists, so the value cannot
    become syntax. This is a structural guarantee, not a filtering heuristic.
```

``` text
-- the places people still get it wrong even when using parameters

-- 1. identifiers cannot be parameterised
'SELECT * FROM ' || table_name                 -- injectable
format('SELECT * FROM %I', table_name)         -- %I quotes an IDENTIFIER safely

-- 2. ORDER BY and LIMIT built from input
'ORDER BY ' || sort_column                     -- injectable
-- fix: map the input to an allow-list in code, never pass it through

-- 3. dynamic SQL inside a function
EXECUTE 'SELECT * FROM orders WHERE status = ''' || p_status || '''';   -- injectable
EXECUTE 'SELECT * FROM orders WHERE status = $1' USING p_status;        -- safe

-- 4. LIKE patterns: parameterised, but the user controls the wildcards
WHERE name LIKE :pattern       -- '%' as input scans everything: a denial of service
-- escape the wildcards, or constrain the pattern shape
```

- **Parameterise every value**, including ones you believe are safe — the next developer will copy the pattern.
- **Allow-list identifiers.** Table, column and sort directions come from a fixed map in code, never from input.
- **Use `format(%I, %L)` or `QUOTE_IDENT`** when dynamic SQL is genuinely necessary.
- **Set `SECURITY INVOKER`** on functions unless you specifically need the definer's rights — and if you do, set an explicit `search_path`.
- **Least privilege is the backstop.** An injected query executing as a role that cannot drop tables or read the audit log is an incident rather than a catastrophe.
- **Statement timeouts limit the damage** of a deliberately expensive injected query.
