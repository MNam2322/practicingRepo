---
id: "10-1"
number: "10.1"
title: "Thinking like an attacker"
part: 10
part_title: "Security"
words: 5078
minutes: 22
---

# 10.1 Thinking like an attacker

> **Quick look.** Security as a way of thinking: how could someone misuse this?
>
> - Security protects confidentiality, integrity and availability.
> - Most attackers are automated and opportunistic, so basic hygiene stops most of them.
> - Every input, endpoint, dependency, tool and person is attack surface; shrink it.
> - Threat model at design time: draw data flows and trust boundaries, walk through STRIDE, decide a defence for each threat.
> - Least privilege, defence in depth, fail secure, secure defaults; never rely on obscurity; assume breach.
> - Watch for account enumeration, unlimited guessing, missing ownership checks and verbose errors; build detection and patching into normal work.


### The big picture

Everything so far in this guide has been about making software work. Security is about making it keep working, and keep its secrets, when someone is trying to make it fail. That changes the question you ask of every feature. As well as "does it do what users need?", you ask "how could someone misuse this?" An attacker needs to find one weakness. A defender needs to have thought of them all, or at least made each one hard to exploit and easy to notice.

This chapter introduces that way of thinking. It covers what security protects, who attacks systems and why, and the idea of an attack surface. It shows how teams find weaknesses before attackers do, by threat modelling with a simple checklist called STRIDE, and the design principles that hold up across every kind of system. The example is an attacker's probe: a short script that asks the questions attackers really ask, run against a careless and a hardened version of the same small API. The rest of Part 10 covers the big areas one at a time: identity, OAuth, web vulnerabilities, and secrets and cryptography.

### Before you start

**Builds on:** 8.4 A Node and TypeScript backend, 8.5 Validation and errors at the boundary.

**Ideas explained later:**

- **Monitoring** (11.6): Watching a system for known problems, such as an error rate above a threshold.
- **Credential** (10.2): Something a user presents to prove who they are, such as a password or a key.
- **Multi-factor authentication** (10.2): Requiring two or more different kinds of credential. Often shortened to MFA.
- **Authentication** (10.2): Establishing who a caller is.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Broken access control** (10.4): Users being able to see or do things they shouldn't, because permission checks are missing or wrong.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What are the three properties of the CIA triad?**

> Confidentiality: only the right people can read data. Integrity: data and systems can't be changed without permission, and changes can be detected. Availability: the system works when legitimate users need it.

**2. What does STRIDE stand for?**

> Spoofing identity, tampering with data, repudiation (denying having done something), information disclosure, denial of service and elevation of privilege. It is a checklist of the kinds of thing that can go wrong at each part of a system.

**3. Why should a login form say "invalid username or password" rather than "wrong password"?**

> "Wrong password" confirms that the username exists. Attackers use that to build lists of real accounts, called user enumeration, and then target them with password guessing.

**4. What is the principle of least privilege?**

> Every user, service and process gets only the permissions it needs for its job, and no more. When something is compromised, the damage is limited to what it was allowed to do.

**5. What is defence in depth?**

> Layering several independent protections, so that one failing doesn't expose the system. For example, input validation, parameterized queries, a database account with limited rights, and monitoring, all guarding against the same attack.

**6. Why is hiding how a system works not enough to secure it?**

> Secrets about design leak, through decompiled code, error messages, former staff or guessing. A system should stay secure even when attackers know exactly how it works, with only its keys kept secret.

**7. What is credential stuffing?**

> Trying usernames and passwords leaked from other sites against yours, relying on people reusing passwords. It succeeds without any flaw in your code, which is why rate limits and multi-factor authentication matter.

### What security protects

Security is usually described by three properties, known as the **CIA triad**.

**Confidentiality** means only authorized people and systems can read data: a passenger's passport number, payment details or travel plans. **Integrity** means data and systems can't be changed without permission, and that unauthorized changes are detected: nobody can alter a fare, a crew roster or a flight plan unnoticed. **Availability** means the system works when legitimate users need it: check-in on the morning of a flight, not just most of the time.

