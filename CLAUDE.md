# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

GainStreak is a gym tracker (.NET 10, Avalonia 12) with a small social server. The code keeps its working name: projects, namespaces, the data folder and the `GYMAPP_*` settings are all "GymApp". Use "GainStreak" only in user-facing text.

## Commands

```bash
dotnet build                                   # whole solution (GymApp.slnx)
dotnet test                                    # all tests; server tests need Docker running (Testcontainers)
dotnet test tests/GymApp.Tests                 # app-side tests only, no Docker
dotnet test tests/GymApp.Tests --filter "FullyQualifiedName~StreakServiceTests"   # one class
dotnet test tests/GymApp.Tests --filter "FullyQualifiedName~StreakServiceTests.MethodName"
dotnet run --project src/GymApp.Desktop        # the app (Debug talks to http://localhost:5080)
dotnet run --project src/GymApp.TestBench      # app + admin window (clock shift, sign in as, reset data)
docker compose up -d && dotnet run --project src/GymApp.Server   # local Postgres + server; applies migrations on start in Development
```

There is no separate lint step. `GYMAPP_DATA_DIR` points the app at another data folder; `GYMAPP_SERVER_URL` overrides the server address.

Migrations (two databases, two commands):

```bash
dotnet ef migrations add <Name> --project src/GymApp.Data --startup-project src/GymApp.Desktop      # app SQLite
dotnet ef migrations add <Name> --project src/GymApp.Server --output-dir Data/Migrations            # server Postgres
```

After any change to the app's entities or `OnModelCreating`, regenerate the compiled model the app runs on (iOS can't build the model at runtime). `CompiledModelTests` fails until you do:

```bash
dotnet ef dbcontext optimize --project src/GymApp.Data --startup-project src/GymApp.Desktop --output-dir CompiledModels
```

The production server never migrates on start: `docker run ... gymapp-server migrate` (or `dotnet GymApp.Server.dll migrate`) must run before deploying a schema change. Desktop releases: `build/release.sh` then `build/upload-github.sh` (see README "Releases for testers").

## Architecture

**Local-first app, social-only server.** Workouts, sets, PRs, body data and calories live only on the device in SQLite (`GymApp.Data`, `AppDbContext`, created and migrated on first start). The server (`GymApp.Server`, ASP.NET Core minimal API on Postgres, `ServerDbContext`) holds accounts, friendships, uploaded stats (username, points, level, streak), shared gym check-ins, shares (splits and workouts sent to friends) and feedback. Accounts are owned by the server; the app keeps a session token in `session.token` in its data folder.

