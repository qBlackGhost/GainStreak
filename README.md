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

The phone heads are `src/GymApp.Android` and `src/GymApp.iOS`. They aren't in `GymApp.slnx`, so `dotnet build` and `dotnet test` at the root don't need the mobile tools. Debug builds talk to the server on this Mac: start it as under "Running the app against a local server" (the Android emulator reaches it at `10.0.2.2:5080`, the iOS Simulator at `localhost:5080`). Release builds use `GymAppServerUrl`.

**Android:** builds find the SDK and JDK through `AndroidSdkDirectory` and `JavaSdkDirectory` (or `ANDROID_HOME` and `JAVA_HOME`). A Debug build deploys its assemblies separately from the APK, so install it with the build's `Install` target, not `adb install`:

```bash
dotnet build src/GymApp.Android -t:Install -p:AndroidSdkDirectory=$HOME/Library/Android/sdk -p:JavaSdkDirectory=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home
```

A Release APK (about 49 MB, both CPU types) installs with `adb install`:

```bash
dotnet build src/GymApp.Android -c Release -p:AndroidSdkDirectory=$HOME/Library/Android/sdk -p:JavaSdkDirectory=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home
```

```bash
~/Library/Android/sdk/platform-tools/adb install -r src/GymApp.Android/bin/Release/net10.0-android/com.gainstreak.app-Signed.apk
```

**iOS:** build for the Simulator, then install and launch it:

```bash
dotnet build src/GymApp.iOS -c Release -r iossimulator-arm64
```

```bash
xcrun simctl install booted src/GymApp.iOS/bin/Release/net10.0-ios/iossimulator-arm64/GymApp.iOS.app
```

```bash
xcrun simctl launch --console-pty booted com.gainstreak.app
```

The iOS head turns on the interpreter for code generated at runtime only (`MtouchInterpreter=-all` in its project): everything is still compiled ahead of time, but EF Core turns every query into code while the app runs, and iOS can't run that otherwise. It also keeps the SDK trimmed (`MtouchLink=SdkOnly`), or a Simulator build would take over 30 minutes. A Release build for the Simulator takes about 7 minutes. If an iOS build ever crashes at startup with "Failed to load AOT module", delete `src/GymApp.iOS/obj` and `bin` and build again: it's left over from a build with different settings.

## TestBench (testing version)

```bash
dotnet run --project src/GymApp.TestBench
```

The same app plus an admin window: move the clock to any date, sign in as any user without a password, check in without logging sets, undo a check-in, bring back the missed-day card, add or set points (stored as "Manual adjustment" entries the sync keeps), rebuild the points ledger, and reset the test data.

It has its own data folder (`GymApp-TestBench/data` next to the real one), so it can run next to the real app and never changes the real data. The first start copies the real data; "Reset to empty" and "Copy real app's data" restart it. The clock offset is kept in `GymApp-TestBench/bench.json` across restarts.

**Social: Local / Server.** The admin window's Social switch (it restarts the TestBench, kept in `bench.json`):

- **Local** (the default): accounts and friends on this computer only, as before the server existed, with the friend rules run locally. Needs no server. The clock moves freely and "Sign in as" works. Nothing is uploaded.
- **Server**: the app's own wiring against the social server (start it first, see below). Sign in with a real account; "Sign in as" is off. Keep the clock within 2 days of today: the server refuses dates further off, so uploads made with a moved clock are rejected.

**Logging: check-in / live workout.** The admin window's Logging switch (it restarts the TestBench, kept in `bench.json`) picks how Home logs workouts: the desktop's "I went today" check-in, or the phones' live workout (Phase 18), one set at a time with the nudge's return popup. The window going to the back stands in for the phone going to the background.

**Location.** What the live workout's location reads (Phase 18c): **At gym** (pick one of the signed-in user's gyms that has a position: load a city's gyms in the app first), **Away** (nowhere near a gym), **In another city** (central Rome), **Approximate** (precise location off) or **No permission** (location off). Like Android, it shows the note before the location prompt. It starts as Away at each start and isn't kept. Home reads it too, to notice a trip away from the home city (Phase 19): Away and In another city both count as away, and **Look for a trip now** skips the 6-hour wait between looks.

**Vacation.** Start one now for the number of days in the box (today, or tomorrow if today is logged), end it early, or **Clear vacations**, which deletes them all and lifts the 60-day cooldown (their frozen days go too). Move the clock to the return date to see Welcome back.