Real incidents usually hit one of these hardest. A data breach attacks confidentiality. Ransomware, which encrypts an organization's systems and demands payment, attacks availability and often confidentiality too. A tampered payment or booking attacks integrity. Two further properties come up often: authenticity, knowing who you are dealing with, which Authentication and authorization (10.2) covers, and non-repudiation, being able to prove who did something, which comes from good audit logs.

### Who attacks, and why

It is tempting to imagine a skilled attacker studying your system in particular. Most attacks aren't like that. They are automated and opportunistic: bots scan the whole internet around the clock for known weaknesses, such as an unpatched server, a default password or an exposed admin page, and attack whatever they find. A new server is typically probed within minutes of appearing online.

Beyond the bots, attackers range from criminals after money, through fraud, stolen card data or ransom, to insiders misusing access, and to well-funded groups working for governments, who target airlines, airports and air traffic systems for espionage or disruption. The encouraging consequence is that basic hygiene defeats most attacks: patching, strong authentication, least privilege and careful handling of input. Sophisticated attackers are a reason to add depth, not a reason to skip the basics.

### The attack surface

The **attack surface** is every point where an attacker can try to get in or cause harm. That includes every API endpoint and every parameter of each one, login and password reset forms, file uploads and message queues. It includes admin tools, forgotten test endpoints and debugging features left switched on. It also includes the dependencies you pull in, from A Node and TypeScript backend (8.4), the build pipeline, backups, and the people who can be tricked into giving access.

Reducing the attack surface is one of the most effective security measures there is. Remove endpoints nobody uses, turn off features you don't need, keep admin interfaces off the public internet, drop unused dependencies, and don't collect data you have no use for: data you don't hold can't leak.

### Threat modelling

**Threat modelling** is a structured way to find weaknesses at design time, when they are cheapest to fix. Adam Shostack's well-known framing reduces it to four questions. What are we working on? What can go wrong? What are we going to do about it? Did we do a good enough job?

The first question is usually answered with a data flow diagram. It shows the parts of the system, the data moving between them, and the trust boundaries, from Validation and errors at the boundary (8.5), where data crosses from less trusted to more trusted parts. Most threats sit on those boundaries.

For the second question, **STRIDE**, developed at Microsoft, is a checklist of six kinds of threat to ask about at each element.

| Threat | Violates | Airline example | Typical defence |
|---|---|---|---|
| **S**poofing | Authenticity | Signing in as another passenger | Strong authentication, MFA |
| **T**ampering | Integrity | Changing the fare in a booking request | Server-side checks, signatures |
| **R**epudiation | Non-repudiation | An agent denies issuing a refund | Audit logs that can't be edited |
| **I**nformation disclosure | Confidentiality | Reading other passengers' bookings | Authorization on every request |
| **D**enial of service | Availability | Flooding seat holds so real customers can't book | Rate limits, quotas, expiry |
| **E**levation of privilege | Authorization | A passenger reaching the agent tools | Least privilege, deny by default |

The answers to the third question are defences, each one a change, a test or an accepted risk with a reason. Prioritize by how likely a threat is and how bad it would be. A threat model doesn't need to be a big document. A diagram and a table, revisited whenever a feature changes the data flows, catches most design flaws.

### Principles that hold up

A handful of principles apply to every system, whatever the technology.

**Least privilege**, often called the principle of least privilege, gives every user, service and process only the permissions it needs. The booking API's database account can read and write bookings, but can't drop tables. A reporting tool gets a read-only account. A compromised component can then only do what it was allowed to do.

**Defence in depth** layers independent protections, so one failure doesn't expose everything. An injection attack might be stopped by input validation, by parameterized queries, by the database account's limited rights, or noticed by monitoring. Any one of them might fail; all of them failing together is much less likely.

**Fail secure** means that when something goes wrong, the system refuses rather than allows. If the permission service can't be reached, the request is denied, not waved through. An exception in an authorization check must never fall through to success.

