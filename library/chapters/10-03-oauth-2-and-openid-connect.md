---
id: "10-3"
number: "10.3"
title: "OAuth 2 and OpenID Connect"
part: 10
part_title: "Security"
words: 5847
minutes: 25
---

# 10.3 OAuth 2 and OpenID Connect

> **Quick look.** Delegate sign-in to an identity provider with OAuth 2.0 and OpenID Connect.
>
> - Apps never handle passwords and users get single sign-on.
> - OAuth issues access tokens to call APIs within scopes; OIDC adds ID tokens and discovery.
> - Use authorization code flow with PKCE and a state check for user apps; client credentials for services.
> - Access tokens are short-lived and audience-bound; ID tokens are for the client only; rotate and guard refresh tokens.
> - APIs check signature (with an allowed algorithm), issuer, audience, expiry and scopes; let the framework do it.
> - Keep tokens out of browser storage, ideally behind a backend-for-frontend.
> - Single logout must end the app session, the IdP session and other apps' sessions (back-channel logout).


### The big picture

Most organizations don't want every application to handle passwords. An airline has a website, mobile apps, agent tools, partner portals and dozens of internal services. If each kept its own user accounts and password checks, each would need its own password storage, MFA, lockout and recovery, and each would be another place for passwords to leak. Instead, sign-in moves to one dedicated service, an identity provider. Applications send users there, and get back proof of who signed in, plus permission to call APIs on their behalf.

Two standards make that work. OAuth 2.0 lets an application get limited permission to call an API for a user, without ever seeing the user's password. OpenID Connect adds a standard way to learn who the user is. This chapter explains how the two fit together, following the main flow step by step. It covers the three kinds of token that come back, and exactly how an API must check a token before trusting it. It also covers where browser apps should keep tokens, and how ASP.NET Core does all this for you. The example is a miniature identity provider, client app and API in one file, run with Node. It tries each of the classic attacks, and shows the specific check that rejects each one.

### Before you start

**Builds on:** 10.2 Authentication and authorization, 10.1 Thinking like an attacker, 9.1 How a browser shows a page, 8.4 A Node and TypeScript backend.

**Ideas explained later:**

- **Managed identity** (10.5): An identity a cloud platform gives a service so it can get tokens without any stored secret.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Cross-site request forgery** (10.4): Making a user's browser send an unwanted, authenticated request to a site from another site. Often shortened to CSRF.
- **Clock skew** (12.2): The difference between two machines' clocks at the same moment.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.

**Tools and technologies named here:**

**The code uses:** lambdas. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between OAuth 2.0 and OpenID Connect?**

> OAuth 2.0 is about authorization: getting permission, as an access token, for an app to call an API on a user's behalf. OpenID Connect is a layer on top that adds authentication: an ID token saying who the user is.

**2. What problem does PKCE solve?**

> An authorization code travels back through the browser, where it can be intercepted. With PKCE, the app sends only a hash of a secret when it starts, and must present the secret itself to redeem the code, so a stolen code is useless.

**3. What is the difference between an access token and an ID token?**

> An access token is for an API: it says what the bearer may do there, and its audience is that API. An ID token is for the client app: it says who signed in. Never send an ID token to an API as proof of access.

**4. What must an API check before trusting a JWT access token?**

> The signature, using the issuer's published key and an allowed algorithm; the issuer; the audience, which must be this API; the expiry; and the scopes or roles the endpoint needs.

**5. Can anyone read the contents of a JWT?**

> Yes. A normal JWT is signed, not encrypted: its payload is only base64url-encoded. The signature stops changes, not reading, so tokens must never carry secrets.

**6. Which flow does a background service use to call another API with no user involved?**

> The client credentials flow: the service authenticates to the identity provider as itself, with a secret, a certificate or a managed identity, and receives an access token for the target API.

**7. Why is a refresh token more sensitive than an access token?**

> It lives much longer and can be exchanged for new access tokens again and again. It must be stored securely, rotated on each use, and revoked when a user signs out or a device is lost.

### Why delegate sign-in

