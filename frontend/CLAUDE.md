# Frontend: booking UI (React 19 + TypeScript + Vite)

This is the UI half. Read the root `../CLAUDE.md` first: it explains how to use the library in `../library`. The roadmap is in `TASKS.md`.

The goal is a **clean, fast, usable UI**, not a showpiece. The backend does the heavy lifting. **The frontend never decides business rules.** It shows what the API says, such as slot states, `canCancel` and error codes, and sends what the user chose.

## Code map

```
src/lib/api.ts          the only place that calls fetch; types mirror backend Contracts.cs; ApiError has status + code
src/lib/useApi.ts       tiny load/reload hook (replaced by TanStack Query in task 4)
src/lib/router.ts       hash routes: #/  #/r/:id[/:date]  #/mine   (add routes here)
src/components/Layout.tsx  top bar, Loading, ErrorBox
src/pages/Home.tsx      resources
src/pages/ResourcePage.tsx  day picker, slot grid, booking panel
src/pages/MyBookings.tsx    upcoming and past, cancel with confirm
src/index.css           all styles; colour tokens on :root, dark mode below
```

## Rules

- **Times:** show `localStart` and `localEnd` from the API (venue time). Send `start` back exactly as the API gave it. Never compute slot times in the browser. "Today" is the venue's today, from `venueToday()`.
- **Errors:** switch on `ApiError.code` (`slot_taken`, `too_late_to_cancel`, …), not on message text. After `slot_taken`, refresh the slots.
- **State:** server data comes from the API. localStorage is only for small UI preferences, such as the remembered name.
- **Style:**
  - plain CSS in `src/index.css`
  - colour tokens only, never hard-coded colours
  - both light and dark themes
  - works at 390px wide
  - visible focus states
  - no CSS framework or component library unless a task says so
- **Accessibility:** use real buttons and links, labelled inputs, `aria-pressed` and `aria-current` where they apply, and keep everything usable with the keyboard alone.

## Commands (run from `frontend/`)

- `npm install` (once)
- `npm run dev`: http://localhost:5173. It needs the backend running.
- `npm run build`: type check plus production build
- `npm run lint`
- `npm run test` (from task 11)
