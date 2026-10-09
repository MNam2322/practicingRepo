---
id: "10-2"
number: "10.2"
title: "Authentication and authorization"
part: 10
part_title: "Security"
words: 8241
minutes: 36
---

# 10.2 Authentication and authorization

> **Quick look.** Authentication proves who a caller is; authorization decides what they may do, on every request.
>
> - Store passwords with a slow salted hash; add MFA; prefer passkeys, which defeat phishing.
> - Session cookies must be HttpOnly, Secure and SameSite; rotate on sign-in, time out, end on the server.
> - In ASP.NET Core the caller is a ClaimsPrincipal; authentication middleware runs before authorization.
> - IDOR (missing ownership checks) is the most common serious API flaw: scope every query by user or tenant.
> - Use named policies plus resource-based checks; deny by default with a fallback policy.
> - Validate JWT audience and issuer; the default 5-minute ClockSkew extends token life.
> - Return 404 rather than 403 for resources the caller shouldn't know exist; enforce on the server, never only in the UI.


### The big picture

Almost every request to a backend raises two questions. Who is this? And are they allowed to do this? The first is authentication: proving identity, usually by signing in. The second is authorization: deciding what that identity may do, on every request, to every resource. They are different problems, solved by different code. Mixing them up, or checking only the first, is behind many of the worst breaches.

This chapter covers both. It starts with credentials: passwords and how to store them, multi-factor authentication, and passkeys, which make phishing useless. It then covers how a signed-in state is carried from request to request, with session cookies or tokens, and the cookie settings that protect them. Authorization comes next: roles, claims and policies, checks on individual resources, and why every endpoint should be closed by default. Throughout, it shows how ASP.NET Core puts these pieces together. The example is a complete single-file app that signs users in with a cookie, hashes passwords properly, and enforces ownership and role rules. It then calls itself as two different users to show the rules at work.

### Before you start

**Builds on:** 10.1 Thinking like an attacker, 6.6 HTTP, 8.3 A modern .NET backend, 7.5 Schema design and migrations, 6.5 TLS.

**Ideas explained later:**

- **Salt** (10.5): A random value stored with each password hash so identical passwords hash differently.
- **Cross-site request forgery** (10.4): Making a user's browser send an unwanted, authenticated request to a site from another site. Often shortened to CSRF.
- **Password hashing function** (10.5): A deliberately slow, salted hash designed for storing passwords, such as Argon2 or PBKDF2.
- **JWT** (10.3): JSON Web Token: signed, base64url-encoded claims in three dot-separated parts.
- **OpenID Connect** (10.3): A layer on OAuth 2.0 that adds sign-in: ID tokens, standard user claims and discovery.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Region** (11.5): A geographic area where a provider has its own data centres.

**The code uses:** variables and var, methods and local functions, string interpolation, records, collection expressions, lambdas, null operators, switch expressions and patterns, tuples, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between 401 and 403?**

> 401 Unauthorized, despite its name, means "not authenticated": the server doesn't know who you are, so sign in. 403 Forbidden means the server knows who you are, and you aren't allowed to do this.

**2. How should passwords be stored?**

> As the output of a slow, salted password hashing algorithm, such as PBKDF2, bcrypt, scrypt or Argon2, with a unique random salt per user. Never in plain text, never encrypted, and never with a fast hash such as SHA-256 alone.

**3. Why are passkeys resistant to phishing?**

> A passkey is a key pair tied to the real site's domain. The browser only uses it for that domain, and the private key never leaves the device, so a fake site gets nothing it can use.

**4. What do the HttpOnly, Secure and SameSite cookie attributes do?**

> HttpOnly hides the cookie from JavaScript, so injected scripts can't steal it. Secure sends it only over HTTPS. SameSite limits sending it with requests that start on other sites, which blocks most cross-site request forgery.

**5. What is an insecure direct object reference?**

> An endpoint that fetches a record by the id in the request without checking that the caller may see that record. Changing the id reveals other users' data.

**6. What does a fallback authorization policy do in ASP.NET Core?**

> It applies to every endpoint that has no authorization settings of its own. Setting it to "require an authenticated user" makes the app closed by default: new endpoints need sign-in unless explicitly marked anonymous.

**7. What is the difference between role-based and resource-based authorization?**

> Role-based checks what kind of user the caller is, such as "agent". Resource-based checks the caller against the specific record, such as "is this booking theirs?". Most real systems need both.

### Two different questions

**Authentication** establishes who a caller is. **Authorization** decides whether that caller may perform a particular action on a particular resource. A passenger signing in with a password is authentication. The check that stops her reading someone else's booking, or reaching the refund screen for agents, is authorization.

HTTP's status codes mirror the distinction, with a confusing name. 401 Unauthorized means the caller isn't authenticated: sign in and try again. 403 Forbidden means the caller is authenticated and still not allowed. Some APIs answer 404 instead of 403 for records that belong to others, so the response doesn't even confirm the record exists.

### Credentials

A **credential** is something a user presents to prove who they are. Credentials come in three kinds, called **authentication factors**: something you know, such as a password; something you have, such as a phone or security key; and something you are, such as a fingerprint.

Passwords are still the most common, and the weakest. Current NIST guidance, the US standard most others follow, favours length over complexity rules: allow long passphrases, don't force periodic changes, and reject passwords known from breaches. Most importantly, how the server stores them decides what an attacker gets if the database leaks. Passwords must be stored with a slow, salted password hashing function, such as PBKDF2, bcrypt, scrypt or Argon2, never in plain text and never merely encrypted. Secrets and cryptography basics (10.5) explains why those algorithms are slow on purpose, and what the salt is for. In .NET, ASP.NET Core Identity's password hasher does this correctly, and the example uses the built-in PBKDF2 function directly.

**Multi-factor authentication** (MFA) requires two or more different factors, so a stolen password alone isn't enough. The second factor is often a **one-time password**: a six-digit code from an authenticator app, which computes it from a shared secret and the current time. Less safely, it comes in an SMS message, which can be intercepted or redirected by taking over a phone number.

