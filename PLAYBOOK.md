# Playbook

This is the order to build the app in. Each step has a **prompt** to give Claude Code and a **check** for you to do afterwards.

The short version: open Claude Code in this folder and say

> Read PLAYBOOK.md and do the next step.

Claude Code finds the first step that isn't ticked, plans it, waits for your OK, builds it, and ticks it here.

## How every step runs

Claude Code does this for every step. You don't need to repeat it in the prompt.

1. Read the task in `backend/TASKS.md` or `frontend/TASKS.md`.
2. **Look it up in the library first:**
   - run two or three `node library/find.mjs "<topic>"` searches
   - read the best sections with `node library/show.mjs`
   - start with the task's **Read first** sections
3. Write a short plan:
   - the files it will change
   - the library sections it used, and what each one changed in the design
   - how it will test the change
4. **Stop and wait for your OK.**
5. Build it. Then:
   - **backend:** run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` in `backend/`
   - **frontend:** run `npm run build` and `npm run lint` in `frontend/`
6. Try the change against the running app.
7. Tick the task in its `TASKS.md`, add one Changelog line, and tick the step here.
8. Commit locally, with a message starting `backend:`, `frontend:` or `repo:`. **There is no remote, so don't pull or push.**

If a step says **Needs backend N** and that task isn't ticked, Claude Code stops and tells you.

### Running the app for your checks

You need two terminals:

```
cd backend
dotnet run --project src/BookingApp.Api
```

```
cd frontend
npm run dev
```

Then open http://localhost:5173. The API itself is at http://localhost:5080.

### When something goes wrong

- **The plan looks too big:** say "Split this into smaller steps and do the first one."
- **A check fails:** paste what you saw, and say "This check failed. Find the cause before changing code."
- **You want to undo a step:** run `git log --oneline` to find it, then ask Claude Code to revert that commit.
- **A long session gets slow or confused:** start a new Claude Code session with `/clear`, then "Read PLAYBOOK.md and do the next step." Everything it needs is in the files.

---

## Step 0: Check the setup
- [x] **Prompt:** "Check this repo builds. In `backend/` run `dotnet build`. In `frontend/` run `npm install`, then `npm run build`. Run `node library/find.mjs "booking state machine"` to check the library works. Don't change any files. Report what happened."
- **Check:**
  - start both halves as above and open http://localhost:5173
  - pick a court, pick tomorrow: some slots are already booked (demo data)
  - book a free slot, then find it under **My bookings** and cancel it

## Phase 1: Safety net

### Step 1: Backend 1, tests
- [x] **Prompt:** "Do backend task 1 (tests)."
- **Check:**
  - `dotnet test` in `backend/` shows every test passing
  - the parallel booking test is there, and the DST tests use `Europe/London`
  - ask: "Break `TimeRange.Overlaps` on purpose, run the tests, show me which fail, then undo it."

### Step 2: Backend 2, OpenAPI
- [x] **Prompt:** "Do backend task 2 (OpenAPI and API docs)."
- **Check:** with the API running, open http://localhost:5080/scalar. Every endpoint is listed with its errors. Try `GET /api/resources` from that page.

### Step 3: Backend 3, validation
- [x] **Prompt:** "Do backend task 3 (validation)."
- **Check:** in `backend/http/booking.http`, send a booking with an empty name. It should come back 400 with a field error for `name`. A booking too far ahead should still be 422 `too_far_ahead`.

### Step 4: Frontend 1, UI foundation
- [x] **Prompt:** "Do frontend task 1 (UI foundation)."
- **Check:** the pages look the same or better. You see grey skeletons while slots load (throttle the network in browser dev tools to see them). A toast appears after booking and after cancelling.

### Step 5: Frontend 2, better availability
- [x] **Prompt:** "Do frontend task 2 (better availability)."
- **Check:**
  - the legend and the week view are there
  - open the same court in two tabs, book a slot in one, then switch to the other: the slot shows as booked within a minute

### Step 6: Frontend 3, booking details
- [ ] **Prompt:** "Do frontend task 3 (booking details page)."
- **Check:** after booking, the link opens `#/b/<id>`. "Add to calendar" downloads an `.ics` file that opens in your calendar app at the right local time.