An **identity provider** is a service whose job is authenticating users: showing the sign-in page, checking passwords and second factors, supporting passkeys, locking out attackers and handling account recovery. Microsoft Entra ID, formerly Azure Active Directory, is common in organizations that use Microsoft tools. Okta and Auth0 are widely used commercial services, Google and Apple offer consumer sign-in, and Keycloak is a popular open-source provider you run yourself.

Applications then delegate sign-in to it. Users get **single sign-on**: signing in once gives access to many applications. Security features such as MFA and passkeys, from Authentication and authorization (10.2), are built once, centrally. Applications never see passwords at all, so they can't leak them. Employees who leave are disabled in one place. And partners can sign in with their own organization's identity provider.

### OAuth 2.0

**OAuth 2.0**, published in 2012, solves a narrower problem than its reputation suggests: letting an application call an API on a user's behalf, with limited permission, without the user handing over their password. The classic comparison is a valet key, which drives the car but doesn't open the boot.

It defines four roles. The resource owner is the user. The **OAuth client** is the application that wants access, such as a booking web app or mobile app. The **authorization server** issues tokens after the user signs in and agrees; in practice it is the identity provider. The **resource server** is the API that accepts those tokens, such as the bookings API.

What the client may do is described by **OAuth scopes**: named permissions such as `bookings.read` or `bookings.refund`, requested by the client and shown to the user on a consent screen when they come from another company. The result is an **access token**: a credential the client sends to the API, as a bearer token from Authentication and authorization (10.2), which says what it may do there.

### OpenID Connect

OAuth on its own says nothing standard about who the user is. **OpenID Connect** (OIDC), published in 2014, adds that. When the client includes the `openid` scope, the authorization server also returns an **ID token**, a JWT describing the sign-in. It says who the user is, in the `sub` (subject) claim, plus standard claims such as name and email. It also says which provider issued it, and for which client. OIDC also standardizes a discovery document at `/.well-known/openid-configuration`. It lists the provider's endpoints and the location of its public signing keys, so libraries configure themselves from one URL.

In practice, "sign in with the company account" means OpenID Connect, and "call the API with the user's permission" means OAuth. Most apps use both at once.

### The authorization code flow

The **authorization code flow** with **PKCE**, Proof Key for Code Exchange, is the recommended flow for every kind of app that signs users in: web apps, single-page apps, mobile and desktop apps. The OAuth security best current practice, published as RFC 9700 in 2025, requires PKCE for all of them. The steps are these.

First, the app creates a random secret, the code verifier, and keeps it. It hashes it to make the code challenge. It then sends the user's browser to the authorization server's `/authorize` endpoint with its client id, the scopes it wants, the challenge, a random `state` value, and its **redirect URI**, the address to come back to.

Second, the user signs in at the identity provider, with passwords, MFA or passkeys, and consents if asked. Third, the provider redirects the browser back to the redirect URI with a short-lived, single-use authorization code and the same `state`. The app checks that `state` matches the value it sent, which stops attackers from injecting their own responses, a form of the cross-site request forgery from Common web vulnerabilities (10.4).

Fourth, the app sends the code, together with the original code verifier, directly to the `/token` endpoint. That is a back-channel call, not through the browser. The provider hashes the verifier, checks it matches the challenge from the first step, and returns the tokens.

The code passes through the browser, where it can leak: in logs, browser history, or to a malicious app registered for the same address on a phone. PKCE makes a leaked code useless, because redeeming it needs the verifier, which never left the app. Server-side web apps also authenticate to the token endpoint with a client secret or certificate. Apps that run on users' devices can't keep a secret, so for them PKCE is the protection.

Two older flows are now deprecated. The implicit flow returned tokens directly in the redirect, exposing them in the browser. The password grant had apps collect the user's password and send it to the provider, which is the very thing OAuth set out to avoid. Don't use either for new work.

### Tokens

Three kinds of token come back, with different jobs.

The access token goes to the API. It should be short-lived, minutes to an hour, and bound to its audience: a token for the bookings API is useless at the payments API. Many providers issue access tokens as JWTs the API can check itself. Others issue opaque random strings that the API checks by asking the provider, which is called token introspection.

The ID token stays with the client. It tells the app who signed in, and its audience is the client itself. Sending it to an API as if it were an access token is a common and serious mistake, and a correctly validating API rejects it for the wrong audience, as the example shows.

