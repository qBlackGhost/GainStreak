# GainStreak

A desktop gym tracker built with .NET 10 and Avalonia. Pick a training split, check in when you've trained, log your sets and PRs, and keep a streak going. Points, levels and a friends-only leaderboard make it a bit of a game.

The code keeps its original working name: projects, namespaces, the data folder and the `GYMAPP_*` settings are still called GymApp.

## What it does

- **Splits and schedule.** Preset splits (Push/Pull/Legs, Upper/Lower, Full Body...) with a choice of start day, or your own custom split. Home shows today's workout.
- **Check-in.** "I went today" logs the workout once you've filled in your sets. It can be undone the same day.
- **Streaks.** Rest days count. A missed training day can be shifted, made up before the next rest day, or skipped, chosen the next day.
- **Exercises, sets and PRs.** A built-in exercise list plus your own. Planned warm-up and working sets per day, last session's sets as placeholders, PR history with an estimated 1RM.
- **Body data and calories.** Height, weight and activity level give a maintenance estimate and a suggested calorie goal. You log what you ate each day.
- **Points and levels.** Points for workouts, complete weeks and hitting your calorie target.
- **Gyms.** Gyms in your city come from OpenStreetMap, shown as a list or a map. You can add your own gyms and log which one you trained at.
- **Friends.** Friend requests, a weekly or all-time leaderboard with each friend's streak, and a map of the gyms friends trained at (if they share it).

Workouts, sets, PRs, body data and calories stay on the device, in SQLite. The account, friends, the leaderboard and shared gym check-ins live on the social server (`src/GymApp.Server`); the app uploads only your username, points, level, streak and, while you share them, which gym you trained at on which day. Without a connection you can still sign in on your own device, log workouts and see friends as they were last time ("Last updated ..."); uploads wait and go out when you're back online. The gym search, city and address search and the map tiles use OpenStreetMap.

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