**Secure by default** means the safe option needs no action: new accounts have minimal rights, new endpoints require authentication unless explicitly opened, debug features are off in production.

Don't rely on **security through obscurity**. A design that is only safe while attackers don't know how it works isn't safe, because designs leak, through decompiled code, error messages, documentation or former staff. The nineteenth-century cryptographer Auguste Kerckhoffs put it this way: a system should be secure even if everything about it, except the key, is public.

Check every request. Every access to a resource needs its own authorization check, not just the first page of a flow; a hidden button or an unguessable URL isn't access control.

And **zero trust** replaces the old idea of a trusted internal network. Traditional security trusted anything inside the corporate network. Zero trust treats the network as hostile and verifies every request explicitly, from whoever and wherever it comes, which matches how cloud systems actually work. It goes with the habit of assuming a breach will happen, and designing so that one compromised part doesn't give away the rest.

### How attackers probe

Attackers start with reconnaissance: learning what the system is and how it responds. Several common findings come from small design choices.

**User enumeration** is discovering which usernames exist, from login or password-reset responses that differ: "no such user" versus "wrong password", or different response times. Enumerated accounts then face guessing.

A **brute-force attack** tries many passwords for one account. **Credential stuffing** tries username and password pairs leaked from other sites, at scale, relying on people reusing passwords. Neither needs a bug in your code. Rate limits, lockouts with care not to let attackers lock out real users, and multi-factor authentication are the defences.

Guessable identifiers invite requests for other people's records: booking 1, then booking 2. If the server checks only that the caller is signed in, not that the record is theirs, every booking can be read. Common web vulnerabilities (10.4) calls this broken access control, and it is the most common serious flaw in web applications.

Verbose error messages hand over stack traces, file paths, SQL and library versions. Version banners in headers say exactly which known vulnerabilities to try. Missing security headers leave browsers without the protections from Common web vulnerabilities (10.4).

**Business logic abuse** uses features exactly as built, in ways nobody intended: holding every seat on a flight without paying, applying one voucher many times, or triggering a refund repeatedly. No scanner finds these. Asking "what if someone did this a thousand times, or in a different order?" does.

### Seeing it for real

This script starts two versions of the same small API, one careless and one hardened. It then runs an attacker's probe against each, asking the questions from the previous section. It needs only Node; save it in a folder whose `package.json` contains `{ "type": "module" }`.

```typescript
// probe.ts: run with: node probe.ts   (two versions of one API, and an attacker's probe run against both)
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { randomUUID } from "node:crypto";

type Booking = { id: number; owner: string; passenger: string; passport: string };
const users = new Map([["ada", "correct horse battery staple"], ["grace", "hopper-1906"]]);
const bookings: Booking[] = [
  { id: 1, owner: "ada", passenger: "Ada Lovelace", passport: "P1234567" },
  { id: 2, owner: "grace", passenger: "Grace Hopper", passport: "P7654321" },
];

function start(hardened: boolean): Promise<string> {
  const sessions = new Map<string, string>();                 // token -> user
  const failures = new Map<string, number>();                 // user -> failed logins
  const server = createServer(async (req: IncomingMessage, res: ServerResponse) => {
    const send = (status: number, body: unknown) => {
      if (hardened) {                                          // tell browsers to be strict with this response
        res.setHeader("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
        res.setHeader("X-Content-Type-Options", "nosniff");
        res.setHeader("Referrer-Policy", "no-referrer");
      } else {
        res.setHeader("Server", "FlightApp/1.4.2 (debug build)");   // free reconnaissance for an attacker
      }
      res.writeHead(status, { "Content-Type": "application/json" }).end(JSON.stringify(body));
    };
    try {
      const url = new URL(req.url!, "http://localhost");
      if (req.method === "POST" && url.pathname === "/login") {
        let raw = ""; for await (const chunk of req) raw += chunk;
        const { user, password } = JSON.parse(raw) as { user: string; password: string };
        if (hardened && (failures.get(user) ?? 0) >= 5) return send(429, { title: "Too many attempts. Try again later." });
        const known = users.get(user);
        if (known === password) { const token = randomUUID(); sessions.set(token, user); failures.delete(user); return send(200, { token }); }
        failures.set(user, (failures.get(user) ?? 0) + 1);
        return send(401, { title: hardened ? "Invalid username or password" : known === undefined ? "No such user" : "Wrong password" });
      }
      const user = sessions.get(req.headers.authorization?.replace("Bearer ", "") ?? "");
      if (!user) return send(401, { title: "Sign in first" });
      const m = url.pathname.match(/^\/api\/bookings\/(.+)$/);
      if (m) {
        const id = Number(m[1]);
        if (!Number.isInteger(id)) throw new Error(`Invalid booking id '${m[1]}' at BookingRepository.find (/srv/flightapp/src/repo.ts:88)`);
        const booking = bookings.find(b => b.id === id && (!hardened || b.owner === user));   // the hardened version checks ownership
        return booking ? send(200, booking) : send(404, { title: "Not found" });
      }
      return send(404, { title: "Not found" });
    } catch (e) {
      return hardened
        ? send(500, { title: "An error occurred.", traceId: randomUUID().slice(0, 8) })     // details go to the server's log only
        : send(500, { error: String(e), stack: (e as Error).stack?.split("\n").slice(0, 2) });
    }
  });
  return new Promise(resolve => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${(server.address() as { port: number }).port}`)));
}

