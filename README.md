# Booking app

A booking app for a sports club's courts and rooms. Customers see free slots in the venue's local time, hold a slot,
pay a deposit and get a confirmation email. They can cancel, book weekly series, and join a waitlist. Staff run the
day's schedule; admins manage courts, opening hours, closures and booking rules.

- **backend/**: a .NET 10 C# API (ASP.NET Core controllers, EF Core, SQLite or PostgreSQL)
- **frontend/**: React 19, TypeScript and Vite
- **docs/adr/**: why the big design choices were made
- **library/**: a read-only programming handbook that the design was built from

Payments and email are **fakes** for development. No real money moves: a fake provider takes test cards, and emails
are written as `.eml` files.

---

## 1. Install the tools (once)

| Tool | Version | Needed for |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0.100 or newer | the backend |
| [Node.js](https://nodejs.org) | 22 or newer | the frontend |
| [Git](https://git-scm.com) | any | getting the code |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | any recent | PostgreSQL, the PostgreSQL tests, and running everything as one image |

Without Docker the app still runs: development uses SQLite, a database in a single file, and the PostgreSQL tests are
skipped.

**Windows** (PowerShell):

```
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
winget install Git.Git
winget install Docker.DockerDesktop
```

**Mac** (with [Homebrew](https://brew.sh)):

```
brew install --cask dotnet-sdk docker
brew install node git
```

Close and reopen the terminal, then check the versions:

```
dotnet --version     # 10.x
node --version       # v22 or newer
docker --version
```

## 2. Get the code

```
git clone https://github.com/MNam2322/practicingRepo.git booking-app
cd booking-app
git switch dev
```

Day-to-day work happens on `dev` (see [Branches and CI](#7-branches-and-ci)).

---

## 3. Run it

There are two ways. **A** is the quickest; **B** is what you use while changing code.

### A. Everything in Docker (one command)

Start Docker Desktop, then from the repository root:

```
docker compose up --build
```

Open **http://localhost:8080**. One container holds the API and the built web app, next to a PostgreSQL container.

| Command | What it does |
|---|---|
| `docker compose up --build` | build the image and start everything |
| `docker compose up -d` | start in the background, without rebuilding |
| `docker compose logs -f app` | follow the app's logs |
| `docker compose down` | stop (the data is kept) |
| `docker compose down -v` | stop and delete all data (database, emails, sign-in keys) |

The first build takes a few minutes. Later builds reuse the cached layers.

### B. Development mode (two terminals)

**Terminal 1, the API:**

```
cd backend
node tools/dev-secrets.mjs                  # once per machine: puts the development secrets in dotnet user-secrets
dotnet run --project src/BookingApp.Api
```

Wait for `Now listening on: http://localhost:5080`.

**Terminal 2, the web app:**

```
cd frontend
npm install                                 # first time, and after package.json changes
npm run dev
```

Open **http://localhost:5173**. Vite forwards every `/api` request to the API on port 5080. If 5173 is busy, Vite picks
the next free port and prints it.

Press **Ctrl+C** in a terminal to stop that half.

**Useful in development:**

- **The API reference:** http://localhost:5080/scalar lists every endpoint, and you can try them from the page.
- **Hand-written requests:** `backend/http/booking.http` has one request per endpoint. Use it with the VS Code
  **REST Client** extension.
- **Demo data:** demo bookings are created for today and tomorrow, so some slots are already booked.

### Use PostgreSQL instead of SQLite in development

```
docker compose up -d postgres                                   # repository root: only the database
cd backend
dotnet run --project src/BookingApp.Api --launch-profile http-postgres
```

The frontend is unchanged. `docker compose stop postgres` stops the database.

---

## 4. Build

### Backend

```
cd backend
dotnet build                                # Debug build; warnings are errors
dotnet build -c Release                     # Release build
dotnet publish src/BookingApp.Api -c Release -o out   # a deployable folder
```

### Frontend

```
cd frontend
npm ci                                      # clean install, exactly from package-lock.json
npm run build                               # type check, then a production build in frontend/dist
npm run preview                             # serve dist locally (it still forwards /api to port 5080)
```

### The Docker image

```
docker build -t booking-app:local .         # repository root
```

The `Dockerfile` has three stages:

1. Node builds the web app.
2. The .NET SDK publishes the API.
3. The final image is the small "chiseled" ASP.NET runtime, running as a non-root user. The web build sits in the
   API's `wwwroot`.

See [How the image serves the web app](#how-the-image-serves-the-web-app).

---

## 5. Test and check

| Where | Command | What it runs |
|---|---|---|
| `backend/` | `dotnet test` | every backend test (unit, repository and API tests) |
| `backend/` | `dotnet format --verify-no-changes` | the formatting check (run `dotnet format` to fix) |
| `frontend/` | `npm run lint` | the linter |
| `frontend/` | `npm run build` | the type check and build |
| `frontend/` | `npm run test` | component tests (Vitest, with the API mocked) |
| `frontend/` | `npm run e2e` | browser tests (Playwright) against the running app |

**Notes:**

- **PostgreSQL tests need Docker.** They start a throwaway PostgreSQL through Testcontainers. Without Docker they're
  skipped; with `REQUIRE_DOCKER=1` a skip counts as a failure (CI sets this).
- **Browser tests need a browser.** Run `npx playwright install chromium` once. Start the app first: development mode
  (port 5173), or set `E2E_BASE_URL=http://localhost:8080` to test the Docker app.
- **After an intended API change,** regenerate the contract and the frontend types:

  ```
  cd backend
  UPDATE_OPENAPI=1 dotnet test        # rewrites openapi/v1.json (PowerShell: $env:UPDATE_OPENAPI=1; dotnet test)
  cd ../frontend
  npm run gen:api:file                # regenerates src/lib/api.generated.ts from it
  ```

---

## 6. Using the app

### Ports

| Address | What |
|---|---|
| http://localhost:8080 | the whole app, in Docker |
| http://localhost:5173 | the web app, in development |
| http://localhost:5080 | the API, in development (`/scalar` is the API reference) |
| `localhost:5432` | PostgreSQL in Docker (database `booking`, user `booking`, password `booking-dev-only`) |
| http://localhost:18888 | the Aspire dashboard for traces, metrics and logs (optional, see below) |

The PostgreSQL password is a local default from `docker-compose.yml`. Anywhere other than your own machine, set
`POSTGRES_PASSWORD` and `WEBHOOK_SECRET` in the environment, or in a `.env` file (which git ignores).

### Test cards (the fake payment provider)

| Card token | Result |
|---|---|
| `tok_visa` | approved |
| `tok_declined` | declined |
| `tok_insufficient_funds` | declined: not enough funds |
| `tok_expired_card` | declined: expired card |
| `tok_unavailable` | the provider is down; nothing is charged |
| `tok_timeout` | charged, but the answer is lost; retrying finds the charge instead of charging twice |

### Emails

| Run | Where the `.eml` files go |
|---|---|
| development | `backend/src/BookingApp.Api/.data/emails/` |
| Docker | the `app-data` volume, at `/data/emails` |

Open them with any mail program.

### Staff and admin accounts

1. Sign up in the app.
2. Add your email to `Accounts:Bootstrap:Staff` or `Accounts:Bootstrap:Admin` in
   `backend/src/BookingApp.Api/appsettings.Development.json`.
3. Restart the API and sign in again. The **Schedule** (staff) and **Admin** links appear in the top bar.

### Looking inside the database

**PostgreSQL**, from the terminal:

```
docker compose exec postgres psql -U booking -d booking
```

Then `\dt` lists the tables and `\d bookings` describes one. Or use a GUI such as DBeaver, pgAdmin or the VS Code
PostgreSQL extension, with the settings in the ports table.

**SQLite** (development): open `backend/src/BookingApp.Api/.data/bookings.db` with the VS Code **SQLite Viewer**
extension or **DB Browser for SQLite**. Times are stored as integers (UTC ticks). To start with an empty database, stop
the API and delete the `.data` folder.

### Traces and metrics (optional)

```
docker compose --profile observability up -d aspire-dashboard
```

Then run the API with the endpoint set:

```
# PowerShell
$env:OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"; dotnet run --project src/BookingApp.Api
# bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project src/BookingApp.Api
```

Open http://localhost:18888. Every request shows up as a trace, with the SQL underneath it.

---

## 7. Branches and CI

- **Branches:** work happens on `dev`. `main` only moves forward by merging `dev` once CI is green.
- **CI** (`.github/workflows/ci.yml`, the **Actions** tab on GitHub) runs on every push to `main` or `dev`, and on every
  pull request:
  1. **Backend:** build, the format check, then the tests, including PostgreSQL.
  2. **Frontend:** lint, build, component tests.
  3. **Docker image:** built once, started with PostgreSQL, then tested in a real browser.
- **Protecting `main`:** to have GitHub enforce this, go to **Settings → Branches → Add rule** for `main`. Require a
  pull request, and require the **Backend**, **Frontend** and **Docker image** checks to pass.

---

## 8. Project layout

```
backend/
  src/BookingApp.Core/            business rules and use cases (no database or web code)
  src/BookingApp.Infrastructure/  database (EF Core), fake payments and email, background jobs
  src/BookingApp.Api/             HTTP: controllers, security, serving the web app
  tests/                          unit, repository and API tests
  openapi/v1.json                 the API contract (generated; a test checks it)
  http/booking.http               one request per endpoint
  tools/                          dev-secrets.mjs, payment-webhook.mjs, the performance tool
  docs/performance.md             query measurements
frontend/
  src/pages/                      screens
  src/components/                 shared UI pieces
  src/lib/                        API client, data hooks (TanStack Query), router, error messages
  e2e/                            Playwright tests
docs/adr/                         architecture decision records
Dockerfile, docker-compose.yml    the image, and the app with PostgreSQL
```

The conventions for changing code are in `CLAUDE.md`, `backend/CLAUDE.md` and `frontend/CLAUDE.md`. The history of
what was built, task by task, is in `PLAYBOOK.md` and the two `TASKS.md` files.

---

## How the image serves the web app

In development there are two servers: Vite serves the React app on 5173 and forwards `/api` to the API on 5080. In the
image there is only one, the API, and no Node at all.

1. **Build.** A Node stage runs `npm ci` and `npm run build`. That writes `frontend/dist`: an `index.html`, plus
   JavaScript and CSS in `assets/`, each with a content hash in its name (such as `index-DkkM4mBp.js`).
2. **Copy.** The final stage puts `dist` into the API's `wwwroot` folder.
3. **Serve.** At start-up the API sees `wwwroot` (`backend/src/BookingApp.Api/WebApp.cs`) and serves it.
   - `/` returns `index.html`.
   - `/assets/*` files are sent with `Cache-Control: public, max-age=31536000, immutable`. Their names change whenever
     their content does, so browsers can keep them for a year.
   - `index.html` is sent with `no-cache`, so a new release shows up immediately.
   - The page's Content-Security-Policy allows only same-site scripts and styles, so keep `index.html` free of inline
     scripts.
4. **Refreshing works.** The app's routes are after the `#` (`/#/mine`, `/#/b/<id>`), and the browser never sends that
   part to the server. Refreshing `/#/mine` asks for `/`, gets `index.html`, and the app shows My bookings again.
5. **Other paths.**
   - Any other non-API path also gets `index.html`, so an old or mistyped link lands in the app.
   - Unknown `/api/...` paths still answer with a JSON 404 (`code: not_found`).
6. **One origin.** The page and the API share an origin, so cookies (sign-in, the customer cookie, antiforgery) work
   exactly as they do through the Vite proxy, with no CORS involved.

---

## Troubleshooting

- **`dotnet`, `node` or `docker` is "not recognized":** close and reopen the terminal, or VS Code, after installing.
- **Port 5080, 5173, 8080 or 5432 is in use:** an old copy is still running. Stop it, or run `docker compose down` if
  it's the Docker app.
- **The web app says it can't reach the API:** start the API in terminal 1 first.
- **Writes fail with `antiforgery_failed`:** reload the page to get a fresh token. After a sign-in, the app does this by
  itself.
- **The PostgreSQL tests are skipped:** start Docker Desktop.
- **`npm install` fails on Windows with a path error:** keep the folder path short, for example `D:\source\booking-app`.
- **Docker runs out of disk space:** `docker system prune` removes old images and build cache. Add `--volumes` only if
  you also want to delete the data.

## The library

From the repository root:

```
node library/find.mjs "time zones and DST"     # search: shows the chapter, heading and line number
node library/show.mjs 1-2 1234                  # read one section
```