A **refresh token** lets the client get new access tokens without sending the user back to sign in. It is long-lived, so it is the most valuable token to steal. Providers rotate refresh tokens, issuing a new one each time one is used and treating reuse of an old one as theft. Store them securely, never in browser script storage. Revoke them when a user signs out or a device is lost.

### JSON Web Tokens

A **JWT**, JSON Web Token, is three base64url-encoded parts joined with dots: a header naming the signing algorithm and key id, a payload of claims, and a signature over the first two. Standard claims include `iss`, the **issuer** that created the token; `aud`, the **audience** it is meant for; `sub`, the subject; `exp`, when it expires; and often `scope` or `roles`.

A JWT is signed, not encrypted. Anyone who has one can read its payload, as the example shows by decoding it. The signature proves who issued it and that nobody has changed it. So never put secrets in tokens.

An API must validate every token before trusting a single claim in it, in this order. Check the signature with the issuer's public key, found by the header's key id in the provider's published key set, called the **JWKS** (JSON Web Key Set). Accept only the algorithm you expect: a famous class of attack sets the header's algorithm to `none`, or switches algorithms, hoping the validator lets the token choose. Check that the issuer is the one you trust, that the audience is this API, and that the token hasn't expired, allowing a minute or two of clock skew. Only then check the scopes or roles the endpoint needs. Leaving out any one of these checks is a real vulnerability. The example tries each attack in turn.

Because a validated JWT is trusted until it expires, it is hard to revoke early. That is the main reason access tokens are kept short-lived and refresh tokens do the long-term work.

### Seeing it for real

This file plays all three parts: an identity provider that issues RS256-signed tokens after a PKCE-protected code exchange, a client app that runs the flow, and an API that validates tokens. It then tries seven things an attacker or a careless client might send. It uses only Node's built-in `crypto` module.