**Phishing** tricks people into entering credentials into a fake site that looks real. Passwords and one-time codes both fall to it: the victim types them into the fake site, and the attacker replays them to the real one straight away. A **passkey**, built on the WebAuthn and FIDO2 standards, fixes that. It is a key pair created by the user's device for one website. The private key never leaves the device, often unlocked with a fingerprint or face, and the browser will only use it for the domain it was created for. A convincing fake site on another domain simply gets nothing. Passkeys are supported by all major browsers and operating systems, and are the strongest option for users today.

Account recovery is often the weakest link: a password reset that anyone can trigger with an email address and a guessable security question undoes everything else. Reset links should be single-use, short-lived, sent only to verified addresses, and should not reveal whether an account exists, for the enumeration reason in Thinking like an attacker (10.1).

### Staying signed in

HTTP is stateless, as HTTP (6.6) described, so after a successful sign-in, every later request must prove again who it comes from. There are two main ways.

A **session** keeps the signed-in state on the server side, or in a protected form the server issues, and gives the browser a **session cookie** that identifies it. ASP.NET Core's cookie authentication stores the user's identity in the cookie itself, encrypted and signed with the Data Protection keys so the browser can't read or change it. A server-side session store, with only a random id in the cookie, is the other common design.

A **bearer token** is a value sent in the `Authorization: Bearer ...` header. Whoever holds it is treated as its owner, like a physical ticket. Tokens suit APIs called by other services and mobile apps. The most common format is the JSON Web Token (JWT), a signed, readable bundle of facts about the user, covered with the rest of token-based sign-in in OAuth 2 and OpenID Connect (10.3).

Session cookies need their protective attributes, from HTTP (6.6). **HttpOnly** keeps scripts from reading the cookie, so a cross-site scripting flaw can't steal it. **Secure** sends it only over HTTPS. **SameSite** controls whether it is sent on requests that start from other sites: `Lax` blocks it on cross-site form posts and background requests, and `Strict` blocks it on all cross-site navigation. That makes most cross-site request forgery impossible, as Common web vulnerabilities (10.4) explains.

Sessions also need lifetimes: an idle timeout, after which inactivity signs the user out, and an absolute limit, after which they must sign in again. Issue a new session identifier when a user signs in, never reusing one from before. Reusing one allows **session fixation**, where an attacker plants a known session id in a victim's browser and waits for them to sign in with it. Signing out must end the session on the server, not just delete the cookie in one browser.

### Who the caller is, in code

In .NET, the authenticated caller is a `ClaimsPrincipal`. Its identity holds a list of **claims**: statements about the user, each a type and a value, such as name `ada`, role `passenger`, email address or tenant id. Claims come from wherever authentication happened: the cookie, a validated token, or a Windows login. Authorization code then reads them.

ASP.NET Core separates the two jobs into middleware, in the order from A modern .NET backend (8.3). `UseAuthentication` runs the configured authentication scheme, such as cookies or JWT bearer tokens, and sets `HttpContext.User`. `UseAuthorization` checks the endpoint's requirements against that user. An endpoint's requirements come from `RequireAuthorization` in minimal APIs or `[Authorize]` on controllers, and `AllowAnonymous` explicitly opens an endpoint.

### Deciding what callers may do

Several models of authorization are common, and most systems combine them.

**Role-based access control** (RBAC) groups permissions into roles, such as passenger, agent and administrator, and assigns roles to users. It is simple and easy to audit, and it becomes unwieldy when every exception needs a new role.

Attribute-based access control decides from attributes of the user, the resource and the situation: an agent may issue refunds up to 500 euros, for bookings in their own region, during their shift. In .NET these rules are usually written as **policies**: named requirements, registered at start-up and applied to endpoints, such as `RequireRole("agent")`, `RequireClaim("region", "EU")`, or custom requirements written in code.

**Resource-based authorization** checks the caller against the specific record: is this booking theirs, or shared with them, or in their tenant? It can't be decided from the endpoint alone, because it depends on data, so it happens in the handler or query. The simplest form, used in the example, puts the condition into the query itself: fetch the booking where the id matches and the owner is the caller. ASP.NET Core's `IAuthorizationService.AuthorizeAsync(user, resource, policy)` runs policies against a loaded resource when the rules are more complex. In multi-tenant systems, from Schema design and migrations (7.5), the same idea means every query is scoped to the caller's tenant.

Missing that check is the **insecure direct object reference**: an endpoint that trusts the id in the request and returns whatever it points at. The probe in Thinking like an attacker (10.1) read another passenger's passport number that way. It is the most common form of the broken access control that tops lists of web vulnerabilities.

Two rules make all of this robust. Deny by default: in ASP.NET Core, a fallback policy that requires an authenticated user applies to every endpoint without its own settings, so a forgotten attribute means "closed", not "open". This is the secure-by-default principle from Thinking like an attacker (10.1). And check on the server, on every request. Hiding a button in the frontend is a convenience, not a control.

### Seeing it for real

This app uses cookie authentication, stores passwords with salted PBKDF2, and makes every endpoint require sign-in unless marked otherwise. It has an ownership check on bookings and an agents-only policy for refunds. It then acts as two clients, each keeping its cookie the way a browser would.

