# GymApp

A desktop gym tracker built with .NET 10 and Avalonia. Pick a training split, check in when you've trained, log your sets and PRs, and keep a streak going. Points, levels and a friends-only leaderboard make it a bit of a game.

## What it does

- **Splits and schedule.** Preset splits (Push/Pull/Legs, Upper/Lower, Full Body...) with a choice of start day, or your own custom split. Home shows today's workout.
- **Check-in.** "I went today" logs the workout once you've filled in your sets. It can be undone the same day.
- **Streaks.** Rest days count. A missed training day can be shifted, made up before the next rest day, or skipped, chosen the next day.
- **Exercises, sets and PRs.** A built-in exercise list plus your own. Planned warm-up and working sets per day, last session's sets as placeholders, PR history with an estimated 1RM.
- **Body data and calories.** Height, weight and activity level give a maintenance estimate and a suggested calorie goal. You log what you ate each day.
- **Points and levels.** Points for workouts, complete weeks and hitting your calorie target.
- **Gyms.** Gyms in your city come from OpenStreetMap, shown as a list or a map. You can add your own gyms and log which one you trained at.
- **Friends.** Friend requests and a weekly or all-time leaderboard with each friend's streak.

Everything is stored locally in SQLite. The only online parts are the gym search, city and address search, and the map tiles (OpenStreetMap). Everything else works offline.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.401 or later)
- macOS (Apple silicon) or Windows. Linux should work through Avalonia but isn't tested.

## Build, run and test

From the repository root:

```bash
dotnet build
```

```bash
dotnet run --project src/GymApp.Desktop
```

```bash
dotnet test
```

The database is created and migrated on first start, in:

- macOS: `~/Library/Application Support/GymApp/`
- Windows: `%LOCALAPPDATA%\GymApp\`

The same folder holds `remember.json` (the "remember me" sign-in) and `errors.log` (unexpected errors). To run against a copy of your data, set `GYMAPP_DATA_DIR` to another folder.

## TestBench (testing version)

```bash
dotnet run --project src/GymApp.TestBench
```

The same app plus an admin window: move the clock to any date, sign in as any user without a password, check in without logging sets, undo a check-in, bring back the missed-day card, add or set points (stored as "Manual adjustment" entries the sync keeps), rebuild the points ledger, and reset the test data.

It has its own data folder (`GymApp-TestBench/data` next to the real one), so it can run next to the real app and never changes the real data. The first start copies the real data; "Reset to empty" and "Copy real app's data" restart it. The clock offset is kept in `GymApp-TestBench/bench.json` across restarts.

## Publish

Self-contained builds, so the target machine doesn't need .NET installed.

macOS (Apple silicon):

```bash
dotnet publish src/GymApp.Desktop -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
```

Windows (x64):

```bash
dotnet publish src/GymApp.Desktop -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Start `publish/osx-arm64/GymApp.Desktop` or `publish\win-x64\GymApp.Desktop.exe`. The macOS build is a plain unsigned executable, not a signed `.app` bundle, so Gatekeeper may ask you to confirm the first time.

## Running the server

The social server (accounts, friends, the leaderboard and shared gym check-ins) is `src/GymApp.Server`, an ASP.NET Core API on Postgres. The app doesn't talk to it yet; that's Phase 14. See `docs/PHASE_13_SOCIAL_SERVER.md`.

Start a local Postgres (Docker), then the server. In Development it applies migrations on start and listens on `http://localhost:5080`:

```bash
docker compose up -d
```

```bash
dotnet run --project src/GymApp.Server
```

`curl http://localhost:5080/health` should answer `Healthy`.

The server tests start their own throwaway Postgres with Testcontainers, so Docker must be running, but `docker compose` isn't needed:

```bash
dotnet test tests/GymApp.Server.Tests
```

### In production

Build the image from the repository root:

```bash
docker build -f src/GymApp.Server/Dockerfile -t gymapp-server .
```

Settings come from environment variables only:

| Variable | What it is |
|---|---|
| `ConnectionStrings__Social` | The Postgres connection string |
| `AllowedHosts` | The host name(s) clients use, separated by `;`. Required |
| `ForwardedHeaders__KnownNetworks__0` (`__1`, …) | The proxy's network in CIDR form, e.g. `10.0.0.0/8`, if it isn't on the same machine. Only these may set the client IP |

HTTPS ends at the host's proxy; the container listens on plain HTTP on port 8080 and runs as a non-root user. Migrations never run on start: apply them first with the same image.

```bash
docker run --rm -e ConnectionStrings__Social="Host=...;Database=...;Username=...;Password=..." gymapp-server migrate
```

Outside Docker, the same is `dotnet GymApp.Server.dll migrate`.

## Project layout

| Project | What's in it |
|---|---|
| `src/GymApp.Core` | Pure logic with no database or HTTP: streaks, points, levels, PRs, calorie goals, parsing. Unit-tested |
| `src/GymApp.Data` | EF Core entities, `AppDbContext` and migrations (SQLite) |
| `src/GymApp.Services` | Services over the database, plus the OpenStreetMap clients (Overpass, Nominatim) |
| `src/GymApp.Desktop` | Avalonia views and view models (CommunityToolkit.Mvvm) |
| `src/GymApp.Social` | The friend and leaderboard rules and the `ISocialStore` interface, shared by the app and the server |
| `src/GymApp.Contracts` | Request and response records for the server's HTTP API |
| `src/GymApp.Server` | The social server: ASP.NET Core minimal API, `ServerDbContext` and migrations (Postgres) |
| `tests/GymApp.Tests` | xUnit tests. Service tests run against in-memory SQLite with the real migrations |
| `tests/GymApp.Server.Tests` | Endpoint tests against a real Postgres (Testcontainers) |
| `tests/Shared` | Test cases compiled into both test projects, e.g. the `FriendService` rules run against both stores |

Phase specs and design notes are in `docs/`.

### Adding a migration

```bash
dotnet ef migrations add <Name> --project src/GymApp.Data --startup-project src/GymApp.Desktop
```

For the server's database:

```bash
dotnet ef migrations add <Name> --project src/GymApp.Server --output-dir Data/Migrations
```

## Versioning

The version is set once in `Directory.Build.props` and shown at the bottom of Profile. See `CHANGELOG.md`.

- **1.0.x**: bug fixes only, with no new features and no schema changes.
- **1.x.0**: new features, migrations allowed.
- **2.0.0**: a breaking change, such as a migration that can't carry existing data forward or a change to seeded ids.

## Known limitations (1.0.0)

- Friends only work between accounts on the same database. There's no sync server yet.
- Points are computed on the client and can't be verified.
- Set values can be made up. The check-in rule only stops one-tap check-ins.
- The missed-day choice needs the app to be opened the next day. There are no notifications, so a user who doesn't open it loses the streak at midnight.