```typescript
// oauth.ts: run with: node oauth.ts   (an identity provider, a client and an API in one file, using only node:crypto)
import { createHash, generateKeyPairSync, randomBytes, sign, verify, type KeyObject } from "node:crypto";

const b64url = (data: Buffer | string) => Buffer.from(data).toString("base64url");
const sha256 = (text: string) => createHash("sha256").update(text).digest();

// ---- PKCE: the client proves that the app redeeming a code is the app that asked for it -------------
const pkceChallenge = (verifier: string) => b64url(sha256(verifier));
console.log(pkceChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));   // the test value from RFC 7636:
// E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM

// ---- JWTs: header.payload.signature, each part base64url-encoded; signed, not encrypted --------------
type Claims = Record<string, unknown> & { iss: string; aud: string; sub: string; exp: number };
function issueJwt(claims: Claims, key: KeyObject, kid: string) {
  const head = b64url(JSON.stringify({ alg: "RS256", typ: "JWT", kid }));
  const body = b64url(JSON.stringify(claims));
  return `${head}.${body}.${b64url(sign("sha256", Buffer.from(`${head}.${body}`), key))}`;
}

// ---- The identity provider ------------------------------------------------------------------------
const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
const ISSUER = "https://login.airline.example";
const keys = new Map([["key-2026-09", publicKey]]);                  // published at the provider's JWKS endpoint
const codes = new Map<string, { user: string; challenge: string; scope: string }>();

function authorize(user: string, scope: string, challenge: string) {  // after the user signs in and consents
  const code = b64url(randomBytes(16));
  codes.set(code, { user, challenge, scope });
  return code;                                                        // sent back to the app through the browser
}

function token(code: string, verifier: string) {                     // the app's back channel to the provider
  const grant = codes.get(code);
  codes.delete(code);                                                 // a code works once
  if (!grant) throw new Error("invalid_grant: unknown or used code");
  if (pkceChallenge(verifier) !== grant.challenge) throw new Error("invalid_grant: PKCE verifier doesn't match");
  const now = Math.floor(Date.now() / 1000);
  return {
    access_token: issueJwt({ iss: ISSUER, aud: "bookings-api", sub: grant.user, scope: grant.scope, exp: now + 300 }, privateKey, "key-2026-09"),
    id_token: issueJwt({ iss: ISSUER, aud: "web-app", sub: grant.user, name: "Ada Lovelace", exp: now + 300 }, privateKey, "key-2026-09"),
    expires_in: 300,
  };
}

// ---- The API: validates every access token before trusting a single claim in it --------------------
function validate(jwt: string, requiredScope: string): Claims {
  const [head, body, signature] = jwt.split(".");
  if (!head || !body || signature === undefined) throw new Error("malformed token");
  const header = JSON.parse(Buffer.from(head, "base64url").toString()) as { alg: string; kid: string };
  if (header.alg !== "RS256") throw new Error(`algorithm ${header.alg} not allowed`);   // never let the token choose
  const key = keys.get(header.kid);
  if (!key || !verify("sha256", Buffer.from(`${head}.${body}`), key, Buffer.from(signature, "base64url")))
    throw new Error("bad signature");
  const claims = JSON.parse(Buffer.from(body, "base64url").toString()) as Claims;
  if (claims.iss !== ISSUER) throw new Error("wrong issuer");
  if (claims.aud !== "bookings-api") throw new Error("wrong audience");               // a token for another app
  if (claims.exp < Date.now() / 1000) throw new Error("expired");
  if (!String(claims.scope ?? "").split(" ").includes(requiredScope)) throw new Error(`missing scope ${requiredScope}`);
  return claims;
}
const attempt = (label: string, work: () => unknown) => {
  try { const result = work(); console.log(`${label}: accepted ${JSON.stringify(result)}`); }
  catch (e) { console.log(`${label}: rejected (${(e as Error).message})`); }
};

// ---- The client app: authorization code flow with PKCE ----------------------------------------------
const verifier = b64url(randomBytes(32));                             // kept secret by the app
const code = authorize("ada", "openid bookings.read", pkceChallenge(verifier));   // only the challenge travels
const tokens = token(code, verifier);
const [, payload] = tokens.access_token.split(".");
console.log(`access token payload: ${Buffer.from(payload!, "base64url").toString()}`);   // readable by anyone
// access token payload: {"iss":"https://login.airline.example","aud":"bookings-api","sub":"ada","scope":"openid bookings.read","exp":...}

attempt("valid token", () => validate(tokens.access_token, "bookings.read").sub);        // accepted "ada"
attempt("needs another scope", () => validate(tokens.access_token, "bookings.refund"));   // rejected (missing scope bookings.refund)
attempt("ID token sent to the API", () => validate(tokens.id_token, "bookings.read"));    // rejected (wrong audience)
const [h, p, s] = tokens.access_token.split(".");
const edited = b64url(JSON.stringify({ ...JSON.parse(Buffer.from(p!, "base64url").toString()), sub: "linus" }));
attempt("payload edited", () => validate(`${h}.${edited}.${s}`, "bookings.read"));       // rejected (bad signature)
const none = b64url(JSON.stringify({ alg: "none", typ: "JWT", kid: "key-2026-09" }));
attempt("alg none", () => validate(`${none}.${p}.`, "bookings.read"));                   // rejected (algorithm none not allowed)
const stale = issueJwt({ iss: ISSUER, aud: "bookings-api", sub: "ada", scope: "bookings.read", exp: 1_000_000_000 }, privateKey, "key-2026-09");
attempt("expired token", () => validate(stale, "bookings.read"));                        // rejected (expired)
attempt("code used twice", () => token(code, verifier));                                  // rejected (invalid_grant: unknown or used code)
const stolen = authorize("ada", "openid bookings.read", pkceChallenge(verifier));
attempt("stolen code, no verifier", () => token(stolen, b64url(randomBytes(32))));        // rejected (PKCE verifier doesn't match)
```

The run's output:

```text
E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
access token payload: {"iss":"https://login.airline.example","aud":"bookings-api","sub":"ada","scope":"openid bookings.read","exp":1790779280}
valid token: accepted "ada"
needs another scope: rejected (missing scope bookings.refund)
ID token sent to the API: rejected (wrong audience)
payload edited: rejected (bad signature)
alg none: rejected (algorithm none not allowed)
expired token: rejected (expired)
code used twice: rejected (invalid_grant: unknown or used code)
stolen code, no verifier: rejected (invalid_grant: PKCE verifier doesn't match)
```