```csharp
#:sdk Microsoft.NET.Sdk.Web
// authz.cs — run with: dotnet run authz.cs
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5060");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;                                   // scripts can't read it
        options.Cookie.SameSite = SameSiteMode.Strict;                    // not sent on requests started by other sites
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // use Always in production, behind HTTPS
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };        // an API answers,
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }; // not redirects
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("agents", policy => policy.RequireRole("agent"))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());   // closed by default

var users = new Dictionary<string, (byte[] Salt, byte[] Hash, string Role)>
{
    ["ada"] = Hashed("correct horse battery staple", "passenger"),
    ["linus"] = Hashed("agent-password-2026", "agent"),
};
var unknownUser = Hashed("not a real account", "none");                  // checked for unknown names too, so timing reveals nothing
Booking[] bookings = [new(1, "ada", "BA117"), new(2, "grace", "QF1")];

var app = builder.Build();
app.UseAuthentication();                                                  // who is calling?
app.UseAuthorization();                                                   // may they do this?

app.MapPost("/login", async (LoginRequest login, HttpContext http) =>
{
    bool known = users.TryGetValue(login.User, out var account);
    var (salt, hash, role) = known ? account : unknownUser;
    bool valid = Verify(login.Password, salt, hash);                      // always do the slow check
    if (!known || !valid) return Results.Problem(title: "Invalid username or password", statusCode: 401);
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, login.User), new Claim(ClaimTypes.Role, role)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));   // issues the cookie
    return Results.NoContent();
}).AllowAnonymous();

app.MapGet("/me", (ClaimsPrincipal user) => new { name = user.Identity!.Name, role = user.FindFirst(ClaimTypes.Role)?.Value });

app.MapGet("/bookings/{id:int}", (int id, ClaimsPrincipal user) =>
    bookings.FirstOrDefault(b => b.Id == id && (b.Owner == user.Identity!.Name || user.IsInRole("agent"))) is { } booking
        ? Results.Ok(booking)
        : Results.NotFound());                                            // not yours looks exactly like not there

app.MapPost("/bookings/{id:int}/refund", (int id) => Results.Ok(new { refunded = id })).RequireAuthorization("agents");

await app.StartAsync();

foreach (var (who, password) in new[] { ("ada", "correct horse battery staple"), ("linus", "agent-password-2026") })
{
    using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };   // keeps cookies, like a browser
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5060") };
    Console.WriteLine($"-- {who}");
    Console.WriteLine($"   /me before signing in: {(int)(await http.GetAsync("/me")).StatusCode}");
    using var login = await http.PostAsJsonAsync("/login", new { user = who, password });
    var cookie = handler.CookieContainer.GetCookies(http.BaseAddress)[".AspNetCore.Cookies"];
    Console.WriteLine($"   sign in: {(int)login.StatusCode}, cookie HttpOnly: {cookie?.HttpOnly}");
    Console.WriteLine($"   /me: {await http.GetStringAsync("/me")}");
    foreach (int id in new[] { 1, 2 })
        Console.WriteLine($"   booking {id}: {(int)(await http.GetAsync($"/bookings/{id}")).StatusCode}");
    Console.WriteLine($"   refund booking 1: {(int)(await http.PostAsync("/bookings/1/refund", null)).StatusCode}");
}
// -- ada
//    /me before signing in: 401
//    sign in: 204, cookie HttpOnly: True
//    /me: {"name":"ada","role":"passenger"}
//    booking 1: 200
//    booking 2: 404            <- someone else's booking: not found, not forbidden
//    refund booking 1: 403     <- signed in, but not an agent
// -- linus
//    ...
//    booking 2: 200            <- agents may see every booking
//    refund booking 1: 200

using (var anonymous = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5060") })
{
    foreach (var (user, password) in new[] { ("ada", "wrong"), ("mallory", "wrong") })
    {
        using var failed = await anonymous.PostAsJsonAsync("/login", new { user, password });
        Console.WriteLine($"{user}: {(int)failed.StatusCode} {await failed.Content.ReadAsStringAsync()}");
    }
}
// ada: 401 {"type":"...","title":"Invalid username or password","status":401}
// mallory: 401 {"type":"...","title":"Invalid username or password","status":401}   <- the same answer
await app.StopAsync();

static (byte[] Salt, byte[] Hash, string Role) Hashed(string password, string role)
{
    byte[] salt = RandomNumberGenerator.GetBytes(16);                     // unique per user
    return (salt, Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32), role);
}

static bool Verify(string password, byte[] salt, byte[] expected) =>
    CryptographicOperations.FixedTimeEquals(                              // compares in constant time, as 10.5 explains
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32), expected);

record LoginRequest(string User, string Password);
record Booking(int Id, string Owner, string Flight);
```

Notice four details. The ownership rule is part of the lookup, so there is no path that forgets it. An unknown username still goes through the slow password check, so response times don't reveal which accounts exist. `SignInAsync` issues a new cookie on every sign-in, which prevents session fixation. And the fallback policy means `/me`, the bookings endpoint and any endpoint added later need sign-in without anyone remembering to ask for it.

In a real app, ASP.NET Core Identity provides the user store, password hashing, lockout after repeated failures, two-factor codes and passkeys. It saves you from writing any of that yourself.

### Services calling services

Not every caller is a person. When one service calls another, it needs an identity too.

An **API key** is a long random secret sent with each request. It is simple, and weak: it identifies an application rather than a user, is often long-lived and widely copied, and grants everything the key can do. If you use API keys, make them long and random, store only their hashes, scope each to the least it needs, and rotate them. Mutual TLS, from TLS (6.5), authenticates both ends of a connection with certificates. The OAuth client credentials flow, in OAuth 2 and OpenID Connect (10.3), issues short-lived tokens to services. In the cloud, managed identities remove the secret entirely, as Secrets and cryptography basics (10.5) explains.

### Recording what happened

Authentication and authorization events belong in the audit log: sign-ins and failures, password and MFA changes, permission denials, and every administrative action, with the user, time and source address. They are how attacks are noticed, as Thinking like an attacker (10.1) described. They also give non-repudiation, when an agent later disputes having issued a refund. Never log passwords, tokens or session cookies, even in failed attempts.

### In short

Authentication proves who a caller is; authorization decides what they may do, on every request. Store passwords with a slow, salted password hash, favour long passphrases, add multi-factor authentication, and prefer passkeys, which defeat phishing. Keep users signed in with session cookies marked HttpOnly, Secure and SameSite, or with bearer tokens for APIs. Rotate the session on sign-in, time sessions out, and end them on the server. In ASP.NET Core, the caller is a ClaimsPrincipal; authentication and authorization middleware run in that order. Combine roles and policies with resource-based checks built into queries, deny by default with a fallback policy, and audit everything that matters.

### What goes wrong

**Checking sign-in, not ownership.** Any user reads any record by changing the id. Build the ownership or tenant condition into every query.

**Open by default.** A new endpoint ships without an authorization attribute and is public. Set a fallback policy that requires authentication.