**Shorter nudge.** Debug builds (the TestBench and Debug phone builds) read `GYMAPP_NUDGE_MINUTES`, e.g. `GYMAPP_NUDGE_MINUTES=1 dotnet run --project src/GymApp.TestBench`, so "Continue your workout?" comes after a minute instead of 12. On the iOS Simulator, launch with `SIMCTL_CHILD_GYMAPP_NUDGE_MINUTES=1 xcrun simctl launch booted com.gainstreak.app`.

## Releases for testers

> **The desktop app is frozen at 0.21.0**, its last release. New work targets the phones (below), and no more desktop releases are planned. The desktop still builds and runs, and the server keeps accepting 0.21.0 (`MinClientVersion` stays at or below it). The steps here are kept for reference.

Desktop releases are built with [Velopack](https://velopack.io): an installer for each system, and the app updates itself.

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

### Phones

The phone betas go out through Google Play internal testing and TestFlight. Both use the same `Version` as the desktop and talk to `GymAppServerUrl`. `build/release-mobile.sh` builds them into `Releases/mobile/<version>/`:

- `GainStreak-<version>-<build>.aab`: the Android App Bundle for Google Play.
- `GainStreak-<version>-<build>.apk`: the same build to send to a tester directly (they allow installs from unknown sources once).
- `GainStreak-<version>-<build>.ipa`: the iOS build for TestFlight.

The build number is the commit count (`git rev-list --count HEAD`), because both stores only take a higher one than the last upload. Commit first, and upload each commit's build once.

**Accounts (one-time).** A Google Play developer account ($25 once) and the Apple Developer Program ($99 a year). The package and bundle id is `com.gainstreak.app`. It can't change once the first build is uploaded. Store questionnaire answers are in `docs/STORE_LISTING.md`, and the privacy policy and account-deletion pages the stores link to are in `site/` (published with GitHub Pages; see that file).

**Signing.** Nothing for signing lives in the repo, and `.gitignore` keeps keystores and profiles out. Keep them somewhere backed up outside it.

- **Android:** make an upload key once and keep it safe. Google Play App Signing holds the real app key; a lost upload key can be reset from the Play Console.

  ```bash
  keytool -genkeypair -v -keystore ~/keys/gainstreak-upload.keystore -alias gainstreak -keyalg RSA -keysize 2048 -validity 10000
  ```

  Then set `GAINSTREAK_ANDROID_KEYSTORE` (the keystore's path), `GAINSTREAK_ANDROID_KEY_ALIAS` and `GAINSTREAK_ANDROID_KEYSTORE_PASS`, plus `GAINSTREAK_ANDROID_KEY_PASS` if the key's password differs. The script passes the passwords as `env:` references, so they don't show in the build log.
- **iOS:**
  1. In Xcode → Settings → Accounts, add the Apple ID and download an **Apple Distribution** certificate, which goes into the keychain.
  2. On developer.apple.com, register the App ID `com.gainstreak.app` with the **HealthKit** capability (Apple Health, 0.20.0), and make an **App Store** provisioning profile for it, then open the profile to install it. A profile made before HealthKit was added doesn't have it: make it again, or signing fails.
  3. Set `GAINSTREAK_IOS_SIGN_IDENTITY` to the certificate's name, e.g. `Apple Distribution: Your Name (TEAMID)`. The team id is the part in brackets.
  4. Set `GAINSTREAK_IOS_PROVISIONING_PROFILE` to the profile's name.
  5. The workout's Live Activity (0.20.0) is an app extension with its own App ID: register `com.gainstreak.app.LiveActivity`, make an **App Store** profile for it too, download it, and set `GAINSTREAK_IOS_LIVE_ACTIVITY_PROFILE` to the downloaded `.mobileprovision` file's path.

Then:

```bash
build/release-mobile.sh
```

`--android` or `--ios` builds just one of them. Uploading:

- **Google Play:**
  1. In the Play Console, create the app.
  2. Under Testing → Internal testing, create a release and upload the `.aab`.
  3. Add the testers' Google accounts to the tester list and send them the opt-in link.
- **TestFlight:**
  1. In App Store Connect, create the app with the same bundle id.
  2. Upload the `.ipa` with Apple's Transporter app.
  3. Once it's processed, add testers under TestFlight. Internal testers need no review; external testers need a short beta review first.

Updates install through the stores. When the server's `MinClientVersion` turns a build away, the 426 banner's **Update** button opens the Play Store on Android. On iOS it appears once `GymAppAppStoreId` (in `src/MobileHead.targets`) holds the App Store id. Raise `MinClientVersion` only once every head has a build that passes it.

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
| `Email__Host`, `Email__User`, `Email__Password` | Optional. The SMTP server that sends the email-confirmation and password-reset codes. For Gmail: `smtp.gmail.com`, the address, and an app password (Google Account → Security → 2-Step Verification → App passwords). Any SMTP provider works. `Email__Port` defaults to 587 and `Email__From` to the user. Unset, nothing is sent in production, and in Development the emails go to the server's log |
| `Invites__BaseUrl` | Optional. Where invite links point, e.g. `https://gainstreak.app` (https, no path). Unset, links use the host the request came to |
| `Invites__AppStoreUrl`, `Invites__PlayStoreUrl` | Optional. Where `/add/<code>` sends phones that don't have the app: the TestFlight public link and the Play testing link during the beta, the store pages later. Play defaults to the app's store page; with no App Store link the page says "App Store: coming soon" |
| `Invites__AppleTeamId` | Optional. The 10-character Apple Team ID. Turns on `/.well-known/apple-app-site-association`, so iPhones open invite links in the app |
| `Invites__AndroidCertFingerprints` | Optional. The SHA-256 fingerprints of the Play app signing key and the upload key (`AB:CD:…`), separated by `;`. Turns on `/.well-known/assetlinks.json`, so Android opens invite links in the app. Play Console → Test and release → App integrity shows both |
| `ForwardedHeaders__KnownNetworks__0` (`__1`, …) | The proxy's network in CIDR form, e.g. `10.0.0.0/8`, if it isn't on the same machine. Only these may set the client IP |

HTTPS ends at the host's proxy; the container listens on plain HTTP on port 8080 and runs as a non-root user. Migrations never run on start: apply them first with the same image.

```bash
docker run --rm -e ConnectionStrings__Social="Host=...;Database=...;Username=...;Password=..." gymapp-server migrate
```

Outside Docker, the same is `dotnet GymApp.Server.dll migrate`.

### Acting on reports

Reports arrive in the Discord channel with the reported username. To suspend that account, or to lift the suspension, run the image the same way:

```bash
docker run --rm -e ConnectionStrings__Social="Host=...;Database=...;Username=...;Password=..." gymapp-server suspend <username>
docker run --rm -e ConnectionStrings__Social="Host=...;Database=...;Username=...;Password=..." gymapp-server unsuspend <username>
```

Suspending ends every session and refuses sign-in, and the account disappears from search, leaderboards, friend lists, the map and shares. Nothing is deleted, so `unsuspend` puts it all back. To remove an account for good, delete its row in `Users`: everything hanging off it goes with it.

## Project layout

| Project | What's in it |
|---|---|
| `src/GymApp.Core` | Pure logic with no database or HTTP: streaks, points, levels, PRs, calorie goals, parsing. Unit-tested |
| `src/GymApp.Data` | EF Core entities, `AppDbContext` and migrations (SQLite) |
| `src/GymApp.Services` | Services over the database, the OpenStreetMap clients (Overpass, Nominatim), and the app's side of the social server: accounts, the remote friend, leaderboard and check-in services with their offline cache, and the upload sync |
| `src/GymApp.App` | The UI the desktop and phone heads share: Avalonia views and view models (CommunityToolkit.Mvvm), the service wiring, and `IPlatform` for what differs per head |
| `src/GymApp.Desktop` | The desktop head: startup, the main window, the data folder and token file, and Velopack updates |
| `src/GymApp.Android`, `src/GymApp.iOS` | The phone heads: startup, icons and splash, the data folder, and the Keystore or Keychain token store. Not in `GymApp.slnx`; see "Building the mobile app" |
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

- **0.&lt;phase&gt;.0**: the phase's spec in `docs/PHASE_<N>_*.md` is done (0.14.0 is Phase 14). New features and migrations come with a new phase.
- **0.&lt;phase&gt;.x**: fixes and small changes within that phase, with no schema changes.
- **1.0.0**: the first public release. After it, 1.0.x is bug fixes only, 1.x.0 new features (migrations allowed) and 2.0.0 a breaking change, such as a migration that can't carry existing data forward or a change to seeded ids.

## Known limitations (0.14.1)

- The server has no production host yet, so a release build has nothing to connect to. There's no password reset or email verification.
- Workouts don't sync between devices: signing in on a second device starts it with no workout history.
- Registering is limited to 5 accounts per hour per IP address, which many phones on one mobile network may share.
- Points are computed on the client and can't be verified.
- Set values can be made up. The check-in rule only stops one-tap check-ins.
- The missed-day choice needs the app to be opened the next day. There are no notifications, so a user who doesn't open it loses the streak at midnight.
