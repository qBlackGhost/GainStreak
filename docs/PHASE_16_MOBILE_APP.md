# Phase 16: The mobile app — spec for Claude Code

This is the spec for GymApp Phase 16: GainStreak on iPhone and Android. Mobile is the main target from here on (decided 2026-10-19). Desktop stays a supported head built from the same code. Work through the sub-phases **in order** and stop after each one for review. 16a is a go/no-go spike: if it fails, stop and revisit the framework choice before touching anything else.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`, and from 16c on, start both mobile heads (Android emulator and iOS Simulator). Then report:
  - which files you created
  - which files you changed or moved
  - which migration you added, and to which database (the app's SQLite or the server's Postgres)
  - what you checked by hand on each platform, and what you couldn't check
- Project layout, existing and new:
  - **GymApp.Core**, **GymApp.Social**, **GymApp.Contracts**, **GymApp.Data**, **GymApp.Client**, **GymApp.Services**: unchanged in role. They already have no UI references; keep it that way.
  - **GymApp.App** (new, 16b): an Avalonia class library, `net10.0`, holding what the heads share: `App.axaml`, `AppServices`, `AppTheme`, `ErrorHandling`, `ViewLocator`, every view model, every view and the styles (`IronlogResources`, `IronlogStyles`, `AuthStyles`), plus `MainView` (today's `MainWindow` content). No platform APIs.
  - **GymApp.Desktop**: becomes a thin head: `Program`, `MainWindow` (hosts `MainView`), `AppUpdater` (Velopack), `MacDockIcon`, `BuildSettings`, the desktop `IPlatform`, and the `.ico`/manifest.
  - **GymApp.Android** (new, 16c): `net10.0-android`, the Android head.
  - **GymApp.iOS** (new, 16c): `net10.0-ios`, the iOS head.
  - **GymApp.TestBench**: stays desktop-only and keeps working. It references App and Desktop.
- Read `App.axaml.cs`, `AppServices`, `Program`, `MainWindow`, `MainViewModel`, `Sidebar`, `ErrorHandling`, `AppTheme`, `AppUpdater`, `BuildSettings`, `AppVersion`, `FeedbackViewModel` (Show log file), `FeedbackView.axaml.cs` (clipboard), `ProfileViewModel` (its `using Avalonia`), `HomeView` (the points-history popup), `GymsView` (Mapsui), `ResponsiveGrid`, `SegmentedPill`, `FileTokenStore`, `AppDbContext.DataDirectory`, `ServerAddress`, `OsmHttp` and `SocialSync` before changing anything.
- Every existing test must still pass after every sub-phase, and the desktop app must look and behave exactly as it does in 0.15.x. Phone layouts are added next to the desktop ones, never in place of them.

## Framework: Avalonia 12 (decided)

The mobile heads use **Avalonia 12.1.x**, the same UI framework as desktop, with Android and iOS heads.

- **What it reuses:** all 6,300 lines of AXAML, every view model, `SegmentedPill`, `ResponsiveGrid`, the light and dark tokens, the services, EF Core on SQLite, `GymApp.Client`, and the test suite. The shared code needs layout work for small screens, not a rewrite.
- **Rejected: .NET MAUI.** Every view would be rewritten in MAUI XAML, with different controls and styling. The map would need a different Mapsui package. Desktop would then be on a second UI stack, or move to MAUI too (its macOS support goes through Mac Catalyst).
- **Rejected: Flutter or React Native.** Everything gets rewritten in another language, including the streak, points, missed-day and calorie rules and their tests. Two copies of the rules would drift apart.
- **What it costs:** Avalonia has no equivalent of MAUI Essentials. Notifications, secure storage and opening the store page need small platform services written in each head (16c, 16g). That's a few hundred lines, behind interfaces in GymApp.App.
- **The risk 16a checks:** EF Core on iOS, where there's no JIT. The app runs on Mono AOT with the interpreter for what can't be compiled ahead of time. Migrations, queries and the points backfill must run correctly and start quickly on a real device build.

## Decisions already made (don't change these)

1. **Workouts stay on the phone.** The server holds social data only. No workout sync (Phase 13, decision 1). A desktop install and a phone install of the same account each keep their own workouts.
2. **One codebase for desktop and mobile.** Phone and desktop differ in layout only (what's shown side by side, the shell, the popups), never in behaviour. A rule change shows up on both.
3. **Privacy rules carry over unchanged** (Phase 14, decision 6). No GPS in this phase: the map still centres on the home city, and distances use the home city and gyms as now. Nothing about the phone's position leaves the phone, because the app doesn't read it.
4. **The server doesn't change for mobile.** The same `/v1` API, the same `X-App-Version` check and the same `MinClientVersion` apply to every head. All heads share one version number (`Directory.Build.props`).
5. **Accounts can be deleted from inside the app** (already built in Phase 14; App Store requirement).

## Defaults to confirm (change here if wrong)

- **Android first for day-to-day work, iOS in step.** Android needs no paid account to run on a phone or send an APK to testers. iOS runs in the Simulator for free, but TestFlight needs the Apple Developer Program ($99 a year). Both heads are built from 16c on.
- **Identity:** app name GainStreak. Bundle id / application id `com.gainstreak.app` (pick the final one before the first store upload, since it can't change after).
- **Phone shell: a bottom tab bar with five tabs:** Home, Workouts, Calories, Friends, More. **More** holds Profile, Splits, Map and Feedback. Tabs show under 700 px wide; the sidebar shows from 700 up (so tablets and desktop keep the sidebar).
- **Back navigation:** a back stack in `MainViewModel`. Tabs are roots. Screens opened from a tab (day → exercise, split → detail, custom exercises, gym picker) push onto it. A back button in the top bar pops it, and so does the Android back button. Popups (points history, feedback, confirmations) close on back first.
- **Popups become full-screen sheets on phones:** the same view, with a top bar holding a title and a close button, instead of a centred card.
- **Token storage:** the iOS Keychain and an Android Keystore-encrypted file, behind the existing `ITokenStore` (as Phase 14 planned). Desktop keeps `FileTokenStore`.
- **Sync on resume:** coming back to the foreground requests a sync (desktop: window activated). The 15-minute timer runs only in the foreground. No background fetch in this phase.
- **Updates:** the stores install updates. The 426 "please update" banner gets an "Update" button that opens the store page. Velopack stays desktop-only.
- **Reminders (16g):** local notifications only, no push server. Off until the user turns them on in Profile, and the permission is requested at that moment, not at first launch.
- **Design:** phone artboards for Home, Workouts → Day → Exercise, Calories, Friends, More/Profile and the tab bar are added to the Gym App design canvas before 16e. The tokens, sliding pill and icons don't change.

---

## 16a. Spike: Avalonia, EF Core and Mapsui on both phones (go/no-go)

A throwaway proof, in a `spike/` folder that is deleted at the end of 16a. It doesn't touch the real projects.

- Install the toolchain: `dotnet workload install android ios`, Xcode with the iOS platform, and the Android SDK and an emulator. Write the exact steps in the README under a new "Building the mobile app" section.
- Create a minimal Avalonia 12.1.3 app with Android and iOS heads. It references GymApp.Data and GymApp.Services and, at start:
  1. runs `Database.Migrate()` on a fresh database in the platform's app data folder
  2. seeds and reads back the preset splits, logs a workout, and computes the streak and points with the real services
  3. shows a Mapsui map (OSM tiles) that pans and pinch-zooms
  4. calls the beta server's `/health`
- Measure on each platform: cold start to first frame, migration time on a fresh install, and the app size. Run iOS as a **Release** build on a device or the Simulator with the same AOT settings a TestFlight build would use (the Debug build JITs on the Simulator and hides the problem).

**Stop and report:** what worked, the numbers, and any EF Core warnings or failures on iOS. **Go** if all four steps work on both platforms and cold start is under 3 seconds on a mid-range Android phone or emulator. **No-go** means stopping here and revisiting the framework choice with what was learned.

### Result (2026-10-27): go

| | Android (Pixel 7 emulator, Android 16, Release) | iOS (iPhone 18 Pro Simulator, iOS 27.1, Release, AOT + interpreter) |
|---|---|---|
| Migrate, fresh install | 24 migrations, 4.46 s | 24 migrations, 3.49 s |
| Splits, workout, streak, points | 2.37 s | 1.71 s |
| Map, `/health` | pass | pass |
| Cold start to first frame | 1.3–1.8 s settled (6.0 s first launch after install) | 2.3–4.6 s from `simctl launch` (upper bound); 0.24–0.63 s of it .NET |
| Size | ~24 MB download, 89 MB installed | ~51 MB compressed, 139 MB installed |

What it changes for the sub-phases below:
- **iOS full AOT can't build EF Core's model.** With the TestFlight defaults, `Migrate()` throws "Model building is not supported when publishing with NativeAOT. Use a compiled model." The spike passed only with the interpreter on (`MtouchInterpreter=-all`, plus `MtouchLink=SdkOnly` to keep build times near 7 minutes). 16c replaces that with a compiled model.
- **EF Core costs 1.6–3 s on every launch** even with nothing to migrate, mostly building its model. The compiled model removes most of that on every head.
- **Avalonia 12 starts Android differently:** `IActivityApplicationLifetime.MainViewFactory`, next to iOS's `ISingleViewApplicationLifetime` and desktop's classic lifetime. 16b handles all three.
- **Data folder on iOS:** `LocalApplicationData` maps to `Documents/GymApp`, the folder meant for user documents, which the Files app can show. 16c moves it to `Library/Application Support/GymApp`.
- Not checked: a real iPhone (cold start). Pinch-zoom on the Android emulator (⌘-drag) zooms but feels odd; the emulator's two mirrored fingers can't show whether real pinches feel right, so 16f checks it on a real phone.

---

## 16b. GymApp.App: the shared UI library

Move the shared UI out of GymApp.Desktop so the mobile heads can use it. Nothing changes for the user.

- Create GymApp.App and move into it everything listed under "Project layout", keeping file names. The namespaces `GymApp.Desktop.ViewModels` and `GymApp.Desktop.Views` become `GymApp.App.ViewModels` and `GymApp.App.Views` (update `x:Class`, `xmlns` and `x:DataType` in every AXAML file, the tests and the TestBench).
- **`MainView`** (a `UserControl`): today's `MainWindow` content (the banners, `Current`, the feedback popup). `MainWindow` becomes a window that hosts `MainView`.
- **`IPlatform`** in GymApp.App, registered by each head. It takes over everything the shared code got from the desktop:
  - `DataDirectory` (desktop: `AppDbContext.DataDirectory` as now; `GYMAPP_DATA_DIR` still overrides it on desktop)
  - `ITokenStore CreateTokenStore()`
  - `AppVersionDescription` (feeds `ErrorLog` and the OSM User-Agent)
  - `CanShowLogFolder` and `ShowLogFolder()` (desktop: today's `Process.Start`; mobile: false, and Feedback hides "Show log file")
  - `CanRestartForUpdate` with the Velopack hook, and `OpenStorePage()` for the 426 banner
  - `ServerUrl` (desktop: `BuildSettings.ServerUrl`)
- `App.OnFrameworkInitializationCompleted` handles three lifetimes: `IClassicDesktopStyleApplicationLifetime` gets `MainWindow`, `ISingleViewApplicationLifetime` (iOS) gets `MainView`, and `IActivityApplicationLifetime` (Android) gets it through `MainViewFactory` (found in 16a). The startup error window becomes a startup error view, shown either way.
- `ProfileViewModel` loses its `using Avalonia`; whatever it uses (likely the theme switch) goes through `AppTheme`.
- `AppDbContext` gets its data folder from `IPlatform` (through the options or a static set once at startup) instead of reading `Environment` itself.

**Tests:** `AppServicesTests` builds the services with a fake `IPlatform` in both modes. All existing view model tests pass with only namespace changes. Start the desktop app and the TestBench and check that every screen still works.

---

## 16c. The Android and iOS heads

The app starts on both phones, with the desktop layout squeezed onto the screen. That's expected; the layouts come in 16d and 16e.

- **GymApp.Android:** `MainActivity` (Avalonia's `AvaloniaMainActivity`), application id and label, minimum API 26 (Android 8), the launcher icon and splash in the flame-arrow and lime of the desktop icon. Permissions: `INTERNET` only (16g adds notifications).
- **GymApp.iOS:** `AppDelegate`, bundle id, display name, minimum iOS 16, the app icon set and launch screen. `ITSAppUsesNonExemptEncryption = false` (HTTPS only).
- **Startup must not block the UI thread.** Today the migration, the points backfill and the session restore run with `GetAwaiter().GetResult()` before the window opens. iOS kills an app that doesn't draw its first frame in time. On mobile: show a splash view at once (logo on the page colour), run the three steps off the UI thread, then switch to login or Home. Desktop may keep its current order.
- **Compiled EF Core model** (16a: full AOT on iOS can't build the model at runtime, and building it costs 1.6–3 s per launch everywhere). Generate it with `dotnet ef dbcontext optimize --project src/GymApp.Data --startup-project src/GymApp.Desktop --output-dir CompiledModels` and use it in `AppDbContext.OnConfiguring` (`UseModel`). It must be regenerated after every model change: add the command to CLAUDE.md next to the migration commands, and a test that fails when the compiled model and the live model differ. iOS then builds with the TestFlight defaults (no interpreter); if anything still needs it, report it rather than turning the interpreter back on silently. Measure the launch time again on both phones.
- **Data folder:** iOS uses `Library/Application Support/GymApp`. It's still in the phone's backup, which is good for workouts, but the Files app doesn't show it the way it can show Documents. The session token stays out of backups because it's in the Keychain; Android keeps the app's private `files/GymApp`. Both come from `IPlatform.DataDirectory`.
- **Token storage:** `KeychainTokenStore` (iOS, `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`, so it isn't in backups or moved to another phone) and `KeystoreTokenStore` (Android, a file encrypted with a Keystore key in the app's private folder). Both implement `ITokenStore`.
- **Lifecycle:** resume → `SocialSync.RequestSync()`; going to the background stops the 15-minute timer and resume starts it again. The SQLite connection survives backgrounding (check that a check-in written just before the app is suspended is there after it's killed and restarted).
- **Server address:** release builds get `GymAppServerUrl` from `Directory.Build.props`, as desktop does. Debug builds can't use `localhost` on a phone: the Android emulator reaches the Mac at `10.0.2.2`, and the iOS Simulator at `localhost`. Use those defaults in Debug, overridable at build time with `-p:GymAppServerUrl=...`.
- **Errors:** `ErrorHandling` works unchanged. `errors.log` goes in the app data folder, and Feedback attaches it as on desktop.

**Tests:** `KeychainTokenStore` and `KeystoreTokenStore` can't run in `dotnet test`. Check them by hand on each platform: sign in, kill the app, reopen, still signed in; log out, the token is gone. Add a test that the mobile startup path (splash → restore → Home or login) reaches the right screen with a fake `IPlatform`.

---

## 16d. The phone shell: tabs, back, sheets, safe areas

- **Tab bar** (`TabBar.axaml` in GymApp.App) with the five tabs from the defaults, the same icons as the sidebar, and the lime accent for the selected tab. `MainView` shows the sidebar from 700 px wide and the tab bar under it, switching live when the width changes (rotation, split screen, tablets, a narrow desktop window).
- **More** (`MoreView`): a list with the profile card at the top (as in the sidebar), then Splits, Map, Feedback, and the version line.
- **Back stack** in `MainViewModel`: `Show…` methods for the tab roots clear the stack; the others push. `GoBack()` closes an open popup first, then pops. A top bar on pushed screens holds the back button and the title. Android's back button calls `GoBack()`; at a tab root it leaves the app (the default). Desktop ignores the stack in its UI; the sidebar keeps working as now.
- **Sheets:** the points history and feedback popups, and any confirmation that is a centred card today, become full-height sheets on phones (top bar with title and close). The same view models and content.
- **Safe areas:** content stays clear of the notch, the Dynamic Island, the home indicator and Android's gesture bar (Avalonia's `InsetsManager`, `DisplayEdgeToEdge`). The tab bar sits above the home indicator, and its background extends behind it.
- **Keyboard:** fields being typed into stay visible above the on-screen keyboard (Avalonia's `InputPane`). Number fields (weights, reps, calories, height, weight) open the numeric keyboard (`TextInputOptions.ContentType`). The login form uses the username, email and password content types so password managers fill them.
- **Touch:** every tap target is at least 44 × 44 pt. Nothing depends on hover (tooltips, hover-only buttons get a visible alternative).

**Tests:** the back stack (push, pop, tab switch clears, popup closes first, back at a root returns false); `MainViewModel` exposes which tab is selected for every screen.

---

## 16e. Phone layouts, screen by screen

Each screen gets a phone layout at 360–430 px wide, after the phone artboards. Use the width breakpoints already in `ResponsiveGrid` and styles keyed on a `phone` class set by `MainView`, inside the existing AXAML files. A separate phone view file is allowed only where a screen's structure is genuinely different (expected: none, or Home at most). In this order, stopping for review after the first three:

1. **Login and Register:** a single column, the brand at the top.
2. **Home:** one column in the order of the phone artboard (streak and check-in first, then today's workout, this week, calories, level, recent PRs, friends). The missed-day card and the 7-day row fit 360 px.
3. **Check-in and Day:** logging sets must be quick with one thumb: the set rows fit without sideways scrolling, and the numeric keyboard moves between weight and reps.
4. Exercise (PR history, the 1RM line), Custom exercises.
5. Workouts, Splits, Split detail (the days-per-week cards stack), Split editor.
6. Calories (the donut and the log).
7. Friends (requests, both leaderboards; the Recent / By gym pill).
8. Map (the map fills the screen above the list; the gym picker and city search as sheets), Gyms visited per month.
9. Profile (all sections in one column, Appearance on the pill), Points history, Feedback.

Check each one at 360 × 740 (a small Android), 393 × 852 (iPhone 15/16) and 430 × 932 (Pro Max), in light and dark, with the largest system text size the layout can reasonably take. Nothing scrolls sideways and no text is cut off.

**Tests:** the existing view model tests cover behaviour. Add none per layout; report the checks above with a screenshot per screen per theme at 393 × 852.

---

## 16f. Platform touches

- **Map:** Mapsui pans and pinch-zooms by touch (see the map checks below). Tiles still come from the public OSM servers with the identifying User-Agent (now naming the platform). Replacing them stays a pre-1.0 task (see Later).
- **Feedback:** "Show log file" is hidden where `CanShowLogFolder` is false. "Copy instead" uses the clipboard as now. "See what's sent" works unchanged.
- **The 426 banner:** "Update" opens the store page (`OpenStorePage()`) on mobile. "Restart now" stays desktop-only.
- **Theme:** System follows the phone's light or dark setting live, as it follows macOS and Windows now. The status bar text colour follows the theme.
- **Haptics:** none in this phase.

**Map checks,** by hand on a real Android phone and a real iPhone. The emulator and Simulator don't count: in 16a the emulator's ⌘-drag pinch zoomed but felt odd, and its mirrored fake fingers can't show how a real pinch feels. Each check passes on both phones:

1. **Pinch out zooms in, pinch in zooms out,** never the other way.
2. **Zoom follows the fingers:** the map grows or shrinks at the speed the fingers move, without lagging behind or overshooting.
3. **Zoom centres between the fingers:** the point under the middle of the pinch stays under it. The map doesn't zoom around the screen centre or a corner.
4. **No jump when a finger lifts:** lifting one finger mid-pinch and carrying on with the other turns into a smooth pan.
5. **One-finger drag pans** without zooming, and a fling coasts to a stop.
6. **Double-tap zooms in one step** around the tapped point.
7. **The map keeps its gestures:** a pinch or drag on the map never also scrolls the page or the gym list around it, and swiping on the list never moves the map.
8. **Pins stay tappable** at every zoom level, and tapping a pin doesn't also pan the map.
9. **Zoom limits hold:** it stops at a sensible city-level minimum and street-level maximum, without going blank.
10. **No stutter:** pinching and panning stay smooth while tiles load, on a mid-range Android phone too.

Fix what fails through Mapsui's settings (gesture and zoom-limit options), a newer Mapsui, or the layout around the map (for 7). Write any check that can't be fixed in this phase into the report, with what happens. 16f isn't done until all ten pass or the failures are reported.

---

## 16g. Reminders: local notifications

The 0.14.1 known limitation: the missed-day choice needs the app opened the next day, so a user who forgets loses the streak at midnight. Phones can remind them.

- **Profile → Reminders:** off by default. Turning it on asks for notification permission (iOS: `UNUserNotificationCenter`; Android 13+: `POST_NOTIFICATIONS`). Refused: the switch turns itself back off and says how to allow it in Settings.
- **Two reminders**, each with its own switch once Reminders is on:
  - **Training day:** on a scheduled training day with no check-in yet, at a time the user picks (default 18:00): "Today is <day name>. Check in when you've trained to keep your <n>-day streak."
  - **Missed day:** the morning after a missed training day, at 09:00: "You missed <day name> yesterday. Choose what happens to your <n>-day streak before midnight." Tapping it opens Home with the missed-day card.
- **Scheduling rule** (pure function in GymApp.Services, `ReminderPlanner`): from the schedule, today's check-in and the missed-day rules, return the reminders for the next 7 days. The heads schedule exactly that list, replacing what was scheduled before, at startup, on resume, after a check-in or undo, and after a schedule change. A check-in cancels today's training-day reminder.
- **Platform services:** `INotificationScheduler` in GymApp.App, implemented with `UNUserNotificationCenter` (iOS) and `AlarmManager` with a broadcast receiver (Android, inexact alarms; no exact-alarm permission). Desktop: a no-op implementation, and the Reminders section is hidden.
- **Schema:** `User.RemindersEnabled`, `User.TrainingReminderEnabled`, `User.TrainingReminderTime`, `User.MissedDayReminderEnabled`. **Migration:** `AddReminders` in the app's SQLite.

**Tests:** `ReminderPlanner`: a training day with no check-in, one already checked in, a rest day, a missed day the morning after, a missed day already resolved, a made-up day, the week-shift and shift-to-rest choices, reminders off, a schedule change mid-week, and the day the user's timezone changes to/from daylight saving time.

---

## 16h. Beta builds for phones

- **Version:** the shared `Version` (0.16.x) becomes `CFBundleShortVersionString` and the Android `versionName`. A build number that only goes up (`ApplicationVersion`) is set by the release script from the commit count.
- **`build/release-mobile.sh`:** builds a signed Android App Bundle (and an APK for sideloading) and an iOS archive. Signing keys and the Apple team id come from the environment or the keychain, never from the repo. Add the keystore and provisioning files to `.gitignore`.
- **Distribution:** Google Play internal testing (one-time $25 account) and TestFlight (Apple Developer Program). The README's "Releases for testers" section gets a mobile part.
- **Store paperwork** (written as notes in `docs/STORE_LISTING.md`, not submitted): the privacy answers (an account with username and email, public stats, optional gym check-ins; no tracking, no ads), the account-deletion path, and the screenshots from 16e.
- **Server:** no change. Raise `MinClientVersion` only when every head has a build that passes it.

---

## Later (not this phase)

- Replacing the public OSM tile, Nominatim and Overpass servers before a public release (required: their usage policies don't allow an app's traffic at scale).
- Rate limits behind shared IPs: mobile carriers put many phones behind one IP, so "register 5 an hour per IP" will hit real users. Fix before the public release.
- Push notifications for friend requests (needs APNs/FCM and server work).
- Background sync, home-screen widgets (streak), Apple Health / Health Connect.
- GPS: "you're at a gym, check in?" (a privacy decision of its own).
- Workout sync between devices (would need Guid ids for rows created offline).
- Retiring or slimming the desktop head, if mobile makes it unnecessary.