**Weak password storage.** Passwords hashed with plain SHA-256, or no salt, are cracked by the million after a leak. Use a slow, salted password hash.

**Authorization only in the frontend.** Hidden buttons don't stop direct API calls. Check every rule on the server.

**Cookies without their attributes.** A session cookie readable by scripts, or sent over plain HTTP or with cross-site requests, is stolen or misused. Set HttpOnly, Secure and SameSite.

**Sessions that never end.** Signing out only deletes the cookie in one browser, and a stolen copy still works for days. End sessions on the server, and set idle and absolute timeouts.

**SMS as the only second factor.** Phone numbers can be hijacked. Offer authenticator apps and passkeys.

**Long-lived shared API keys.** One leaked key opens everything for years. Scope, rotate and hash keys, or use managed identities.

### Cost model

A slow password hash is slow on purpose: 600,000 rounds of PBKDF2 take a few hundred milliseconds of CPU per sign-in, which is invisible to one user and ruinous to an attacker guessing billions of passwords. It also means sign-in endpoints need rate limits, or they become an easy way to exhaust a server's CPU. Validating a signed cookie or token on each request costs microseconds. Authorization checks cost a condition in a query or a policy evaluation, far less than the data access they guard. Server-side sessions add a lookup per request, typically in a cache.

### In other languages (optional)

**Node.js.** Passport.js and framework-specific libraries handle sign-in strategies; sessions use express-session or signed cookies. Password hashing uses the `bcrypt` or `argon2` packages, or `crypto.scrypt` built in.

**Python.** Django has a full user model with PBKDF2 hashing by default and permissions; FastAPI uses dependencies for authentication and authorization.

**Java.** Spring Security provides authentication, sessions, method-level authorization with annotations and password encoders.

**Hosted identity.** Many teams avoid writing sign-in at all and use an identity provider, such as Microsoft Entra ID, Auth0 or Keycloak, through OpenID Connect, as the next chapter describes.

### Key terms

- **Authentication**: Establishing who a caller is.
- **Authorization**: Deciding whether a caller may perform an action on a resource.
- **Credential**: Something a user presents to prove who they are, such as a password or a key.
- **Authentication factor**: A kind of credential: something you know, something you have or something you are.
- **Multi-factor authentication**: Requiring two or more different kinds of credential. Often shortened to MFA.
- **One-time password**: A short code valid once or for a short time, often from an authenticator app.
- **Phishing**: Tricking people into giving credentials to a fake site or message that looks real.
- **Passkey**: A phishing-resistant credential: a key pair created on the user's device for one website.
- **Session**: The signed-in state that lets later requests be recognized as coming from the same user.
- **Session cookie**: A cookie that identifies a signed-in session.
- **Bearer token**: A token sent in the Authorization header; whoever holds it is treated as its owner.
- **Session fixation**: An attack that plants a known session id in a victim's browser before they sign in.
- **Claim**: A statement about a user, such as their name, role or tenant, carried with their identity.
- **Role-based access control**: Granting permissions through roles assigned to users. Often shortened to RBAC.
- **Policy**: In ASP.NET Core, a named set of requirements a caller must meet to use an endpoint.
- **Resource-based authorization**: Checking the caller against the specific record being accessed.
- **Insecure direct object reference**: Returning or changing a record by the id in a request without checking the caller may access it.
- **API key**: A long secret value that identifies an application calling an API.

### Where this leads

Signing in through an identity provider, and the tokens that come back, are in OAuth 2 and OpenID Connect (10.3). Cross-site request forgery, and the other attacks that cookies and scripts invite, are in Common web vulnerabilities (10.4). How password hashing and constant-time comparison work, and where keys and secrets belong, is in Secrets and cryptography basics (10.5).

### Check yourself

1. Add a `/logout` endpoint to `authz.cs` that calls `SignOutAsync`, and check that `/me` returns 401 afterwards for that client.
2. Add a lockout: after five failed sign-ins for one username, refuse for five minutes with 429, and make sure the response for an unknown username looks the same.
3. Add a `region` claim to agents and a policy that allows refunds only for bookings in the agent's region.
4. Time 20 failed sign-ins for a known and an unknown username with the `unknownUser` check removed, then with it restored, and compare.
5. In an application you know, find three endpoints that return a record by id, and check how each one decides whether the caller may see it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Any logged-in user could read any booking

`GET /bookings/{id}` checks that the caller is signed in, but not that the booking is theirs (IDOR).

*Scope the query to the caller*

```csharp
app.MapGet("/bookings/{id:guid}", async (Guid id, ClaimsPrincipal user, BookingDb db, CancellationToken ct) =>
{
    var customerId = user.GetCustomerId();
    var booking = await db.Bookings
        .Where(b => b.Id == id && b.CustomerId == customerId)   // ownership is part of the query
        .Select(b => b.ToDto())
        .SingleOrDefaultAsync(ct);
    return booking is null ? Results.NotFound() : Results.Ok(booking);   // 404, not 403: don't confirm it exists
}).RequireAuthorization();
```

> **Lesson:** Authentication says who; authorization must check this caller may touch this resource, on every request.

#### Example 2: Deny by default

A new admin endpoint shipped without `[Authorize]` and was public for a week.

*A fallback policy*

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())   // everything needs sign-in
    .AddPolicy("Agents", p => p.RequireRole("agent"));

app.MapGet("/health", () => "ok").AllowAnonymous();   // public endpoints are now explicit
```

> **Lesson:** Make secure the default. Endpoints must opt out of authentication, not opt in.

### API Security

<p class="src">From <em>.NET Backend Deep Dive</em> · API Security</p>

<a id="dn-104"></a>

#### JWT validation — the pitfalls that matter

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "https://idp.example.com";   // fetches JWKS, rotates keys automatically
        o.Audience  = "order-api";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)   // default is FIVE MINUTES
        };
    });
```

What goes wrong in practice:

- **`ValidateAudience = false`** — now a token minted for a *different* service in the same tenant is accepted by yours. A real privilege-escalation path.
- **The default 5-minute `ClockSkew`** means a "revoked" 5-minute token is honored for up to 10 minutes.
- **`alg: none` and algorithm confusion** — never hand-roll validation; the library rejects these, your custom code probably won't.
- **JWTs cannot be revoked.** Short access-token TTL plus refresh-token revocation is the only workable answer (see Part 1.2).
- **Don't put PII in a JWT.** It's base64, not encrypted — anyone holding it can read every claim.

**Refresh token rotation:** issue a new refresh token on each use and invalidate the old one. If an already-used token is presented again, that means theft — revoke the entire token family and force re-authentication.

<a id="dn-105"></a>

#### Authorization: resource-based, not just role-based

```csharp
// Role checks alone can't answer "is this THEIR order?"
[Authorize(Roles = "Customer")]
public async Task<IActionResult> Get(int id)
{
    var order = await _repo.GetAsync(id);
    return Ok(order);   // BROKEN: any customer can read any order (IDOR)
}
```

**IDOR (insecure direct object reference)** is the single most common serious API vulnerability, and no framework prevents it for you.

```csharp
// Resource-based authorization
var order = await _repo.GetAsync(id);
if (order is null) return NotFound();

var result = await _authz.AuthorizeAsync(User, order, "OrderOwnerOrAdmin");
if (!result.Succeeded) return Forbid();
```

Two design habits that help: scope every query by tenant/user at the repository level (`Where(o => o.CustomerId == _user.Id)`), and return **404 rather than 403** for resources the caller may not even know exist, so you don't leak their existence.

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a184"></a>

#### What authentication actually is

This part uses “token”, “claim”, “principal” and “scheme” throughout. They have precise meanings, and the precision matters — most authorisation bugs come from confusing two of them.

##### Two different questions

``` text
  AUTHENTICATION   "who are you?"        -> establishes IDENTITY
                   answer: a principal with claims
                   failure: 401 Unauthorized   (badly named: it means
                                                UNAUTHENTICATED)

  AUTHORISATION    "may you do this?"    -> a DECISION about a request
                   answer: yes or no
                   failure: 403 Forbidden

  they run in that order, and they are separate. A perfectly
  authenticated user is routinely forbidden - that is not an error,
  it is the system working.
```

##### The vocabulary, precisely

| Term           | Is                                       | Concretely                                               |
|----------------|------------------------------------------|----------------------------------------------------------|
| **Credential** | Proof you can present                    | A password, a certificate, a key                         |
| **Claim**      | **One statement about a subject**        | `email = nam@acme.com`, `role = Admin`, `tenant_id = 42` |
| **Identity**   | A set of claims from one source          | “what the login provider asserts about you”              |
| **Principal**  | The user, holding one or more identities | `HttpContext.User`                                       |
| **Token**      | A signed, expiring package of claims     | A JWT in an `Authorization` header                       |
| **Scheme**     | *How* to authenticate                    | Cookies, Bearer, Negotiate                               |
| **Policy**     | A named authorisation rule               | “`CanApproveInvoices`”                                   |

A **claim** is the atom of the whole system, and it is simply a name/value pair asserted by something you trust. “This subject's role is Admin” is a claim. Authorisation is then nothing more than inspecting claims — and sometimes the resource — and returning yes or no.

##### A token, opened up

``` text
  a JWT is three base64 segments joined by dots. It is SIGNED, not
  encrypted - anyone holding it can READ it.

    eyJhbGciOiJSUzI1NiJ9 . eyJzdWIiOiI0MiIsInJvbGUiOiJBZG1pbiJ9 . MEUCIQDf...
    \_______________/     \_____________________________/     \_________/
        HEADER                      PAYLOAD (claims)             SIGNATURE
      which algorithm            { "sub": "42",              proves the payload
                                   "role": "Admin",          was not altered and
                                   "exp": 1786000000,        came from the issuer
                                   "iss": "https://id..." }

  so:
    do NOT put secrets in a token. It is readable by the holder.
    ALWAYS validate the signature, the issuer, the audience AND the expiry.
      validating only the signature is a real and common vulnerability.
    a token cannot be revoked before it expires - which is why access
      tokens are short-lived and refresh tokens exist.
```

``` csharp
// the flow, end to end, in ASP.NET Core
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "https://login.acme.com";     // where to fetch signing keys
        o.TokenValidationParameters = new()
        {
            ValidateIssuer   = true,                // WHO issued it
            ValidateAudience = true,                // was it meant for THIS api
            ValidateLifetime = true,                // has it expired
            ValidateIssuerSigningKey = true,        // was it tampered with
        };
    });

app.UseAuthentication();   // reads the header, builds HttpContext.User
app.UseAuthorization();    // evaluates policies against that user

// and then, in a handler:
var tenant = User.FindFirst("tenant_id")?.Value;   // reading a CLAIM
```

> **\[Trap\]**
>
> **Common trap.** `401` and `403` are confused constantly, and the names are the reason. `401 Unauthorized` actually means *unauthenticated* — “I do not know who you are; log in”. `403 Forbidden` means *authenticated but not permitted* — “I know exactly who you are, and no”. Returning 401 for a permission failure sends clients into a login loop they can never escape, because logging in again changes nothing.

###### A token's life, and the failures at each step

``` text
  THE FULL FLOW, and what can go wrong at each hop

  1  user authenticates at the IDENTITY PROVIDER
       -> failure: wrong password, MFA, account locked. Not your problem.

  2  the IdP issues an ACCESS TOKEN (short, ~15 min) and a
     REFRESH TOKEN (long, days)
       -> why two: an access token cannot be revoked before it expires,
          so it is short. A refresh token CAN be revoked, so it is long.

  3  the client sends the access token with every request
       Authorization: Bearer eyJhbGci...

  4  YOUR API validates it, and this is where the bugs live:
       signature   -> was it tampered with?
       issuer      -> did WE trust who issued it?
       audience    -> was it meant for THIS api?
       expiry      -> is it still valid?
       algorithm   -> is it the one we expect?

       validating only the SIGNATURE is a real and common vulnerability:
       a token issued by the same IdP for a DIFFERENT application will
       pass, and you have just accepted someone else's credentials.

  5  claims become HttpContext.User
  6  authorisation policies evaluate those claims
  7  the access token expires -> the client uses the refresh token
       -> failure here logs the user out mid-session, which is the
          most-reported symptom of clock skew
```