The first line is the PKCE test value published in the PKCE standard, RFC 7636, which confirms the challenge is computed exactly as specified. Every rejection maps to one check in `validate` or `token`. Remove any one of them, and the matching attack succeeds. That is a quick way to see that none of them is optional.

This is for understanding only. Real applications must never implement token issuing or validation by hand, because the details are easy to get subtly wrong. Use the identity provider and a maintained library.

### Services and devices

When no user is involved, such as a nightly job calling the bookings API, the **client credentials flow** applies. The service authenticates to the authorization server as itself, with a client secret, a certificate or, best, a managed identity from Secrets and cryptography basics (10.5), and receives an access token for the target API. Give each service its own identity and only the scopes it needs, which is least privilege from Thinking like an attacker (10.1).

Devices without a convenient browser or keyboard, such as a gate display or a command-line tool, use the device code flow: the device shows a short code, and the user enters it on their phone to approve.

### Tokens in browser apps

A single-page app, from How a browser shows a page (9.1), can run the authorization code flow with PKCE itself. The question is where it keeps the tokens. Anything in `localStorage` or `sessionStorage` can be read by any script running on the page, so one cross-site scripting flaw hands every token to an attacker, as Common web vulnerabilities (10.4) explains. Tokens kept only in memory are safer, and are lost on reload.

The approach current guidance favours is a backend for frontend, from A Node and TypeScript backend (8.4). A small server-side component runs the flow, keeps the tokens on the server, and gives the browser only an HttpOnly session cookie. It then calls APIs on the browser's behalf, adding the access token itself. Tokens never reach the browser at all. Mobile apps run the flow through the system browser, not an embedded web view, and store refresh tokens in the operating system's secure storage.

### In ASP.NET Core

ASP.NET Core does all of this through configuration, and you shouldn't write any of it by hand.

An API that accepts access tokens uses JWT bearer authentication. It fetches the provider's discovery document and keys itself, caches them, and validates the signature, issuer, audience, lifetime and algorithm on every request:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.airline.example";      // discovery document and signing keys come from here
        options.Audience = "bookings-api";                        // tokens for any other audience are rejected
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("refunds", policy => policy.RequireClaim("scope", "bookings.refund"));
```

A server-rendered web app that signs users in uses OpenID Connect with a cookie, so the flow runs on the server and the browser gets only the session cookie:

```csharp
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.Authority = "https://login.airline.example";
        options.ClientId = "web-app";
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];   // from a secret store, never from source control
        options.ResponseType = "code";                                       // the authorization code flow; PKCE is on by default
        options.Scope.Add("bookings.read");
        options.SaveTokens = true;                                           // keep tokens server-side to call APIs
    });