**Layering.** Dependencies point downward. Only `GymApp.App`, the heads (`GymApp.Desktop`, later `GymApp.Android` and `GymApp.iOS`) and `GymApp.TestBench` reference Avalonia; keep it that way so every head can reuse the rest.
- `GymApp.Core`: pure rules (streaks, points, levels, PRs, calorie goals) and `AppClock`.
- `GymApp.Social`: friend and leaderboard rules plus the interfaces the view models use (`IFriendService`, `ILeaderboardService`, `IFriendCheckInSource`, `ISocialStore`). The same rules back both the TestBench's local mode and the server.
- `GymApp.Contracts`: request/response records shared by server and client. A change here is a wire change.
- `GymApp.Client`: `SocialApiClient`, which returns Ok / Refused / Offline results; a 10-second timeout counts as offline.
- `GymApp.Services`: services over `AppDbContext`, the OpenStreetMap clients, and the app's side of the server: `Remote*Service` implementations with an offline cache (`SocialCacheStore`), and `SocialSync`, which uploads stats and check-ins. Services report changes through `ISocialSyncHooks`; uploads wait while offline.
- `GymApp.App`: the UI every head shares: `GymApplication` (the Avalonia application), `AppServices`, views and view models (CommunityToolkit.Mvvm). What differs per head comes in through `IPlatform` (data folder, token store, server address, log folder, windows, updates); never ask the system directly from here.
- `GymApp.iOS.LiveActivity`: Swift, no Xcode project. `build.sh` (run by the iOS head's build) makes the widget extension that draws the workout's Live Activity and the bridge framework `IosLiveActivity` calls; the app side is `ILiveActivity`, driven by `LiveWorkoutWatcher`.
- Apple Health (Phase 20): `IHealthStore` from the head (`IosHealthStore`). `HealthSync` reconciles the last 30 days with Health on foreground and background, and reads weigh-ins, profile values and steps (`HealthImportService`). After changing the iOS head's `Info.plist` or `Entitlements.plist`, delete `obj/<config>/net10.0-ios/<rid>/AppManifest.plist` and `nativelibraries/GymApp.iOS`: incremental builds keep both stale.
- `GymApp.Desktop`: the desktop head: `Program`, `MainWindow` (hosts the shared `MainView`), `DesktopPlatform`, Velopack updates and the macOS Dock icon.

**Two wirings of the same app.** `AppServices.Create(localSocial, platform)` in `GymApp.App` registers either the server-backed `Remote*` services or the `Local*` ones (accounts and friends on this computer, used by the TestBench's "Local" social mode). A new social feature usually needs both a `Remote*` and a `Local*` implementation of its interface, registered in both branches.

**Time.** Use `AppClock.Now`/`AppClock.Today` (or an injected `TimeProvider`), never `DateTime.Now`, so the TestBench can shift the clock. The server rejects client dates more than 2 days from today (`ClientDates`).

**Old clients stay in the wild.** Installed copies update through Velopack only when they restart. The server's `MinClientVersion` gates old builds with a 426. Keep `Contracts` and endpoint changes backward compatible, or plan a `MinClientVersion` bump. `ClientContractTests` and `ClientVersionTests` cover this.

**UI.** `ViewLocator` maps `FooViewModel` to `FooView` by name, so a new screen needs both with matching names and namespaces. Colors and styles are tokens in `Views/IronlogResources.axaml` / `IronlogStyles.axaml`, with light and dark variants. Reuse `Views/SegmentedPill` for segmented toggles and `ResponsiveGrid` for adaptive layouts.

**Tests.**
- `tests/GymApp.Tests`: service tests run on in-memory SQLite with the real migrations. The server side runs against `FakeHttpHandler` / `FakeSocialServer`.
- `tests/GymApp.Server.Tests`: endpoint tests on a real Postgres (Testcontainers). End-to-end tests run the real app client and services, one simulated `Phone` per user, against the real server.
- `tests/Shared`: compiled into both projects, so rules like `FriendService` run against both stores.

## Versioning and changelog

The version is set only in `Directory.Build.props`. Before 1.0 it is `0.<phase>.<fix>`: the phase number matches the implementation plan in `docs/`. New features and migrations open a new phase (`0.N.0`); `0.N.x` is fixes with no schema changes. Every user-visible change gets an entry in `CHANGELOG.md` in the existing style: bold lead-ins, written for testers, ending with a `**Migration:**` line naming the database when there is one. Commit subjects start with the version, e.g. `0.15.6: ...`.

## Phase specs

`docs/PHASE_*.md` are specs written for Claude Code; follow the current one when working on its phase. Phase 16 (`docs/PHASE_16_MOBILE_APP.md`, the iOS and Android heads, with a shared `GymApp.App` library) is in progress; the 16a spike is done (its results are in the spec). Its ground rules include:
- Do not commit or push.
- The desktop app must look and behave exactly as in 0.15.x.

For iOS, test EF Core changes with a Release build for the Simulator (full AOT). A Debug build JITs and hides runtime model-building failures.

**Checks, for every phase** (this overrides a spec's own "after each sub-phase" checks, to save time and tokens):
- After each sub-phase: `dotnet build` and the app tests (`dotnet test tests/GymApp.Tests`, which includes `CompiledModelTests`).
- At the end of the phase: the full `dotnet test` (server tests, Docker), the Android build, the iOS Release build for the Simulator and running the heads.
- Exception: a sub-phase that changes `GymApp.Contracts`, the server, or a head's own code also builds or tests that part right away.