``` csharp
// the validation that matters, made explicit
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "https://login.acme.com";     // fetches signing keys from
                                                     // /.well-known/openid-configuration
        o.Audience = "https://api.acme.com/orders";  // THIS api, specifically
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,                 // <- the one people disable
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],   // pin it
            ClockSkew = TimeSpan.FromSeconds(30),    // DEFAULT IS FIVE MINUTES
        };
    });
// the default 5-minute ClockSkew means an "expired" token is accepted for
// another five minutes. Usually harmless; occasionally exactly the window
// that matters after a revocation.
```

| Symptom                              | Cause                                                                |
|--------------------------------------|----------------------------------------------------------------------|
| 401 with a token that looks valid    | Audience or issuer mismatch — decode it and compare                  |
| Works locally, 401 deployed          | A different IdP tenant, or an authority URL typo                     |
| Intermittent 401s                    | **Clock skew** between your server and the IdP                       |
| 401 immediately after a key rotation | The signing-key cache is stale — check the metadata refresh interval |
| 403 where you expected 401           | Authenticated, but a policy refused — the correct behaviour          |
| Users logged out mid-session         | Refresh failing — check the refresh token's own expiry and rotation  |

> **\[Trap\]**
>
> **Common trap.** Never decode a token and trust its claims without validating the signature. Anyone can craft a JWT with `role: Admin` — it is base64, not encryption. And be specific about the algorithm: historic libraries accepted a token declaring `alg: none` and skipped verification entirely. Pin `ValidAlgorithms` rather than trusting the token to declare its own.

<a id="be-a185"></a>

#### Authentication and authorization

Authentication answers “who are you?”. Authorization answers “are you allowed to do this?”. They are separate middleware, in that order, and confusing them is the source of a lot of insecure code.

| Mechanism         | Where it fits                                                     |
|-------------------|-------------------------------------------------------------------|
| Cookie auth       | Server-rendered apps on one domain; needs CSRF protection         |
| JWT bearer tokens | APIs and SPAs; stateless, but revocation is hard                  |
| OAuth 2.0         | *Delegated authorization* — letting an app act on a user's behalf |
| OpenID Connect    | An identity layer on OAuth 2.0; this is what gives you *login*    |
| API keys          | Machine callers where you control both ends; rotate them          |
| mTLS              | Service-to-service inside a mesh or with a bank                   |

``` text
  OIDC authorization code flow with PKCE (the one to use for apps)

  1. app -> identity provider  /authorize?...&code_challenge=...
  2. user authenticates and consents
  3. IdP -> app  ?code=abc
  4. app -> IdP  /token  { code, code_verifier }      (back channel)
  5. IdP -> app  { id_token, access_token, refresh_token }
  6. app -> API  Authorization: Bearer <access_token>
  7. API validates signature, issuer, audience, expiry against the IdP's JWKS
```

``` csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "https://login.example.com";     // fetches JWKS automatically
        o.Audience  = "orders-api";
        o.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)       // default 5 min is generous
        };
    });
```

###### Worked example

What actually happens when a JWT arrives, step by step, because ‘validate the token’ hides a lot and each step has its own failure mode.

``` text
  Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6ImFiYzEyMyJ9.eyJzdWIiOi...

  1  split into header.payload.signature; base64url-decode the header
  2  read "kid" (key id) from the header
  3  look up that key in the cached JWKS
       - not cached? fetch https://issuer/.well-known/openid-configuration
       - then fetch the jwks_uri, cache for 24 h by default
       - FAILURE MODE: the IdP rotated keys and your cache is stale -> all tokens
         rejected until refresh. This is why the default refresh interval matters.
  4  verify the signature over header.payload with that public key
       - FAILURE MODE: alg mismatch, or "alg": "none" if validation is misconfigured
  5  validate the claims:
       iss  matches the configured Authority
       aud  contains this API's identifier   <- omitted more often than you would think
       exp  in the future (allowing ClockSkew, default 5 minutes)
       nbf  in the past
  6  build a ClaimsPrincipal and assign it to HttpContext.User
```

``` csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = cfg["Auth:Authority"];
        o.Audience  = cfg["Auth:Audience"];
        o.MapInboundClaims = false;              // keep "sub" as "sub", not the SOAP-era URI
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub", RoleClaimType = "roles"
        };
        o.RefreshInterval = TimeSpan.FromHours(1);       // JWKS refresh cadence
        o.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                // log the REASON - "401" alone tells you nothing
                _log.LogWarning(ctx.Exception, "Token validation failed: {Type}",
                                ctx.Exception.GetType().Name);
                return Task.CompletedTask;
            }
        };
    });
```

| Failure                                | Actual cause                                                                       |
|----------------------------------------|------------------------------------------------------------------------------------|
| All requests 401 after an IdP change   | JWKS cache holds a rotated-out key; shorten `RefreshInterval`                      |
| Works from Postman, fails from the SPA | Audience mismatch — the SPA requested a token for a different API                  |
| Intermittent 401 near expiry           | Clock skew between the IdP and your host; check NTP before changing `ClockSkew`    |
| `User.Identity.Name` is null           | `MapInboundClaims` renamed the claim; set `NameClaimType`                          |
| `[Authorize(Roles=...)]` never matches | The role claim is named `roles` or `groups`, not the default                       |
| Token accepted that should not be      | `ValidateAudience` left off — a token for any API in the tenant now works on yours |

###### Refresh tokens, revocation, and the trade JWTs make

A JWT is a bearer credential validated offline. That is its entire advantage — no network call per request — and its entire drawback: you cannot revoke it. A token issued for an hour is valid for an hour, even if you delete the user thirty seconds after issuing it.