```

For Microsoft Entra ID, the Microsoft.Identity.Web library wraps both with Entra-specific defaults, and can acquire tokens to call downstream APIs. The packages are `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.AspNetCore.Authentication.OpenIdConnect`.

### In short

Delegate sign-in to an identity provider, so applications never handle passwords and users get single sign-on. OAuth 2.0 issues access tokens that let a client call an API on a user's behalf within OAuth scopes. OpenID Connect adds ID tokens and discovery. Use the authorization code flow with PKCE and a checked state value for every app that signs users in, and client credentials for services. Access tokens are short-lived and bound to an audience, ID tokens are for the client only, and refresh tokens are long-lived secrets to rotate and guard. APIs must check a JWT's signature with an allowed algorithm, then its issuer, audience, expiry and scopes. Keep tokens out of browser script storage, preferably behind a backend for frontend, and let the framework do the validation.

### What goes wrong

**Skipping validation steps.** An API checks the signature but not the audience, and accepts tokens issued for another app. Validate signature, algorithm, issuer, audience and expiry, with a maintained library.

**Letting the token choose its algorithm.** A validator that honours `alg: none` or switches algorithms accepts forged tokens. Allow only the expected algorithm.

**ID tokens used as access tokens.** A client sends its ID token to APIs, and the APIs accept it. Request proper access tokens, and reject tokens with the wrong audience.

**Tokens in `localStorage`.** One injected script steals every user's tokens. Keep them on the server behind a backend for frontend, or in memory.

**Loose redirect URIs.** A wildcard or prefix-matched redirect URI lets an attacker receive authorization codes. Register exact redirect URIs.

**No PKCE or no state check.** Intercepted codes are redeemed, or forged responses accepted. Use PKCE everywhere and check `state`.

**Long-lived access tokens.** A leaked token works for days, with no way to revoke it. Keep access tokens short-lived and use rotating refresh tokens.

**Secrets in tokens or logs.** Tokens are readable and often logged. Put nothing secret in claims, and never log tokens.

### Cost model

Validating a JWT locally costs one signature check, tens of microseconds for RS256 and less for elliptic-curve signatures, plus fetching the provider's keys once and caching them. Opaque tokens cost a call to the provider per request, or per cache period, which adds latency and a dependency. The sign-in flow itself costs a few redirects and one back-channel call, a second or two for the user, once per session. Short access token lifetimes mean a refresh call every few minutes to an hour per active client, a small and steady load on the identity provider.

### In other languages (optional)

**JavaScript and TypeScript.** The `jose` library validates JWTs and fetches key sets; `openid-client` implements the flows for Node.js servers; providers publish browser libraries such as MSAL.js for Entra ID.

**Python.** Authlib implements OAuth and OpenID Connect clients and servers; PyJWT validates tokens.

**Java.** Spring Security's OAuth2 resource server and client modules provide the same two configurations as ASP.NET Core.

**Mobile.** AppAuth for iOS and Android runs the authorization code flow with PKCE through the system browser.

### Key terms

- **Identity provider**: A service whose job is signing users in and issuing proof of who they are.
- **Single sign-on**: Signing in once to get access to many applications.
- **OAuth 2.0**: The standard for letting an application call an API on a user's behalf with limited permission.
- **OAuth client**: The application that requests tokens and calls APIs with them.
- **Authorization server**: The OAuth component that authenticates users and issues tokens, usually the identity provider.
- **Resource server**: The API that accepts access tokens.
- **OAuth scope**: A named permission a client requests, such as bookings.read.
- **Access token**: A credential a client sends to an API, saying what it may do there.
- **OpenID Connect**: A layer on OAuth 2.0 that adds sign-in: ID tokens, standard user claims and discovery.
- **ID token**: A JWT that tells the client app who signed in. Not for calling APIs.
- **Authorization code flow**: The OAuth flow where the browser brings back a one-time code that the app exchanges for tokens.
- **PKCE**: Proof Key for Code Exchange: a secret and its hash that make a stolen authorization code useless.
- **Redirect URI**: The registered address the authorization server sends the browser back to.
- **Refresh token**: A long-lived token a client exchanges for new access tokens.
- **JWT**: JSON Web Token: signed, base64url-encoded claims in three dot-separated parts.
- **Issuer**: The authority that created a token, in its iss claim.
- **Audience**: Who a token is meant for, in its aud claim; other recipients must reject it.
- **JWKS**: JSON Web Key Set: the published public keys an issuer signs tokens with.
- **Client credentials flow**: The OAuth flow in which a service gets an access token as itself, with no user.

### Where this leads

The script attacks that make token storage matter, and cross-site request forgery, are in Common web vulnerabilities (10.4). Signatures, key pairs and how services can authenticate without stored secrets are in Secrets and cryptography basics (10.5). Security between services inside a larger system comes back in Monoliths, modules and services (12.1).

### Check yourself

1. Remove the audience check from `validate` in `oauth.ts` and rerun. Note which attack now succeeds, and describe how it could happen in a real system.
2. Add a second signing key, `key-2026-10`, issue tokens with it, and keep old tokens valid until they expire. This is key rotation.
3. Add a `nbf` (not before) check with 60 seconds of allowed clock skew, and a test token that isn't valid yet.
4. Decode the access token from an application you use, with a local tool rather than a website, and list its issuer, audience, expiry and scopes.
5. Configure `AddJwtBearer` in an ASP.NET Core API against a real or local identity provider, such as Keycloak in a container, and call it with a token from the client credentials flow.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A token for another service accepted

An internal API accepts any token from the company's identity provider, including ones issued for other services.

*Validate audience and issuer*

```csharp
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = "https://login.example.com/";
    o.Audience  = "api://bookings";                    // reject tokens minted for other APIs
    o.TokenValidationParameters.ValidateAudience = true;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    o.MapInboundClaims = false;
});
```

> **Lesson:** A valid signature isn't enough. Check issuer, audience, expiry and scopes; let the framework do it.

#### Example 2: Tokens out of the browser with a BFF

A security review flags access tokens stored in localStorage by the SPA.

*Backend-for-frontend with cookies*

```csharp
builder.Services.AddAuthentication(o => { o.DefaultScheme = "cookie"; o.DefaultChallengeScheme = "oidc"; })
    .AddCookie("cookie", o => { o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                                 o.Cookie.SameSite = SameSiteMode.Strict; })
    .AddOpenIdConnect("oidc", o =>
    {
        o.Authority = "https://login.example.com/"; o.ClientId = "web-bff";
        o.ResponseType = "code"; o.UsePkce = true; o.SaveTokens = true;   // tokens stay server-side
    });
