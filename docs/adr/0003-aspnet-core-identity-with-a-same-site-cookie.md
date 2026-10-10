# ADR 0003: ASP.NET Core Identity with an HttpOnly same-site cookie

**Status:** Accepted (October 2026, backend tasks 7, 8 and 19)

## Context

Customers can book without an account; staff and admins need accounts with roles. The web app is a single-page React
app on the same origin as the API: through the Vite proxy in development, and served by the API in the Docker image.
Whatever proves who someone is must not be readable by JavaScript, because any cross-site-scripting bug would then turn
into stolen sessions (library 10.3 "Example 2: Tokens out of the browser with a BFF").

## Decision

- **ASP.NET Core Identity**, in the booking database: password hashing, lockout (five failures, five minutes), roles.
- **The session is a cookie**, `bk-auth`: HttpOnly, SameSite=Lax, Secure over HTTPS. No tokens in JavaScript or local
  storage.
- **Anonymous customers** get an HttpOnly `bk-customer` cookie with a random id. When they sign up or sign in, their
  bookings move to the account, through the aggregate.
- **Authorization:**
  - deny by default: a global filter requires a signed-in user unless an action says `[AllowAnonymous]`
  - roles Customer, Staff and Admin, checked through capability policies (`ViewSchedule`, `ManageVenue`), not role
    names in controllers (library 10.2)
  - `Accounts:Bootstrap` grants roles by email
- **Cross-site request forgery** is handled in two layers:
  - SameSite=Lax cookies
  - an antiforgery token on every write: the JS-readable `XSRF-TOKEN` cookie is echoed in `X-XSRF-TOKEN`, and the
    token is reissued on sign-in, sign-up and sign-out
  The only exempt endpoint is the signed payment webhook (library 10.4).
- Sign-in is rate limited per IP. Writes are rate limited per account, or per IP for anonymous callers, never per the
  client-sent customer cookie.

## Alternatives

- **JWT bearer tokens held by the SPA.** No CSRF problem, but the token sits in JavaScript's reach. An XSS bug means
  token theft, and revocation is hard.
- **An external OpenID Connect provider (Entra ID, Auth0, Keycloak).** Single sign-on and MFA for free, and the right
  move when the club needs them. For now it is another service to run and configure for a two-role app. Identity's cookie
  scheme can sit behind an OIDC login later without changing the API's authorization.
- **No accounts, only the anonymous cookie.** Not enough: staff and admin actions need a real identity and an audit
  trail.

## Consequences

- **+** Sessions cannot be stolen by script. The app and the API share an origin, so cookies just work, without CORS.
- **+** Authorization reads as capabilities and is tested per role.
- **−** CSRF is our problem. Every write needs the token, and every client (the SPA, tests, the k6 script) has to send
  it.
- **−** Cookies and antiforgery tokens are protected with Data Protection keys.
  - The Docker image keeps them on a volume.
  - Several instances need a shared key store (a database or blob storage) before scaling out.
- **−** We store password hashes and own lockout and reset flows; an external provider would take that on.