| Requirement                             | Approach                                                          | Cost                                                          |
|-----------------------------------------|-------------------------------------------------------------------|---------------------------------------------------------------|
| Fast logout                             | Short access tokens (5–15 min) + refresh tokens                   | More token endpoint traffic                                   |
| Immediate revocation                    | A revocation list checked per request (Redis)                     | You have reintroduced a network call — and that is often fine |
| Session invalidation on password change | A `security_stamp` claim, compared against the store              | One cached lookup per request                                 |
| Refresh token theft detection           | Rotation with reuse detection — a reused token revokes the family | State to store; well worth it                                 |

``` csharp
// a revocation check that costs ~0.2 ms and buys real logout
o.Events.OnTokenValidated = async ctx =>
{
    var jti = ctx.Principal!.FindFirst("jti")?.Value;
    var cache = ctx.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();

    if (jti is not null && await cache.GetAsync($"revoked:{jti}") is not null)
        ctx.Fail("Token has been revoked.");
};
```

The pragmatic position most teams land on: 10-minute access tokens, rotating refresh tokens with reuse detection, and a revocation list only for the high-consequence cases (admin lockout, compromised account). Chasing true statelessness while also needing instant revocation is chasing two incompatible goals.

<a id="be-a186"></a>

#### Authorization policies that scale

Role checks scattered through controllers become unmaintainable by about the twentieth endpoint. Policies move the decision into one place and give it a name the business would recognise.

``` csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanRefund", p => p.RequireAuthenticatedUser()
                                  .RequireClaim("scope", "orders.write")
                                  .Requirements.Add(new RefundLimitRequirement(5000m)));

// resource-based: does THIS user own THIS order?
var result = await _authz.AuthorizeAsync(User, order, "SameTenant");
if (!result.Succeeded) return Forbid();
```

| Model                  | Decision based on               | Fits                                             |
|------------------------|---------------------------------|--------------------------------------------------|
| Role-based (RBAC)      | The user's role                 | Small, stable permission sets                    |
| Claims-based           | Arbitrary claims in the token   | Most APIs; scopes and tenant IDs                 |
| Resource-based         | User + the specific object      | Ownership and per-record rules                   |
| Attribute-based (ABAC) | User, resource, action, context | Complex enterprise rules; external policy engine |

> **\[Trap\]**
>
> **Common trap.** Multi-tenancy is an authorization problem disguised as a data problem. Every query needs a tenant filter, and relying on developers to remember means one forgotten `Where` leaks another customer's data. Use an EF Core global query filter plus tests that prove it.

###### Beyond roles, and where the decision actually belongs

``` text
  the progression, and when each stops working

  ROLE-BASED        [Authorize(Roles = "Admin")]
                    breaks as soon as "admin of WHICH tenant?" is asked.
                    roles multiply: TenantAdmin, TenantBillingAdmin, ...

  CLAIM-BASED       [Authorize(Policy = "CanApproveInvoices")]
                    better: the policy name is a CAPABILITY, not a job
                    title. Roles map to capabilities in one place.

  RESOURCE-BASED    "may this user approve THIS invoice?"
                    the decision needs the RESOURCE, so it cannot be an
                    attribute. -> IAuthorizationService, imperatively.

  POLICY ENGINE     OPA, Cedar, OpenFGA
                    when rules are numerous, change without a deploy,
                    or must be audited and shared across services.
```

``` csharp
// capability policies - map roles to capabilities ONCE, at the boundary
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanApproveInvoices", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "invoices:approve") ||
        ctx.User.IsInRole("FinanceManager")))
    .AddPolicy("SameTenant", p => p.AddRequirements(new SameTenantRequirement()));

// resource-based: the ONLY correct form when the answer depends on the row
public class InvoiceHandler : AuthorizationHandler<ApproveRequirement, Invoice>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, ApproveRequirement req, Invoice invoice)
    {
        var tenant = ctx.User.FindFirst("tenant_id")?.Value;
        if (invoice.TenantId.ToString() == tenant &&
            invoice.Amount <= GetApprovalLimit(ctx.User))
            ctx.Succeed(req);
        return Task.CompletedTask;
    }
}

// used imperatively, because the attribute cannot see the resource
var result = await authz.AuthorizeAsync(User, invoice, "ApproveInvoice");
if (!result.Succeeded) return Results.Forbid();
```

> **\[Trap\]**
>
> **Common trap.** Attribute-based authorisation cannot express “this row”, so multi-tenant systems that rely on it alone are one forgotten `WHERE tenant_id = ...` away from a cross-tenant leak. The durable defence is **defence in depth**: a query filter in EF Core, *and* row-level security in the database, *and* a resource-based check. The database-level control is the one that still holds when someone writes raw SQL or a report bypasses your repository.

``` csharp
// the EF Core layer of that defence
modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == _tenant.Id);
// applies to EVERY query automatically. Note: it does NOT apply to raw SQL,
// to ExecuteUpdate/ExecuteDelete, or when someone calls IgnoreQueryFilters().
// which is exactly why the database-level control matters as well.
```

### Authentication and Authorization

<p class="src">From <em>Backend Engineering</em> · APIs · Authentication and Authorization</p>

<p class="pd">Two different questions, constantly conflated: 
 
  Authentication  — who are you? (401 if unanswered) 
  Authorization  — are you allowed? (403 if no)</p>

<a id="be-e516"></a>

#### The mechanisms

**Basic auth** — `Authorization: Basic base64(user:pass)`. Base64 for header safety, not secrecy. Sends the real password on every request; no expiry, no scope, no revocation short of a password change. Acceptable over TLS for internal service-to-service. Not for user-facing anything.

**API keys** — a long random string in a header (`X-API-Key`) or `Authorization: Bearer`. Simple, common for server-to-server. Weaknesses: usually long-lived, often over-privileged, and leak constantly into git repos and CI logs. Mitigate with scoping, rotation, and a prefix (`sk_live_…`) so secret-scanners can spot them.

**Never in the query string.** URLs land in access logs, browser history, and the `Referer` header sent to third parties.

**Bearer tokens (OAuth 2.0 / OIDC)** — the modern default for user-facing APIs:

``` yaml
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
```

Anyone holding it can use it ("bearer"), so it must be short-lived and transmitted only over TLS. Comes with expiry, scopes, and revocation — the three things Basic lacks.

