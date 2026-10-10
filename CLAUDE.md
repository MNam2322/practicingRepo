# Booking app

> **No git remote yet:** skip `git pull` and `git push` until one is added. Commit locally.

A court and room booking app for a sports club. Customers see free slots, book, and cancel. Staff and admin features, payments, reminders and more come from the task lists.

```
backend/    .NET 10 C# API (the heavy part).           Guide: backend/CLAUDE.md, roadmap: backend/TASKS.md
frontend/   React + TypeScript + Vite, a clean usable UI. Guide: frontend/CLAUDE.md, roadmap: frontend/TASKS.md
library/    A 950,000-word programming library. Read-only knowledge base: how to build this well.
PLAYBOOK.md The order to do the tasks in, with what the user checks after each one.
docs/adr/   Architecture decision records: why the big choices were made, and what else was considered.
```

## Build it the way the library says

The library is the team's engineering handbook. **Before designing any task, look up what it says and follow it.**

1. Search it with two or three queries for the task's topics:
   ```
   node library/find.mjs "overlapping time ranges"
   node library/find.mjs "idempotency key" --limit 5
   ```
2. Read the most relevant sections, and only those:
   ```
   node library/show.mjs 1-2 <line>       # line number from find.mjs
   node library/show.mjs 12-5 "Idempotency keys"
   ```
3. In your plan, list the sections you used (chapter number and heading) and what each one changed in the design. If you go against the library's advice, say so and why.

**Never read whole chapters, many chapter files, or `library/chapters/*` with a plain file read.** The library is 7 MB, far more than your context. Use `find.mjs` and `show.mjs`. Each task's "Read first" names good starting points.

### Where the library covers this app

| Concern | Read first |
|---|---|
| Time ranges and overlap | 1.2 "Intervals, overlaps, and scheduling" |
| Time zones, DST, `DateTimeOffset`, `TimeProvider` | 1.2 "Time zones and DST in practice", "DateTime is broken; use DateTimeOffset", "Dates, times and TimeProvider" |
| Booking lifecycle | 4.7 "Example 2: A booking lifecycle as a state machine"; 12.6 "State machines for aggregate lifecycles" |
| Aggregates and invariants | 12.6 "Aggregates: the transaction boundary" |
| No double bookings under concurrency | 7.4 "Optimistic and pessimistic concurrency"; 7.9 "Atomic conditional update"; 7.2 "Constraints: the integrity the application cannot enforce"; 7.3 "Specialised index types" (exclusion constraints) |
| Retries and duplicate requests | 12.5 "Idempotency keys — preventing double charges"; 8.2 "Idempotent HTTP APIs and safe retries" |
| Emails and reminders reliably | 12.3 "Outbox + inbox patterns"; 8.7 "Example 1: Confirmation emails lost on restart"; 4.3 "Example 2: Scoped services inside a background worker" |
| Payments and webhooks | 8.9 "Example 2: Receiving a payment webhook safely"; 14.2 "Receiving a webhook: verify, store, then work"; 4.9 the Adapter case |
| Validation and errors | 8.5 "Validation in ASP.NET Core"; 8.2 API design |
| Accounts and permissions | 10.2 "Authorization policies that scale" |
| Caching availability | 8.6 "Invalidation" |
| Rate limiting and abuse | 12.5 "Rate limiting"; 10.4 "Rate limiting (.NET 7+, built in)" |
| Tests | 4.6 "API integration tests"; 4.6 "TimeProvider — testing time-dependent code (.NET 8+)" |
| Live updates | 6.8 "Live updates: SignalR with a backplane and auth"; 9.7 "Live updates" |
| React | 9.4 Components and state; 9.5 "Effects, and when not to use them"; 9.7 Talking to the backend |
| Slow queries | 7.9 casebook; 7.3 Indexes and query plans |

## How the two halves connect

- The API runs at http://localhost:5080: `dotnet run --project backend/src/BookingApp.Api`.
- The frontend runs at http://localhost:5173: `cd frontend`, then `npm run dev`. Vite proxies `/api` to the API.
- **The contract** is `backend/src/BookingApp.Api/Contracts.cs`, published as `backend/openapi/v1.json` (a backend test fails if it drifts: regenerate with `UPDATE_OPENAPI=1 dotnet test`). The frontend types in `frontend/src/lib/api.generated.ts` are generated from it with `npm run gen:api:file` (or `npm run gen:api` with the API running). Never hand-write them. Change and regenerate both sides in the same task.
- A frontend task marked **Needs backend N** waits for that backend task. If it isn't ticked, stop and tell the user.

## Backend style

- The backend is layered **controller → service → repository** (see `backend/CLAUDE.md`). Controllers call services; services call repositories; controllers never touch a repository or a `DbContext`.
- Never use minimal APIs for application endpoints.

## Working rules

- **"Do the next step"** means: open `PLAYBOOK.md`, find the first step that isn't ticked, and run it as "How every step runs" there describes.
- **One task at a time.** Look up the library, plan, and wait for the user's OK before writing code.
- **After each task:**
  - build, test and lint the half you changed
  - try the change against the running app
  - tick the task in its `TASKS.md`, add one Changelog line, and tick the step in `PLAYBOOK.md`
  - commit with the prefix `backend:`, `frontend:` or `repo:`
- **Don't change unrelated code.** Never weaken a test to make it pass.