// the BFF forwards API calls with the user's token (e.g. via YARP); the browser only holds an HttpOnly cookie
```

> **Lesson:** Script-readable storage turns any XSS into token theft. Keep tokens on the server behind an HttpOnly cookie.

### Logging & Observability

<p class="src">From <em>.NET Backend Deep Dive</em> · Logging & Observability</p>

<a id="dn-3"></a>

#### SSO Logout Request (Single Logout / SLO)

##### What / Why
SSO login is easy: redirect to the IdP, come back with a token. **Logout is the hard part**, because three sessions exist at once:

1. The session at the **IdP** (Keycloak / Entra ID / Auth0)
2. The session at **app A** (your cookie)
3. Sessions at **apps B, C, D…** (same IdP)

Clearing app A's cookie does **not** log the user out — click login again and the IdP sees its own session still alive, so it silently signs you straight back in. The user believes they logged out. On a shared machine that's a real security hole.

##### Three flavors of logout

**a) Local logout** — clear only your app's cookie.

```csharp
await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
```

**b) RP-initiated logout (front-channel)** — send the user to the IdP so it can clean up:

```
GET https://idp.com/connect/endsession
    ?id_token_hint=eyJhbGciOi...        // proves "this is my session"
    &post_logout_redirect_uri=https://app.com/signed-out
    &state=xyz