// ---- The attacker's probe: the same questions against both versions -----------------------------
async function probe(name: string, base: string) {
  const post = (body: unknown) => fetch(`${base}/login`, { method: "POST", body: JSON.stringify(body) });
  const findings: string[] = [];

  const unknownUser = await (await post({ user: "mallory", password: "x" })).json();
  const knownUser = await (await post({ user: "grace", password: "x" })).json();
  if (unknownUser.title !== knownUser.title) findings.push(`login messages reveal which usernames exist ("${unknownUser.title}" vs "${knownUser.title}")`);

  let status = 0, attempts = 0;
  for (; attempts < 20 && status !== 429; attempts++) status = (await post({ user: "grace", password: `guess${attempts}` })).status;
  if (status !== 429) findings.push("20 wrong passwords in a row were all accepted for checking: no brute-force limit");

  const { token } = await (await post({ user: "ada", password: "correct horse battery staple" })).json();
  const auth = { headers: { Authorization: `Bearer ${token}` } };
  const other = await fetch(`${base}/api/bookings/2`, auth);
  if (other.status === 200) findings.push(`signed in as ada, read booking 2: ${JSON.stringify(await other.json())}`);

  const broken = await (await fetch(`${base}/api/bookings/abc`, auth)).text();
  if (broken.includes("/srv/") || broken.includes("stack")) findings.push(`an error response leaks internals: ${broken.slice(0, 90)}...`);

  const headers = (await fetch(`${base}/api/bookings/1`, auth)).headers;
  if (headers.get("server")) findings.push(`the Server header advertises "${headers.get("server")}"`);
  if (!headers.get("content-security-policy")) findings.push("no Content-Security-Policy or nosniff headers");

  console.log(`\n${name}: ${findings.length} finding(s)`);
  for (const f of findings) console.log(`  - ${f}`);
}