The same folder holds `session.token` (the server session that keeps you signed in, readable only by you) and `errors.log` (unexpected errors, each stamped with the app version and system). Testers send it with **Feedback** (the sidebar's Support section) → Report a problem → Send, which posts their note and the report to the server (user names are taken out of file paths first). If sending fails, "Copy instead" puts it on the clipboard. To run against a copy of your data, set `GYMAPP_DATA_DIR` to another folder.

The app needs the social server to register and to sign in for the first time on a device; see [Running the app against a local server](#running-the-app-against-a-local-server).

## Building the mobile app

The Android and iOS heads need a Mac (Apple silicon) and about 35 GB of free disk space: Xcode takes about 12 GB, the iOS Simulator runtime about 7 GB, the Android SDK and an emulator about 6 GB, the .NET workloads about 3 GB, and an iOS Release build about 2 GB.

**.NET workloads** (`sudo` because the SDK is installed under `/usr/local/share/dotnet`):

```bash
sudo dotnet workload install android ios
```

**Xcode:** install it from the App Store. The iOS workload is built for one Xcode version (workload 27.1 needs Xcode 27.1; `dotnet workload list` shows the workload version). Then:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

```bash
sudo xcodebuild -license accept
```

```bash
xcodebuild -runFirstLaunch
```

```bash
xcodebuild -downloadPlatform iOS
```

Only one iOS Simulator runtime is needed; `xcrun simctl runtime list` lists them and `xcrun simctl runtime delete <id>` removes an old one (about 7 GB each).

**Android:** a JDK 21 and the Android command-line tools, from Homebrew (no `sudo`):

```bash
brew install openjdk@21
```

```bash
brew install --cask android-commandlinetools
```

Accept the SDK licenses and install the SDK into `~/Library/Android/sdk`:

```bash
yes | JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home sdkmanager --sdk_root=$HOME/Library/Android/sdk --licenses
```

```bash
JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home sdkmanager --sdk_root=$HOME/Library/Android/sdk "platform-tools" "platforms;android-36" "build-tools;36.0.0" "emulator" "system-images;android-36;google_apis;arm64-v8a"
```

Homebrew's `avdmanager` looks for the SDK next to itself and can't see these system images, so create the emulator with the copy inside the SDK:

```bash
JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home sdkmanager --sdk_root=$HOME/Library/Android/sdk "cmdline-tools;latest"
```

```bash
JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home ~/Library/Android/sdk/cmdline-tools/latest/bin/avdmanager create avd -n Pixel -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_7
```

```bash
~/Library/Android/sdk/emulator/emulator -avd Pixel
```

On Apple silicon the Android 16 (API 36) image draws on the CPU: its log says "API level 36 requires GLES 3.1, forcing software rendering for GLES". Scrolling, map redraws and gestures are then slower and choppier than on a phone, so for UI work add a second emulator on Android 15 (API 35), which can draw on the Mac's GPU. The heads still build against API 36; this only changes the emulator. It needs about 2 GB more:

```bash
JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home sdkmanager --sdk_root=$HOME/Library/Android/sdk "system-images;android-35;google_apis;arm64-v8a"
```

```bash
JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home ~/Library/Android/sdk/cmdline-tools/latest/bin/avdmanager create avd -n Pixel35 -k "system-images;android-35;google_apis;arm64-v8a" -d pixel_7
```

```bash
~/Library/Android/sdk/emulator/emulator -avd Pixel35 -gpu host
```

Its log should no longer say "forcing software rendering". If it does, the GPU isn't being used and the emulator is no better than `Pixel`. Keep `Pixel` for checking Android 16 behaviour. Neither emulator replaces a real phone for judging how gestures feel.

The heads (`src/GymApp.Android`, `src/GymApp.iOS`) arrive in Phase 16c. Android builds find the SDK and JDK through `AndroidSdkDirectory` and `JavaSdkDirectory` (or `ANDROID_HOME` and `JAVA_HOME`):

```bash
dotnet build src/GymApp.Android -c Release -p:AndroidSdkDirectory=$HOME/Library/Android/sdk -p:JavaSdkDirectory=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home
```

**iOS Release builds and EF Core:** a Release build for the Simulator (`-r iossimulator-arm64`) is compiled ahead of time like a TestFlight build, so test EF Core changes there; a Debug build JITs and hides problems. With the default settings (no JIT and no interpreter) EF Core can't build its model at runtime and `Migrate()` throws "Model building is not supported when publishing with NativeAOT". Until the compiled EF Core model arrives (Phase 16c), the iOS head turns on the interpreter for code generated at runtime, and keeps the SDK trimmed as a device build does:

```bash
dotnet build src/GymApp.iOS -c Release -r iossimulator-arm64 -p:MtouchInterpreter=-all -p:MtouchLink=SdkOnly
```

```bash
xcrun simctl install booted src/GymApp.iOS/bin/Release/net10.0-ios/iossimulator-arm64/GymApp.iOS.app
```

Without `MtouchLink=SdkOnly` a Simulator build with the interpreter trims nothing and takes over 30 minutes; with it, about 7.

## TestBench (testing version)

```bash
dotnet run --project src/GymApp.TestBench
```

The same app plus an admin window: move the clock to any date, sign in as any user without a password, check in without logging sets, undo a check-in, bring back the missed-day card, add or set points (stored as "Manual adjustment" entries the sync keeps), rebuild the points ledger, and reset the test data.

It has its own data folder (`GymApp-TestBench/data` next to the real one), so it can run next to the real app and never changes the real data. The first start copies the real data; "Reset to empty" and "Copy real app's data" restart it. The clock offset is kept in `GymApp-TestBench/bench.json` across restarts.

**Social: Local / Server.** The admin window's Social switch (it restarts the TestBench, kept in `bench.json`):

- **Local** (the default): accounts and friends on this computer only, as before the server existed, with the friend rules run locally. Needs no server. The clock moves freely and "Sign in as" works. Nothing is uploaded.
- **Server**: the app's own wiring against the social server (start it first, see below). Sign in with a real account; "Sign in as" is off. Keep the clock within 2 days of today: the server refuses dates further off, so uploads made with a moved clock are rejected.

## Releases for testers

Releases are built with [Velopack](https://velopack.io): an installer for each system, and the app updates itself.

1. Fill in `GymAppServerUrl` (the social server, https) and `GymAppUpdateUrl` (the folder the releases will be served from) in `Directory.Build.props`. Both are built into the app. A Release publish without a server address stops with an error. Without an update folder the app never checks for updates.
2. Bump `Version` in `Directory.Build.props`. Velopack only offers a higher version.
3. On a Mac:

   ```bash
   build/release.sh
   ```

   It publishes self-contained builds (osx-arm64, osx-x64, win-x64), so testers don't need .NET, and packs them into `Releases/`:
   - `GainStreak-osx-arm64-Setup.pkg` / `GainStreak-osx-x64-Setup.pkg`: installs `GainStreak.app` (Apple silicon / Intel). The `-Portable.zip` is the same `.app` without an installer.
   - `GainStreak-win-x64-Setup.exe`: installs for the current user, no admin needed, with Start menu and desktop shortcuts.
   - `releases.<channel>.json`, the `.nupkg` packages and the rest: what the app reads to update. Each architecture is its own channel, so a copy only updates to its own build.
4. Upload everything in `Releases/` to the update folder and keep the files from earlier versions there. Keep `Releases/` locally too: the next run builds small delta updates against it.

   **On GitHub:** set `GymAppUpdateUrl` to a public repository used only for releases (e.g. `https://github.com/qBlackGhost/GainStreak-releases`; this repository is private, so testers' copies couldn't read its releases). Then, signed in with `gh auth login`:

   ```bash
   build/upload-github.sh
   ```

   It creates the release `v<Version>` with all three channels' files. Testers download the `Setup` file for their system from the release page. Without a token the app may check GitHub 60 times an hour per network, so one check per start is fine.

Installed copies check the folder at startup and download a newer release in the background. The banner offers "Restart now", and otherwise the update installs the next time the app opens. `dotnet run`, the TestBench and plain `dotnet publish` folders never update.

**Signing.** Unsigned builds work, but the first start is harder: on macOS testers open the app once, then choose "Open Anyway" in System Settings → Privacy & Security; on Windows SmartScreen asks, and "More info" → "Run anyway" starts it. To sign and notarize the Mac builds, set `VPK_SIGN_APP_IDENTITY`, `VPK_SIGN_INSTALL_IDENTITY` and `VPK_NOTARY_PROFILE` before running the script (Apple Developer ID certificates).

For a quick local build without an installer, `dotnet publish src/GymApp.Desktop -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64` still works (it needs `GymAppServerUrl` too).

## Running the app against a local server

The social server (accounts, friends, the leaderboard and shared gym check-ins) is `src/GymApp.Server`, an ASP.NET Core API on Postgres. See `docs/PHASE_13_SOCIAL_SERVER.md` and `docs/PHASE_14_APP_ON_SOCIAL_SERVER.md`.

Start a local Postgres (Docker), then the server. In Development it applies migrations on start and listens on `http://localhost:5080`:

```bash
docker compose up -d
```

```bash
dotnet run --project src/GymApp.Server
```

`curl http://localhost:5080/health` should answer `Healthy`. Then start the app, which talks to `http://localhost:5080` in Debug builds:

```bash
dotnet run --project src/GymApp.Desktop
```

To try feedback forwarding locally, give the dev server a webhook: `dotnet user-secrets set Feedback:DiscordWebhookUrl "https://discord.com/api/webhooks/..." --project src/GymApp.Server`.

To use another server, set `GYMAPP_SERVER_URL` before starting the app, e.g. a server on another machine on your network:

```bash
GYMAPP_SERVER_URL=http://192.168.1.20:5080 dotnet run --project src/GymApp.Desktop
```

Release builds use the `https://` address they were published with (`GymAppServerUrl`), and `GYMAPP_SERVER_URL` overrides it. Without either, the app shows why it can't start. Requests time out after 10 seconds, which counts as offline.

**Accounts from before the server.** The first sign-in with an account that only existed on this computer offers "Move this account online": it registers the account on the server (asking for an email if it has none, or another username if it's taken) and keeps its workouts, points and streak. Local friendships don't carry over; friends add each other again.

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
| `MinClientVersion` | Optional. The oldest app version the API accepts, e.g. `0.15.0`. Older builds, and builds from before 0.15 (which don't send their version), get a 426 and show "please update"; workouts keep working on the device. Unset, every build is accepted |
| `Feedback__DiscordWebhookUrl` | Optional. A Discord webhook (Channel settings → Integrations → Webhooks → New Webhook → Copy URL). Each problem report or idea from the app is posted there, the error report as an attached file. Unset, feedback is only stored in the `Feedback` table. Keep it secret: anyone with the URL can post to the channel |
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
| `src/GymApp.Services` | Services over the database, the OpenStreetMap clients (Overpass, Nominatim), and the app's side of the social server: accounts, the remote friend, leaderboard and check-in services with their offline cache, and the upload sync |
| `src/GymApp.App` | The UI the desktop and phone heads share: Avalonia views and view models (CommunityToolkit.Mvvm), the service wiring, and `IPlatform` for what differs per head |
| `src/GymApp.Desktop` | The desktop head: startup, the main window, the data folder and token file, and Velopack updates |
| `src/GymApp.Social` | The friend and leaderboard rules, the `ISocialStore` interface (shared by the TestBench's local mode and the server), and the `IFriendService` / `ILeaderboardService` / `IFriendCheckInSource` interfaces the view models use |
| `src/GymApp.Contracts` | Request and response records for the server's HTTP API |
| `src/GymApp.Client` | The typed HTTP client for the server (`SocialApiClient`, with Ok / Refused / Offline results), the token store and the server address rule. No database or UI, so a mobile app can reuse it |
| `src/GymApp.Server` | The social server: ASP.NET Core minimal API, `ServerDbContext` and migrations (Postgres) |
| `src/GymApp.TestBench` | The testing version: the app plus an admin window (see above) |
| `tests/GymApp.Tests` | xUnit tests. Service tests run against in-memory SQLite with the real migrations; the app's server side runs against a fake HTTP handler |
| `tests/GymApp.Server.Tests` | Endpoint tests against a real Postgres (Testcontainers), plus contract and end-to-end tests: the app's client and services, one simulated phone per user, against the real server |
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

Before the first public release the version is **0.&lt;phase&gt;.&lt;fix&gt;**:

- **0.&lt;phase&gt;.0**: the implementation plan's phase is done (0.14.0 is Phase 14). New features and migrations come with a new phase.
- **0.&lt;phase&gt;.x**: fixes and small changes within that phase, with no schema changes.
- **1.0.0**: the first public release. After it, 1.0.x is bug fixes only, 1.x.0 new features (migrations allowed) and 2.0.0 a breaking change, such as a migration that can't carry existing data forward or a change to seeded ids.

## Known limitations (0.14.1)

- The server has no production host yet, so a release build has nothing to connect to. There's no password reset or email verification.
- Workouts don't sync between devices: signing in on a second device starts it with no workout history.
- Registering is limited to 5 accounts per hour per IP address, which many phones on one mobile network may share.
- Points are computed on the client and can't be verified.
- Set values can be made up. The check-in rule only stops one-tap check-ins.
- The missed-day choice needs the app to be opened the next day. There are no notifications, so a user who doesn't open it loses the streak at midnight.
