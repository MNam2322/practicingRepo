# Frontend: booking UI (React 19 + TypeScript + Vite)

This is the UI half. Read the root `../CLAUDE.md` first: it explains how to use the library in `../library`. The roadmap is in `TASKS.md`.

The goal is a **clean, fast, usable UI**, not a showpiece. The backend does the heavy lifting. **The frontend never decides business rules.** It shows what the API says, such as slot states, `canCancel` and error codes, and sends what the user chose.

## Code map

```
src/lib/api.ts          the only place that calls fetch; re-exports the generated types under short names; ApiError has status, code, traceId, fieldErrors
src/lib/api.generated.ts  GENERATED from backend/openapi/v1.json by openapi-typescript. Do not edit; run npm run gen:api:file
src/lib/queries.ts      TanStack Query: query keys, hooks (useBasics, useAvailability(ies), useMyBookings, useBooking),
                        mutations (useBook, useCancelBooking: optimistic) and what each one invalidates
src/lib/router.ts       hash routes: #/  #/r/:id[/:date][/week][?at=start]  #/mine   (add routes here; view and selection live in the URL)
src/components/Layout.tsx  top bar and page frame
src/components/Button.tsx, Card.tsx, Field.tsx, Notice.tsx (Notice, ErrorNotice), Skeleton.tsx (Slots/Bookings/Cards skeletons)
src/components/Toaster.tsx + src/lib/toast.ts   toasts: <Toaster> in main.tsx, useToast() anywhere
src/pages/Home.tsx      resources
src/pages/ResourcePage.tsx  header, Day/Week toggle, legend; resource/DayView.tsx (slot grid, booking panel), resource/WeekView.tsx (7 days × times)
src/pages/MyBookings.tsx    upcoming and past, cancel with confirm, rows link to the details page
src/pages/SignInPage.tsx    #/signin and #/signup (?next= an in-app route only: router.safeNext)
src/components/AccountMenu.tsx  top-bar Sign in link or account menu (useMe, sign out)
src/pages/BookingPage.tsx   #/b/:id details, status, cancel, Add to calendar (src/lib/calendar.ts builds the .ics from UTC instants)
src/lib/errors.ts       ERROR_MESSAGES: one friendly message per API error code
src/pages/resource/slots.ts  SLOT_LABEL (free, booked, past, closed) and closure time formatting
src/index.css           all styles; colour tokens on :root, dark mode below
```

## Rules

- **Times:** show `localStart` and `localEnd` from the API (venue time). Send `start` back exactly as the API gave it. Never compute slot times in the browser. "Today" is the venue's today, from `venueToday()`.
- **Errors:** switch on `ApiError.code` (`slot_taken`, `too_late_to_cancel`, …), not on message text. After `slot_taken`, refresh the slots. Friendly wording for each code lives only in `src/lib/errors.ts`; add new codes there. Show `ApiError.traceId` as a reference (ErrorNotice does).
- **Accounts:** `useMe()` is the signed-in account or null. Any request answered 401 `unauthenticated` sends the person to `#/signin?next=<where they were>` (lib/queries.ts); never build a redirect target from anything but `safeNext`.
- **State:** server data comes from the API through the hooks in `src/lib/queries.ts`; never fetch in a component or an effect. Every write is a mutation that invalidates exactly the query keys it affects. localStorage is only for small UI preferences, such as the remembered name.
- **Style:**
  - plain CSS in `src/index.css`
  - colour tokens only, never hard-coded colours
  - both light and dark themes
  - works at 390px wide
  - visible focus states
  - no CSS framework or component library unless a task says so
- **Components:** use `Button`, `Card`, `Field`, `Notice`/`ErrorNotice` and the skeletons from `src/components/` rather than writing their markup by hand. Loading states are skeletons at the size of the real content.
- **Accessibility:** use real buttons and links, labelled inputs, `aria-pressed` and `aria-current` where they apply, and keep everything usable with the keyboard alone.

## Commands (run from `frontend/`)

- `npm install` (once)
- `npm run dev`: http://localhost:5173. It needs the backend running.
- `npm run build`: type check plus production build
- `npm run lint`
- `npm run gen:api:file`: regenerate `src/lib/api.generated.ts` from `../backend/openapi/v1.json` (`npm run gen:api` reads the running API instead). openapi-typescript runs through npx, pinned, because it does not yet accept TypeScript 6 as a peer.
- `npm run test` (from task 11)