await probe("careless version", await start(false));
await probe("hardened version", await start(true));
process.exit(0);
```

The probe's output, from a real run:

```text
careless version: 6 finding(s)
  - login messages reveal which usernames exist ("No such user" vs "Wrong password")
  - 20 wrong passwords in a row were all accepted for checking: no brute-force limit
  - signed in as ada, read booking 2: {"id":2,"owner":"grace","passenger":"Grace Hopper","passport":"P7654321"}
  - an error response leaks internals: {"error":"Error: Invalid booking id 'abc' at BookingRepository.find (/srv/flightapp/src/re...
  - the Server header advertises "FlightApp/1.4.2 (debug build)"
  - no Content-Security-Policy or nosniff headers

hardened version: 0 finding(s)
```

Every fix is small: one shared message for failed logins, a counter, an ownership condition in one query, a generic error with a trace id, and a few headers. Each finding in the careless version is also a real category of incident. The third one, reading another passenger's passport number, is a reportable data breach under privacy laws such as the GDPR.

### Security as part of the work

Security isn't a phase at the end; it is woven into how software is built.

Threat model new features when they change data flows or trust boundaries. Review code with security questions in mind: where does this input come from, who is allowed to call this, what happens if this fails. Automated tools help at every stage: static analysis of code for dangerous patterns, scanning of dependencies for known vulnerabilities, and scanning for secrets committed to repositories, all in the pipeline, as CI/CD pipelines (11.4) describes. A **penetration test** hires specialists to attack the system under agreed rules and report what they find. A bug bounty programme pays outside researchers who find and report vulnerabilities.

Detection matters as much as prevention, because some attacks will get through. Log security events: sign-ins, failures, permission denials and administrative actions. Alert on patterns such as a burst of failed logins, and keep logs where an attacker who breaks in can't delete them, as Observability (11.6) covers. Have an incident response plan, written before it is needed, that says who decides what when something goes wrong. And patch promptly: most breaches use vulnerabilities for which fixes already existed.

### Rules and standards

Much of this is also required. Card payments bring the PCI DSS standard. Passenger data falls under privacy laws such as the EU's GDPR. Aviation regulators increasingly require formal information security management; in Europe, EASA's Part-IS rules apply to many aviation organizations from 2026. The OWASP Application Security Verification Standard (ASVS) is a detailed, free checklist of security requirements for web applications, useful whether or not a regulation demands it.

### In short

Security protects confidentiality, integrity and availability against attackers who are mostly automated and opportunistic, so basic hygiene stops most of them. Every input, endpoint, dependency, tool and person is part of the attack surface; shrink it. Threat model at design time: draw the data flows and trust boundaries, walk through STRIDE, decide a defence for each threat and revisit as the system changes. Apply least privilege, defence in depth, fail secure and secure defaults; don't rely on obscurity; check every request; assume breach. Watch for enumeration, guessing, broken access checks, verbose errors and logic abuse, and build detection and patching into normal work.

### What goes wrong

**Messages that confirm accounts.** Login and reset pages that say "no such user" hand attackers a list of real accounts. Use one message for every failure.

**No limits on guessing.** Unlimited login attempts make brute force and credential stuffing easy. Rate limit, add delays, and offer or require multi-factor authentication.

**Checking sign-in but not ownership.** Any signed-in user reads any record by changing an id. Check that every record requested belongs to, or is shared with, the caller.

**Leaky errors and banners.** Stack traces and version headers give attackers a map. Return generic errors with trace ids, and remove version banners.

**Security only at the edge.** Internal services trust each other completely, so one foothold opens everything. Verify requests between services too, and give each one least privilege.

**Failing open.** An authorization service outage lets every request through. Deny when in doubt.

**No detection.** A breach goes unnoticed for months because nobody logs or reviews security events. Log, alert and keep logs out of attackers' reach.

**Late patching.** Known vulnerabilities in frameworks and libraries stay unpatched for months. Update dependencies on a routine, and quickly for critical fixes.

### Cost model

Most defences in this chapter cost almost nothing at run time: an ownership condition in a query, a counter per account, a few response headers, a generic error message. The costs are in time and attention: an hour or two of threat modelling per significant feature, security review in code review, and pipeline scanning that adds minutes to builds. Compare that with the cost of a breach: investigation, notification of every affected passenger, regulatory fines that under the GDPR can reach 4% of global turnover, and lost trust. Fixing a design flaw found in threat modelling costs a small fraction of fixing it after release.

### In other languages (optional)

**Every stack.** The principles and probes in this chapter are the same in C#, TypeScript, Python, Java and Go, because they are about design, not syntax. The frameworks differ in their safe defaults: ASP.NET Core, for example, doesn't send a detailed exception page outside development, and requires explicit configuration to open CORS.

**Tools.** Microsoft's Threat Modeling Tool and OWASP Threat Dragon draw data flow diagrams and suggest STRIDE threats. OWASP ZAP and Burp Suite probe running web applications, automating the kind of checks in the example.

### Key terms

- **CIA triad**: Confidentiality, integrity and availability: the three properties security protects.
- **Confidentiality**: Only authorized people and systems can read the data.
- **Integrity**: Data and systems can't be changed without permission, and changes can be detected.
- **Availability**: A system works when legitimate users need it.
- **Attack surface**: Every point where an attacker can try to get in or cause harm.
- **Threat modelling**: Finding and addressing what can go wrong with a system, in a structured way, usually at design time.
- **STRIDE**: A checklist of threats: spoofing, tampering, repudiation, information disclosure, denial of service and elevation of privilege.
- **Least privilege**: Giving every user, service and process only the permissions it needs.
- **Defence in depth**: Layering independent protections so that one failing doesn't expose the system.
- **Fail secure**: Refusing, rather than allowing, when something goes wrong.
- **Secure by default**: Making the safe choice the one that needs no action.
- **Security through obscurity**: Relying on attackers not knowing how a system works. Not enough on its own.
- **Zero trust**: Verifying every request explicitly, rather than trusting anything inside a network.
- **User enumeration**: Discovering which usernames exist from differences in a system's responses.
- **Brute-force attack**: Trying many possible passwords or keys until one works.
- **Credential stuffing**: Trying username and password pairs leaked from other sites.
- **Business logic abuse**: Misusing features exactly as built, in ways their designers didn't intend.
- **Penetration test**: An authorized attack on a system by specialists, to find weaknesses before real attackers do.

### Where this leads

Proving who a user is and deciding what they may do is in Authentication and authorization (10.2), and delegating sign-in to an identity provider in OAuth 2 and OpenID Connect (10.3). The main categories of web attack, including broken access control and injection, are in Common web vulnerabilities (10.4). Protecting data and keys is in Secrets and cryptography basics (10.5). Security scanning in pipelines is in CI/CD pipelines (11.4), and security logging in Observability (11.6).

### Check yourself

1. Add a password reset endpoint to both versions in `probe.ts` and a probe that detects whether it reveals which accounts exist. Fix the hardened version.
2. Measure the response time of failed logins for a known and an unknown user in the careless version. Decide whether timing alone could reveal accounts, and how to prevent it.
3. Draw a data flow diagram for online check-in: passenger, app, API, booking database, boarding pass service. Mark the trust boundaries.
4. Walk your check-in diagram through STRIDE and write one threat and one defence for each letter.
5. Run a free scanner such as OWASP ZAP against a test environment of an application you work on, and triage its findings into real risks and noise.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A login form that confirmed which emails exist

"No account with that email" versus "Wrong password" lets attackers build a list of customer emails.

*Same answer, same timing*

```csharp
var user = await users.FindByEmailAsync(email);
var ok = user is not null && hasher.VerifyHashedPassword(user, user.PasswordHash, password)
                                 != PasswordVerificationResult.Failed;
if (user is null) hasher.HashPassword(DummyUser, password);   // spend similar time either way
if (!ok) return Results.Problem("Email or password is incorrect.", statusCode: 401);
```

> **Lesson:** Messages and timing that differ by account existence leak information. Respond identically.

#### Example 2: A threat model in 30 minutes

Before launching a "manage booking" page reachable with booking code + surname, the team does a quick STRIDE pass.

*The outcome*

```text
Spoofing     guessing code+surname  -> rate limit per IP and per code, lockout after 10 tries
Tampering    changing another's seat -> server checks the booking belongs to the session
Repudiation  "I didn't cancel"       -> audit log with actor, time, IP
Info leak    passport numbers shown  -> show only last 4 digits
DoS          scripted lookups        -> rate limiting, CAPTCHA after failures
Elevation    agent-only actions      -> separate policy, never reachable from this page
```

> **Lesson:** Walking each threat category against a real flow finds the defences you need before attackers do.

### Platform Security

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Platform Security</p>

<p class="pd">Security as a property of the deployed system rather than the code: identity, secrets, patching and the posture that limits what a compromise can reach.</p>

<a id="be-g711"></a>

#### The threat model of a deployed system

``` text
  ask, in order:

  WHAT are you protecting?     customer data, money, availability,
                               your ability to operate at all

  FROM WHOM?                   opportunistic scanners (constant, automated)
                               targeted attackers (rare, patient)
                               insiders (accidental far more often than malicious)
                               your own mistakes (BY FAR the most frequent)

  ENTRY POINTS?                public endpoints, dependencies, CI/CD,
                               developer laptops, third-party integrations,
                               the cloud control plane

  BLAST RADIUS?                if THIS is compromised, what else follows?
                               -> this is the question that most changes designs
```

| Assumption to design against                | Consequence                                        |
|---------------------------------------------|----------------------------------------------------|
| The application *will* have a vulnerability | It must run with minimal privilege                 |
| A credential *will* leak                    | Short-lived credentials, or none at all            |
| A dependency *will* be compromised          | Pin, scan, restrict egress                         |
| An instance *will* be reachable             | Segment the network so reaching one reaches little |
| Someone *will* misconfigure something       | Deny by default; detect drift                      |
| Backups *will* be targeted                  | Immutable, in a separate account                   |

The most valuable question in this table is the last one in the diagram. A design where compromising the web tier grants database credentials, cloud API access and the ability to reach every other service is common, and it is the difference between an incident and a catastrophe.

###### Working out what an attacker actually gets, layer by layer

``` text
  ASSUME EACH LAYER IS COMPROMISED, and ask what follows. This is more
  useful than a checklist because it exposes the BLAST RADIUS.

  a stolen API TOKEN
      -> what can it do? Which scopes, which tenant, for how long?
      -> can it be revoked, or must it expire?
      -> short-lived tokens turn a permanent breach into a 15-minute one

  ONE COMPROMISED CONTAINER
      -> what can it REACH?          (egress policy, network policy)
      -> what CREDENTIALS does it hold? (env vars, mounted files)
      -> can it reach the METADATA endpoint and become the node?
      -> can it reach OTHER services directly, or only through a gateway?
      -> does it run as root? can it escape to the host?

  a compromised BUILD PIPELINE
      -> it can deploy to production and read every secret.
      -> this is usually the WEAKEST link and the HIGHEST value target.
      -> a PR from a fork must never see production credentials.

  a compromised DEVELOPER LAPTOP
      -> what does their standing access reach?
      -> is production access just-in-time, or permanent?

  the pattern: most real breaches are not a clever exploit of one
  service. They are ONE FOOTHOLD plus EXCESSIVE REACH - which is why
  egress control and least privilege matter more than any single
  hardening step.
```

| Control                                   | Reduces                               |
|-------------------------------------------|---------------------------------------|
| Short-lived credentials                   | The window a stolen token is useful   |
| Workload identity, no stored secrets      | What there is to steal                |
| Default-deny egress                       | Exfiltration and lateral movement     |
| Blocking the metadata IP                  | SSRF → node credentials               |
| Non-root containers, read-only filesystem | Escape and persistence                |
| Network policy between services           | Lateral movement                      |
| Just-in-time production access            | Standing human privilege              |
| OIDC federation from CI                   | Long-lived cloud keys in the pipeline |
| Separate accounts per environment         | A dev compromise reaching production  |

Two things follow from the pattern above. First, **the blast radius is the design variable** — you cannot make compromise impossible, so make it contained. Second, the controls that reduce blast radius are mostly *platform* controls, not application ones: a perfectly written service inside a network with unrestricted egress and node-level credentials is one SSRF bug away from full account compromise.