```

The IdP kills its own session, then renders a page containing `<iframe>` elements pointing at each relying party's **front-channel logout URI**, so every app clears its own cookie. Finally it redirects the user to `post_logout_redirect_uri`.

In ASP.NET Core this is just:

```csharp
[HttpPost("logout")]
public IActionResult Logout() =>
    SignOut(
        new AuthenticationProperties { RedirectUri = "/signed-out" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);  // <- this produces the endsession request
```

The handler attaches `id_token_hint` automatically if you set `options.SaveTokens = true`.

**c) Back-channel logout** — the IdP **POSTs a `logout_token` (a JWT) server-to-server** to each app. No browser involved, no third-party-cookie blocking. This is the correct mechanism for modern systems.

```csharp
[HttpPost("/bff/backchannel-logout")]
[AllowAnonymous]
public async Task<IActionResult> BackchannelLogout([FromForm] string logout_token)
{
    // 1. Validate the JWT: signature (IdP JWKS), iss, aud, iat
    // 2. Required: an "events" claim containing
    //    "http://schemas.openid.net/event/backchannel-logout"
    // 3. Required: there must be NO "nonce" claim
    // 4. Read "sub" and/or "sid"
    var (sub, sid) = await _validator.ValidateAsync(logout_token);

    // 5. Kill the session in your own store
    await _sessionStore.RevokeAsync(sub, sid);
    await _refreshTokens.RevokeBySessionAsync(sid);

    return Ok();
}
```

##### Real-world problems you must handle
- **Stateless JWT apps**: back-channel logout can't invalidate a JWT already in the client's hands (JWTs aren't revocable). Solution: short access-token TTL (5–15 min) + **revoke the refresh token** + (if you need it tight) a `sid` denylist in Redis with TTL equal to the access-token lifetime.
- **Front-channel iframes get blocked** by third-party cookie restrictions (Safari ITP, Chrome). Prefer back-channel.
- **Idempotency**: the IdP may retry; the endpoint must tolerate repeated calls.
- **The logout endpoint must be POST.** If it's a GET, it's CSRF-able: `<img src="/logout">` logs people out.

##### Interview answer
> "SSO logout has to terminate three layers of session: the app, the IdP, and the other relying parties. Use RP-initiated logout against `end_session_endpoint` with `id_token_hint`, and implement back-channel logout to receive the `logout_token` and revoke the session and refresh tokens server-side. With stateless JWTs you can't revoke the access token directly, so it needs a short TTL."

---

### Logging and observability, practically

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Logging and observability, practically</p>

<p class="pd">Structured logs, scopes and attribute filtering, OpenTelemetry, and the specific case of auditing an SSO logout request end to end.</p>

<a id="be-q3"></a>

#### Auditing an SSO logout request end to end

A concrete case worth walking: single sign-out. The user clicks “log out”, and you need a durable record of what happened — because “I was logged out of everything unexpectedly” and “I logged out but my session stayed alive” are both security incidents.

``` text
single logout, and what to record at each hop

browser ──1──► your app  /logout
                 │   log: LogoutRequested {UserId, SessionId, TraceId, IP, UA}
                 ├──2──► revoke local session + refresh-token family
                 │       log: SessionRevoked {SessionId, TokenFamilyId, Count}
                 └──3──► redirect to IdP end_session_endpoint
                              ?id_token_hint=…&post_logout_redirect_uri=…

IdP ──4──► back-channel logout POST to every other app in the SSO group
                 │   log: BackChannelLogoutReceived {Sid, Issuer, Verified}
                 └── each app kills its own session

browser ◄─5── redirect back
                     log: LogoutCompleted {UserId, DurationMs, Outcome}
```

``` csharp
app.MapPost("/logout", async (HttpContext ctx, SessionService sessions,
                             ILogger<Program> log, CancellationToken token) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var sid    = ctx.User.FindFirstValue("sid");          // the IdP session id

    using var scope = log.BeginScope(new Dictionary<string, object?>
    {
        ["UserId"] = userId, ["Sid"] = sid,
        ["TraceId"] = Activity.Current?.TraceId.ToString(),
        ["Ip"] = ctx.Connection.RemoteIpAddress?.ToString(),
    });

    log.LogInformation("Logout requested from {UserAgent}",
                       ctx.Request.Headers.UserAgent.ToString());

    var revoked = await sessions.RevokeAllAsync(userId!, sid, token);
    log.LogInformation("Revoked {SessionCount} sessions and {TokenCount} refresh tokens",
                       revoked.Sessions, revoked.Tokens);

    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
    // ↑ the second one redirects to the IdP's end_session_endpoint
});

// the OTHER half: receiving a back-channel logout from the IdP
app.MapPost("/bff/backchannel-logout", async (HttpRequest req,
        LogoutTokenValidator validator, SessionService sessions,
        ILogger<Program> log, CancellationToken ct) =>
{
    var form = await req.ReadFormAsync(ct);
    var result = await validator.ValidateAsync(form["logout_token"]!, ct);

    if (!result.Valid)
    {
        log.LogWarning("Rejected back-channel logout: {Reason}", result.Reason);
        return Results.BadRequest();      // an unverified one is an attack
    }

    var n = await sessions.RevokeBySidAsync(result.Sid!, ct);
    log.LogInformation("Back-channel logout from {Issuer} revoked {Count} sessions",
                       result.Issuer, n);
    return Results.Ok();
});
```

> **\[Trap\]**
>
> **Common trap.** Log the *outcome*, not just the intent. The failure mode that matters is a logout that returned 200 while a refresh token survived somewhere — so record the revocation counts, and alert when a logout revokes zero tokens for a user who clearly had a session. Never log the `logout_token` or `id_token_hint` themselves: they are credentials.