## Phase 2: A real database

### Step 7: Backend 4, EF Core with SQLite
- [ ] **Prompt:** "Do backend task 4 (EF Core with SQLite)."
- **Check:** book a slot, stop the API (Ctrl+C), and start it again: the booking is still there. `dotnet test` runs the parallel test on SQLite too.

### Step 8: Backend 5, resources and closures in the database
- [ ] **Prompt:** "Do backend task 5 (resources and opening hours in the database, with closures)."
- **Check:** ask Claude Code to add a closure for one court tomorrow. That day shows no free slots for that court in the UI, and booking it through `booking.http` is refused with a clear code.

### Step 9: Frontend 4, TanStack Query
- [ ] **Prompt:** "Do frontend task 4 (TanStack Query)."
- **Check:** booking and cancelling still work, and My bookings updates without a page reload. Cancel feels instant.

### Step 10: Frontend 5, generated API types
Needs backend 2.
- [ ] **Prompt:** "Do frontend task 5 (generated API types)."
- **Check:** with the API running, `npm run gen:api` then `npm run build` both pass. `src/lib/api.ts` no longer hand-writes the response types.

### Step 11: Backend 6, PostgreSQL
You need Docker Desktop installed and running for this step.
- [ ] **Prompt:** "Do backend task 6 (PostgreSQL with an exclusion constraint)."
- **Check:**
  - `docker compose up -d` starts PostgreSQL
  - the API runs with `Storage:Provider` set to `Postgres`
  - `dotnet test` passes, including the Testcontainers tests
  - ask: "Show me the constraint in the migration, and prove it is the database, not app code, that blocks the double booking."

## Phase 3: Accounts, staff and admin

### Step 12: Backend 7, accounts
- [ ] **Prompt:** "Do backend task 7 (accounts)."
- **Check:** in `booking.http`, sign up, sign in, and call `GET /api/me`. A booking made before signing in now belongs to the account.

### Step 13: Frontend 6, sign in
Needs backend 7.
- [ ] **Prompt:** "Do frontend task 6 (sign in)."
- **Check:** book while signed out, then sign up. The message says your bookings moved, and they're in My bookings. Sign out and in again: they're still there.

### Step 14: Backend 8, staff features
- [ ] **Prompt:** "Do backend task 8 (staff features)."
- **Check:** ask Claude Code to give your account the `Staff` role. Then the day schedule works and "mark no-show" works on a past booking. A customer account gets 403 on these endpoints.

### Step 15: Frontend 7, staff schedule
Needs backend 8.
- [ ] **Prompt:** "Do frontend task 7 (staff schedule)."
- **Check:** as staff, the schedule shows every resource for the day. Block an hour for maintenance, then check that customers can't book it.

### Step 16: Backend 9, admin features
- [ ] **Prompt:** "Do backend task 9 (admin features)."
- **Check:** as admin, change a court's opening hours through `booking.http`. Availability changes, and the audit log shows who changed it.

### Step 17: Frontend 8, admin pages
Needs backend 9.
- [ ] **Prompt:** "Do frontend task 8 (admin pages)."
- **Check:** add a new resource and a holiday closure from the UI. Submit a form with a bad value: the error shows next to the right field.

## Phase 4: Real-world booking features

### Step 18: Backend 10, idempotent booking
- [ ] **Prompt:** "Do backend task 10 (idempotent booking requests)."
- **Check:** send the same booking twice in `booking.http` with the same `Idempotency-Key`. You get the same booking back both times and only one booking exists.

### Step 19: Backend 11, holds
- [ ] **Prompt:** "Do backend task 11 (holds that expire)."
- **Check:**
  - create a hold: the slot shows as taken
  - ask Claude Code to shorten the hold to one minute in Development settings
  - wait: the slot frees up by itself

### Step 20: Backend 12, payments
- [ ] **Prompt:** "Do backend task 12 (payments with a fake provider)."
- **Check:**
  - confirming a hold takes a deposit
  - sending the same webhook twice changes nothing the second time
  - a webhook with a bad signature is refused
  - cancelling early refunds the deposit; cancelling after the cutoff doesn't

