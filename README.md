# Booking app

A booking app for a sports club's courts and rooms. Customers see free slots in the venue's local time, book, and cancel. The double-booking guard and the booking rules all live in the backend.

- **backend/**: a .NET 10 C# API. This is the heavy part.
- **frontend/**: React, TypeScript and Vite. A clean, usable UI.
- **library/**: the programming library, used as a read-only knowledge base. Claude Code looks things up in it before designing each feature.
- **PLAYBOOK.md**: the order to build the remaining features in, step by step.

The starter works today: resources, a day's slots, booking, My bookings, cancelling, light and dark themes, and a mobile layout. `PLAYBOOK.md` takes it from there: a real database, accounts, staff and admin tools, holds and payments, emails, tests, and Docker.

## 1. Install the tools (once)

### Windows

Open **PowerShell** and run:

```
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
winget install Git.Git
winget install Microsoft.VisualStudioCode
```

Then close PowerShell and open it again, so it finds the new tools.

### Mac

Install Homebrew from https://brew.sh if you don't have it, then in **Terminal** run:

```
brew install --cask dotnet-sdk
brew install node git
brew install --cask visual-studio-code
```

### Both

Check the versions. .NET must be 10.x and Node must be 22 or newer.

```
dotnet --version
node --version
git --version
```

Then install Claude Code by following https://docs.claude.com/en/docs/claude-code. Either use the VS Code extension, or run `claude` in the terminal.

You only need Docker Desktop later, from playbook step 11 (PostgreSQL).

## 2. Put the project in place

Unzip `booking-app.zip` where you keep your code, for example `D:\source\booking-app` on Windows or `~/source/booking-app` on a Mac. Then set your Git name once, if you haven't already:

```
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

The project is already a Git repository with one commit. It stays **local only**: there is no remote, and nothing is pushed anywhere.

## 3. Run it

You need two terminals. In VS Code, open the `booking-app` folder, then **Terminal → New Terminal**. The **+** button opens a second terminal.

**Terminal 1, the API:**

```
cd backend
dotnet run --project src/BookingApp.Api
```

Wait for `Now listening on: http://localhost:5080`.

**Terminal 2, the UI:**

```
cd frontend
npm install
npm run dev
```

You only need `npm install` the first time. Then open http://localhost:5173.

In Development the API books two demo slots per resource for tomorrow, so you can see booked slots straight away. Bookings are kept in memory until playbook step 7 adds a database, so they reset when the API restarts.

To stop either one, press **Ctrl+C** in its terminal.

## 4. Build the rest with Claude Code

Open Claude Code in the `booking-app` folder (the root, not `backend` or `frontend`), and say:

> Read PLAYBOOK.md and do the next step.

For each step, Claude Code:

1. searches the library for the topic
2. shows you a plan that names the library sections it used
3. waits for your OK
4. builds the change, tests it, ticks the step, and commits it locally

Each step in `PLAYBOOK.md` says what to check yourself afterwards. Then say "do the next step" again.

## Using the library yourself

From the repo root:

```
node library/find.mjs "time zones and DST"     # search: shows chapter, heading and line number
node library/show.mjs 1-2 1234                  # read one section
node library/show.mjs 7-4                       # list a chapter's sections
```

`library/quick-look.md` has a few lines on every chapter.

## Useful commands

| Where | Command | What it does |
|---|---|---|
| `backend/` | `dotnet build` | Build |
| `backend/` | `dotnet test` | Run the tests (from playbook step 1) |
| `backend/` | `dotnet format` | Fix the formatting |
| `frontend/` | `npm run build` | Type check and build |
| `frontend/` | `npm run lint` | Lint |
| root | `git log --oneline` | See what has been done |

`backend/http/booking.http` has one request per endpoint. In VS Code, install the **REST Client** extension, then click **Send Request** above any request.

## Troubleshooting

- **`dotnet` or `node` is "not recognized":** close and reopen the terminal, or VS Code, after installing.
- **Port 5080 or 5173 is in use:** an old copy is still running. Close its terminal, or restart VS Code.
- **The UI says it can't reach the API:** start the API in terminal 1 first.
- **`npm install` fails on Windows with a path error:** keep the folder path short, for example `D:\source\booking-app`.