The flows, briefly:

- **Authorization Code + PKCE** — for anything with a user. The only correct choice for SPAs and mobile now.
- **Client Credentials** — machine-to-machine, no user.
- **Refresh tokens** — long-lived, used to mint new short-lived access tokens. Rotate them.
- **Implicit** and **Password (ROPC)** grants are deprecated. Don't.

**mTLS** — both sides present certificates. Strong, standard inside service meshes, operationally heavy because of certificate lifecycle.

<a id="be-e517"></a>

#### JWTs — useful and routinely misused

A JWT is three base64url segments: `header.payload.signature`.

``` json
{ "sub": "user-42", "exp": 1755432000, "scope": "orders:read", "iss": "https://auth.example.com" }
```

The value: the signature lets any service verify the token **without calling the auth server**. That's what makes it scale.

The cost, and it's the thing people miss: **you cannot revoke a JWT.** It's valid until it expires, full stop. Fire an employee and their token works until `exp`. Mitigations: short lifetimes (5–15 minutes) plus refresh tokens, or a revocation list — which reintroduces the central lookup you used a JWT to avoid.

The mistakes, in order of severity:

1.  **Not verifying the signature.** Decoding is not verifying. Every JWT library has separate functions for these, and the wrong one is easy to call.
2.  **Trusting the `alg` header.** `alg: none` was a real, widely-exploited vulnerability. Pin the expected algorithm server-side; never let the token choose.
3.  **Putting secrets in the payload.** It's base64, not encryption. Anyone can read it.
4.  **Storing them in `localStorage`.** Readable by any XSS. Use `HttpOnly` `Secure` `SameSite` cookies for browsers.
5.  **Skipping `exp`, `iss`, `aud` validation.** A valid signature from a *different* system is still a valid signature.

<a id="be-e518"></a>

#### Authorization models

- **RBAC** — roles carry permissions; users get roles. Simple, sufficient for most systems.
- **ABAC** — decisions from attributes (department, time, resource owner). Flexible, harder to reason about.
- **ReBAC** — relationship-based ("can edit if in the owning team"). What Zanzibar/OpenFGA implement; right for deeply hierarchical sharing.

**Enforce on the server, per request, per resource.** The most common serious API vulnerability — OWASP's \#1 — is **BOLA/IDOR**: checking that you're logged in but not that *this* record is yours. `GET /orders/43` returning someone else's order because you only validated the token.

``` csharp
var order = await _db.Orders.FindAsync(id);
if (order.CustomerId != currentUser.Id) return Forbid();   // ← the line people forget
```

Hiding a button in the UI is not authorization.

<a id="be-e519"></a>

#### Browser-specific concerns

**CORS** is a browser restriction, not a server security feature. By default a page on `a.com` cannot read a response from `b.com`. The server opts in via `Access-Control-Allow-Origin`. Two things people get wrong: it protects *users of browsers* and does nothing against curl or a server-side caller; and `Allow-Origin: *` combined with credentials is forbidden by the spec for good reason.

Non-simple requests trigger a **preflight** `OPTIONS` round trip first. Cache it with `Access-Control-Max-Age` or you double your latency.

**CSRF** applies when the browser attaches credentials automatically — i.e. cookies. A form on `evil.com` can `POST` to your API and the browser will helpfully include the session cookie. Defenses: `SameSite=Lax` or `Strict` cookies, plus anti-forgery tokens. **Bearer tokens in an `Authorization` header are not vulnerable**, because the browser doesn't attach them automatically. This is the strongest argument for header tokens over cookies in APIs.

------------------------------------------------------------------------

### Access, tenancy and changing the schema

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Access, tenancy and changing the schema</p>

<p class="pd">Who is allowed to see what, how tenant isolation is enforced rather than remembered, and how to change a table under live traffic without downtime.</p>

<a id="be-w22"></a>

#### Authorization: policies, requirements and the resource check

``` csharp
// ---------- policies: what, not who ----------
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("orders:read",  p => p.RequireAuthenticatedUser()
                                     .RequireClaim("scope", "orders.read"))
    .AddPolicy("orders:write", p => p.RequireAuthenticatedUser()
                                     .RequireClaim("scope", "orders.write"))
    .AddPolicy("orders:refund", p => p.AddRequirements(new RefundRequirement(limit: 500m)));

// a requirement + handler, for rules a claim cannot express
public sealed record RefundRequirement(decimal Limit) : IAuthorizationRequirement;

public sealed class RefundHandler(ILogger<RefundHandler> log)
    : AuthorizationHandler<RefundRequirement, Order>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext ctx,
        RefundRequirement req, Order order)          // ← RESOURCE-based
    {
        var isOwner   = ctx.User.UserId() == order.CustomerId;
        var isSupport = ctx.User.IsInRole("support");
        var withinLimit = order.Total <= req.Limit;

        if (isSupport && withinLimit) ctx.Succeed(req);
        else if (isSupport) log.LogInformation(
            "Refund of {Amount} exceeds the {Limit} limit — escalation required",
            order.Total, req.Limit);
        else if (isOwner) ctx.Succeed(req);

        return Task.CompletedTask;
    }
}

// at the call site: the object must be loaded before it can be authorised
app.MapPost("/api/v1/orders/{id:guid}/refund", async (Guid id, AppDb db,
        IAuthorizationService authz, ClaimsPrincipal user, CancellationToken ct) =>
{
    var order = await db.Orders.FindAsync([id], ct);
    if (order is null) return Results.NotFound();

    var check = await authz.AuthorizeAsync(user, order, "orders:refund");
    if (!check.Succeeded)
        return user.Identity?.IsAuthenticated == true
            ? Results.Forbid()          // 403: we know who you are, you may not
            : Results.Unauthorized();   // 401: we do not know who you are

    …
});
```

> **\[Trap\]**
>
> **Common trap.** Returning 404 instead of 403 for objects the caller may not see is a deliberate choice, not an accident: 403 confirms the resource *exists*, which leaks information in a multi-tenant system. Pick one convention, write it down, and apply it everywhere — inconsistency is itself an enumeration oracle.