### Step 21: Frontend 9, holds and payment step
Needs backend 11 and 12.
- [ ] **Prompt:** "Do frontend task 9 (holds and payment step)."
- **Check:** choose a slot and watch the countdown. Pay, and see the confirmation. Try again but let it expire: the message explains what happened.

### Step 22: Backend 13, notifications
- [ ] **Prompt:** "Do backend task 13 (notifications with an outbox)."
- **Check:** book and cancel, then open the email folder the fake sender writes to. Both `.eml` files are there. Stop the API mid-send and restart it: no email is lost or sent twice.

### Step 23: Backend 14, recurring bookings
- [ ] **Prompt:** "Do backend task 14 (recurring bookings)."
- **Check:** book a weekly series where one date is already taken. The result lists that date as failed and books the rest.

### Step 24: Backend 15, waitlist
- [ ] **Prompt:** "Do backend task 15 (waitlist)."
- **Check:** with two customers, the second joins the waitlist for a booked slot. The first cancels, and an email for the second appears in the email folder.

### Step 25: Frontend 10, recurring and waitlist
Needs backend 14 and 15.
- [ ] **Prompt:** "Do frontend task 10 (recurring bookings and waitlist)."
- **Check:** "Repeat weekly" shows which dates failed. "Join waitlist" appears on booked slots and confirms you joined.

## Phase 5: Quality

### Step 26: Backend 16, caching
- [ ] **Prompt:** "Do backend task 16 (caching availability)."
- **Check:** `dotnet test` includes a test showing availability is never stale after booking or cancelling.

### Step 27: Backend 17, observability
- [ ] **Prompt:** "Do backend task 17 (observability)."
- **Check:** ask Claude Code how to see the traces locally (for example the .NET Aspire dashboard in Docker). A booking shows up as one trace from the HTTP request to the database, and the `slot_taken` counter goes up when you double book.

### Step 28: Backend 18, performance
- [ ] **Prompt:** "Do backend task 18 (performance)."
- **Check:** Claude Code shows before and after timings and query plans. The load test shows exactly one success per slot.

### Step 29: Backend 19, security
- [ ] **Prompt:** "Do backend task 19 (security hardening)."
- **Check:** ask: "List the security headers on a response, show what happens to a write with no antiforgery token, and show where the secrets now live."

### Step 30: Frontend 11, tests and accessibility
- [ ] **Prompt:** "Do frontend task 11 (tests and accessibility)."
- **Check:** `npm run test` passes, and the Playwright test passes against the running app. Try booking with only the keyboard (Tab, Enter, Space).

## Phase 6: Delivery

### Step 31: Backend 20, Docker
You need Docker Desktop for this step.
- [ ] **Prompt:** "Do backend task 20 (Docker)."
- **Check:** stop everything else. `docker compose up --build` from the repo root starts the whole app, and http://localhost:8080 (or the port Claude Code tells you) shows the UI and books.

### Step 32: Frontend 12, production build
Needs backend 20.
- [ ] **Prompt:** "Do frontend task 12 (production build)."
- **Check:** the README explains how the image serves the frontend. Refreshing on a page like `#/mine` still works inside the container.

### Step 33: Backend 21, CI
Do this only after you add a GitHub remote. When you do, tell Claude Code to remove the "No git remote yet" note from `CLAUDE.md`.
- [ ] **Prompt:** "Do backend task 21 (CI)."
- **Check:** push, then open the Actions tab on GitHub. The workflow is green.

### Step 34: Backend 22, architecture guard rails
- [ ] **Prompt:** "Do backend task 22 (architecture guard rails)."
- **Check:** ask Claude Code to add a reference from Core to EF Core on purpose: the architecture tests fail. Then undo it. Read the ADRs in `docs/adr/`: each one says what was chosen, what else was considered, and why.

---

When every step is ticked, you have a booking app with:

- a real database that refuses double bookings by itself
- accounts, staff and admin tools
- holds, deposits, emails, recurring bookings and a waitlist
- tests, monitoring and a Docker image

Every design choice in it is traced back to a section of the library.
