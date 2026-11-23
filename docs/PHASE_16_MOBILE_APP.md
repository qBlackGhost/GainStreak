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
  - **GymApp.App** (new, 16b): an Avalonia class library, `net10.0`, holding what the heads share: `App.axaml`, `AppServices`, `AppTheme`, `ErrorHandling`, `ViewLocator`, every view model, every view and the styles (`GainStreakResources`, `GainStreakStyles`, `AuthStyles`), plus `MainView` (today's `MainWindow` content). No platform APIs.
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
- `ProfileViewModel` keeps its `using Avalonia`: it's for `Avalonia.Point` in the weight trend chart, which is fine in GymApp.App (an Avalonia library). It's not a platform API.
- `AppDbContext` gets its data folder from `IPlatform` (through the options or a static set once at startup) instead of reading `Environment` itself.
- **As built:** the application class is `GymApplication`, not `App`, because a head's namespace (`GymApp.Desktop`, `GymApp.TestBench`) would read `App` as the namespace `GymApp.App`. `AppDbContext.DataDirectory` is set once at startup from `IPlatform`; `AppDbContext.DefaultDataDirectory` (GYMAPP_DATA_DIR or LocalApplicationData) stays as the desktop's folder and the fallback for `dotnet ef` and tests. `IPlatform` also has `OnFrameworkReady` (the macOS Dock icon), `CreateWindow` (desktop only) and `OnShellReady` (starts the Velopack download).

**Tests:** `AppServicesTests` builds the services with a fake `IPlatform` in both modes. All existing view model tests pass with only namespace changes. Start the desktop app and the TestBench and check that every screen still works.

---

## 16c. The Android and iOS heads

The app starts on both phones, with the desktop layout squeezed onto the screen. That's expected; the layouts come in 16d and 16e.

- **GymApp.Android:** `MainActivity` (Avalonia's `AvaloniaMainActivity`), application id and label, minimum API 26 (Android 8), the launcher icon and splash in the flame-arrow and lime of the desktop icon. Permissions: `INTERNET` only (16g adds notifications).
- **GymApp.iOS:** `AppDelegate`, bundle id, display name, minimum iOS 16, the app icon set and launch screen. `ITSAppUsesNonExemptEncryption = false` (HTTPS only).
- **Startup must not block the UI thread.** Today the migration, the points backfill and the session restore run with `GetAwaiter().GetResult()` before the window opens. iOS kills an app that doesn't draw its first frame in time. On mobile: show a splash view at once (logo on the page colour), run the three steps off the UI thread, then switch to login or Home. Desktop may keep its current order.
- **Compiled EF Core model** (16a: full AOT on iOS can't build the model at runtime, and building it costs 1.6–3 s per launch everywhere). Generate it with `dotnet ef dbcontext optimize --project src/GymApp.Data --startup-project src/GymApp.Desktop --output-dir CompiledModels` and use it in `AppDbContext.OnConfiguring` (`UseModel`). It must be regenerated after every model change: add the command to CLAUDE.md next to the migration commands, and a test that fails when the compiled model and the live model differ. iOS was to build with the TestFlight defaults (no interpreter), but EF's queries still need it (see As built). Measure the launch time again on both phones.
- **Data folder:** iOS uses `Library/Application Support/GymApp`. It's still in the phone's backup, which is good for workouts, but the Files app doesn't show it the way it can show Documents. The session token stays out of backups because it's in the Keychain; Android keeps the app's private `files/GymApp`. Both come from `IPlatform.DataDirectory`.
- **Token storage:** `KeychainTokenStore` (iOS, `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`, so it isn't in backups or moved to another phone) and `KeystoreTokenStore` (Android, a file encrypted with a Keystore key in the app's private folder). Both implement `ITokenStore`.
- **Lifecycle:** resume → `SocialSync.RequestSync()`; going to the background stops the 15-minute timer and resume starts it again. The SQLite connection survives backgrounding (check that a check-in written just before the app is suspended is there after it's killed and restarted).
- **Server address:** release builds get `GymAppServerUrl` from `Directory.Build.props`, as desktop does. Debug builds can't use `localhost` on a phone: the Android emulator reaches the Mac at `10.0.2.2`, and the iOS Simulator at `localhost`. Use those defaults in Debug, overridable at build time with `-p:GymAppServerUrl=...`.
- **Errors:** `ErrorHandling` works unchanged. `errors.log` goes in the app data folder, and Feedback attaches it as on desktop.

**Tests:** `KeychainTokenStore` and `KeystoreTokenStore` can't run in `dotnet test`. Check them by hand on each platform: sign in, kill the app, reopen, still signed in; log out, the token is gone. Add a test that the mobile startup path (splash → restore → Home or login) reaches the right screen with a fake `IPlatform`.

**As built:**
- The startup steps moved into `AppStartup.PrepareAsync`, which both paths run off the UI thread: the desktop waits for it before opening its window, as before, and the phones show `SplashView` first. `PhonePlatform` (GymApp.App) holds what both phones share; `AndroidPlatform` and `IosPlatform` add the data folder and token store.
- **Compiled model on Mono:** the generated `AppDbContextModel` builds itself on a second thread inside its static constructor, and on Mono that thread waits for the same static constructor, so the phones hung on the splash. `MobileHead.targets` turns on EF's `Microsoft.EntityFrameworkCore.Issue31751` switch for the phone heads, which builds it inline. Desktop keeps the default.
- **Server address on phones:** the iOS trimmer removes the assembly metadata desktop reads, so `MobileHead.targets` writes the addresses into a generated `BuiltSettings` class. Debug builds use `GymAppDebugServerUrl` (10.0.2.2:5080 on Android, localhost:5080 on iOS) through the `GYMAPP_SERVER_URL` override.
- **Android cleartext:** Android refuses plain HTTP, so `network_security_config.xml` allows it to 10.0.2.2 and localhost only, for the Debug server.
- **iOS interpreter (decided 2026-10-27):** the compiled model lets iOS build the model, but EF Core also turns every LINQ query into code at runtime, which iOS refuses without the interpreter, Debug builds included ("Query wasn't precompiled and dynamic code isn't supported"). The iOS head sets `MtouchInterpreter=-all` (everything compiled ahead of time, only runtime-generated code interpreted) and `MtouchLink=SdkOnly`. EF's experimental precompiled queries were the alternative: rejected for now, since they don't cover dynamic queries and must be regenerated after every query change. The compiled model stays, for startup time.
- **No migration check at runtime:** EF Core 10's `Migrate()` builds the design-time model to look for changes without a migration, which iOS can't do and which costs every head the time the compiled model saves. `AppDbContext` ignores `PendingModelChangesWarning`; `CompiledModelTests.EveryModelChange_HasAMigration` checks it in `dotnet test`.
- **Keychain entitlement:** without an entitlements file attached (`CodesignEntitlements`), every Keychain write fails with `MissingEntitlement`, so sign-in and registration failed after the server had already answered (registering again then said the username was taken). `Entitlements.plist` grants `keychain-access-groups`. Simulator builds are signed ad hoc (`CodesignKey` `-`), so they need no Apple certificate. The login and register screens now write unexpected errors to errors.log instead of only showing "Could not sign in".
- **Foreground:** `IActivatableLifetime` (Background activation) restarts the 15-minute timer and syncs on resume, and stops the timer in the background. `SocialSync` gained `StopTimer` and `IsTimerRunning`.
- **Checked (2026-10-27):** Android emulator (Pixel 7, API 36): register, kill and reopen signed in, the token file encrypted, log out removes it; Release first frame 0.76-1.08 s, fresh-install migration 1.2 s. iOS Simulator (iPhone 18 Pro): Release starts with no errors; Debug sign-in, kill and reopen signed in, log out (by hand). A check-in survives the app being killed straight after (by hand). Typing with the Mac keyboard reaches iOS text boxes; simulated input from tools arrives late, which is the tool, not the app.

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

**As built:**
- **Phone mode** is a width, not a platform: `MainView` sets the `phone` class and `MainViewModel.IsPhone` under 700 px, so a narrow desktop window gets the tab bar too. Screens keep their `Sidebar`; a `.phone` style hides it. `MainView` holds the back bar (back button and `Title`) and the `TabBar`.
- **The back stack** stores how to open each screen, not the screen: going back opens it again with fresh data, as the screens' own back buttons always did. Opening a screen already in the stack goes back to it (Exercise's "Back" calls `ShowDay`, so no loops build up). A pushed screen stays under its root's tab (Day opened from Home keeps Home lit). Screens with popups implement `IClosesOnBack`: Home (check-in, undo, points history), Day (copy), Workouts and Splits (share), Inbox (add a shared split or workout), Map (gym picker).
- **More** lists Inbox too (with the unread badge; the More tab gets a dot), and Log out on the profile card, since the phones have no sidebar to hold them.
- **Sheets:** `Sheet` (a `Border`) replaces each popup's card. Under the `phone` class it drops its width, corners, margin and shadow, and its scrim's padding, and puts them back when the screen widens. The popup's own header already has the title and close button. Sheets inside a screen sit above the tab bar; only Feedback covers the whole shell.
- **Safe areas:** Avalonia 12 pads the top-level content by itself (`TopLevel.AutoSafeAreaPadding`); the phones turn that off and `MainView` pads itself, so the tab bar's background reaches behind the home indicator. Android draws edge to edge; the status bar takes the page colour for the theme.
- **Keyboard:** `MainView` moves up by the part of the screen the keyboard covers (zero when the system already shrank the window), hides the tab bar meanwhile, and scrolls the focused field into view. Avalonia has no username content type: the login box (username or email) uses Email, the register username turns off capitals and suggestions.
- **A second Android activity** (after back leaves the app and it's opened again) first reports the safe area in device pixels, before the window has its size, and never sends the corrected one; `MainView` reads it again on every resize.
- **Checked (2026-10-27):** Android emulator (Pixel 7, API 36, Debug): tab bar, sidebar hidden, More, Splits pushed with the back bar, Android back pops, then leaves the app at a tab root, Feedback as a full-screen sheet closed by back, the page colour behind the status bar and the gesture bar, insets right after the app is reopened. The emulator's keyboard stays in hardware-keyboard mode, so the keyboard wasn't checked there. iOS Simulator (iPhone 18 Pro, Debug): the page colour behind the Dynamic Island; the email keyboard on the login box, with the content moved up above it. By hand on iOS: the tabs work with Feedback open (a tab closes it; it used to stay over the new screen). Not checked by tool: iOS tabs and sheets (simulated taps arrive late), every screen's layout with the 44 pt buttons (16e), the desktop app by hand.
- **Touch (`PhoneTouch`):** many buttons have a size of their own that a style can't override, so on the phones every button gets a 44 × 44 minimum at a higher priority; desktop sizes don't change. Tooltips open on a long press. 16e still checks each screen's layout with the bigger buttons.

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

**As built, first review stop (2026-10-28):** the layouts follow the **GainStreak Mobile** design canvas (claude.ai artifact "GainStreak Mobile"; the Log in / Create account artboard was added for 16e). It changes the 16d shell, as decided with the user:
- **Shell:** tabs Splits, Workouts, Home (a raised circle in the middle, lime when current), Calories, Friends; no More tab. Every screen after sign-in gets the **app bar**: the logo (Home) and the streak, the brand, the inbox bell with its badge, the profile avatar. No back bar: pushed screens keep their own back button (Day's "Workouts"), and Android's back uses the stack. The lit tab is the current screen's own (Day lights Workouts even from Home); Profile and Inbox light none. The Friends tab holds the leaderboard and the map, with a Leaderboard / Map switch at the top of both. Profile ends with Send feedback and Log out on phones.
- **Sheets** come up from the bottom, as tall as their content up to 88% of the screen, rounded on top (the canvas), not full-screen.
- **Phone-only and wide-only parts:** the classes `phoneOnly` and `wideOnly` (GainStreakStyles). They can't hide an element that binds its own `IsVisible`; wrap it. Sizes the phones change move from the elements into named styles with the same desktop values, since a style can't beat a local value.
- **Login and Register:** no card; the brand large and centred over a lime glow, a Log in / Create account switch, a show-password button, "on this phone" in the privacy line.
- **Home:** already in the canvas's order; the phones get its sizes (16 px page, 26 px greeting, 88 px streak), This week without a card and the legend under the tiles, and the calorie goal as a chip that opens Calories.
- **Day:** the picker moves into a bottom sheet (86% high) behind "Add exercises", with "Copy a day" beside it and a Done button in the sheet. As on desktop, adding an exercise opens it; the canvas's sheet stays open and marks it "Added" instead (a behaviour change, so not made).
- **Check-in:** the keyboard's Next key goes weight → reps → the next set (phones only).
- **Checked:** Android emulator (Pixel 7, API 36), light: app bar, tab bar, Profile's log out, Log in and Create account, Home with a split chosen, Day and its add sheet, the check-in sheet and Next. iOS Simulator: the shell and Home start. Not yet: dark theme, 360 and 430 px widths, the largest text size, screenshots per screen. Found for 16f: iOS has no monospace font the app names ("0 / 4 done" falls back to a typewriter face).

**As built, screens 4 to 9 (2026-10-28):**
- **Tools for the rest:** `PhoneGrid` (a Grid whose columns change on phones, or whose children stack one under another, label above field). The shared page header (`Grid.pageHeader`, `TextBlock.pageTitle`, `.headerActions`): the title at 34 px on desktop and 28 on phones, the header's buttons beside it on desktop and under it on phones. On phones, cards have 16 px padding, card titles (`h2`) are 18 px, and the page has 16 px margins.
- **Exercise:** the set rows take the canvas's columns (#, type 116, kg, reps, delete 44 for the touch minimum) and drop the text boxes' 64 px minimum width; the date goes under the title. **Custom exercises:** unchanged (one column already).
- **Workouts:** the header's buttons under the title (they had squeezed the title out); the week tiles as on Home. **Splits:** no "Dashboard" back button on phones (a tab). **Split detail:** the week tiles fit; "Who uses it" puts the donut above its legend. **Split editor:** a row per day (the day, then its name) instead of seven columns.
- **Calories:** a 140 px ring, Goal and Eaten as rows with the number on the right. **Friends:** the privacy line wraps; leaderboard rows narrower, and the This week / All time switch under the title. **Map:** already fits; unchanged. **Inbox:** the full width.
- **Profile:** Body data puts each label above its field; the header's chips wrap; Male / Female lose their 170 px minimum; "Only on this phone" and "follows your phone's light or dark setting". **Points history:** the filter's words fit. **Feedback:** the bottom sheet as is.
- **Day:** the row number is gone on phones and the planned sets wrap under the group, so names fit at 360 px. **Home:** at 360 px the week tile's mark goes under the day.
- **Checked (2026-10-28):** Android emulator, light at 393 px wide: every screen above. Dark at 393: Profile, the Feedback sheet. Dark at 360: Home, Workouts, Day, Exercise's set rows, Calories, Friends. Not checked: 430 px, the largest text size, iOS for these screens, the desktop app by hand, and screenshots per screen per theme as files (they were looked at, not kept).
- **Left as they were:** Points history's row icons are taller than the canvas's, because Home's week-tile style reaches into the popup (on desktop too). The Map is a card in the page rather than filling the screen above the list.

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

**As built (2026-10-28):**
- **User-Agent:** `OsmHttp.UserAgent` is set at startup to `GainStreak/<version> (<platform>; student project)` (Android, iOS, macOS, Windows, Linux), for Nominatim, Overpass and the tiles alike, the desktop's too.
- **426 banner:** an "Update" button where the head can open its store (`CanOpenStorePage`), in "Restart now"'s place (they never show together). Android opens the Play Store app at `com.gainstreak.app`, or the web page without one. iOS needs the app's App Store id (`GymAppAppStoreId`, written into `BuiltSettings`) and hides the button until the listing exists (16h).
- **Feedback:** nothing to change; "Show log file" was already hidden on phones (16b).
- **Theme:** System already followed the phone live; checked both ways on the Android emulator (`cmd uimode night`) and the iOS Simulator (`simctl ui appearance`), status bar included.
- **Map, phones only:** a drag or pinch on the map no longer scrolls the page (`PreventGestureRecognition` on the way down; marking the events handled didn't stop Avalonia's scroll gesture). Zoom stops at street level in and a whole city out (`OverrideZoomBounds` 0.3 to 305 m per pixel), set once the map has zoomed to the city: set before the map had a size, it hung the app. The desktop keeps its free zoom. The map's gym card and Home's two toasts were a fixed 420 px wide and ran off a phone; they now shrink (the same 420 on desktop).
- **The iOS "typewriter" numbers (16e) are Menlo**, the system's monospace font, not a fallback: the shared list reaches it as meant. Matching the canvas's JetBrains Mono would mean bundling that font; not done.
- **Map checks:** none of the ten can be passed on the emulator or Simulator (see above). On the emulator: one-finger drag pans the map and not the page, the page still scrolls from outside the map, and tapping a gym opens its card. Double-tap couldn't be checked (the tool's taps are too far apart). The ten checks on a real Android phone and a real iPhone are still to do, by hand.
- **Not checked:** the "Update" button on a phone (it needs a 426 from the server) and the Play Store page opening.

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

**As built (2026-10-28):**
- **Planning:** `ReminderPlanner.Plan` (GymApp.Services) takes the settings, workout dates, schedule history, missed-day choices, the local time and a day-name lookup. It walks the next 7 days the way the streak would go if the user keeps to the plan. **Training day:** each day a workout is expected (a training day, a day owed under a chain, tomorrow after a shift), at the user's time, today only if nothing is logged and the time hasn't passed. **Missed day:** at 09:00 this morning while yesterday's miss waits for its choice, and the morning after each training day planned above while there's a streak at stake. The phone can't know ahead that a day will be missed, so it plans the 09:00 one and a check-in replans it away. `StreakCalculator.IsTrainingDay` is now public for it. Times are wall-clock (`PlannedReminder.At(zone)`), so 18:00 stays 18:00 across a daylight saving change.
- **Keeping them current:** `ReminderSync` replans and replaces the whole list at startup, on resume, on every screen change, on sign-in or sign-out (signed out: none), and on every change the sync hooks report (`ReminderHooks` wraps `SocialSync`: a check-in, an undo, a gym, a new split). Errors go to errors.log ("Reminders").
- **Profile → Reminders** (`RemindersViewModel`): hidden on the desktop (`INotificationScheduler.IsSupported`). Turning it on asks for permission; refused, the switch goes back off with "Notifications are off for GainStreak. Allow them in your phone's Settings, then turn Reminders on again." Then a switch each for Training days (with a 24-hour `TimePicker`, 5-minute steps) and Missed days. Every change saves and replans at once.
- **Android:** `AndroidNotificationScheduler` sets one inexact `SetAndAllowWhileIdle` alarm per reminder (no exact-alarm permission; Android may deliver up to an hour late) and remembers the ids in SharedPreferences to cancel them next time. `ReminderReceiver` posts on a "Reminders" channel with a flame icon (`ic_notification`). The tap opens `MainActivity`, which goes to Home (`GymApplication.OpenFromReminder`). `POST_NOTIFICATIONS` is requested from `MainActivity` on Android 13+. Alarms don't survive a reboot; the next start replans them (no boot receiver, so no `RECEIVE_BOOT_COMPLETED`).
- **iOS:** `IosNotificationScheduler` adds a `UNCalendarNotificationTrigger` per reminder (date and time, so wall-clock) after removing all pending ones. `ReminderDelegate`, set in `AppDelegate` before launching ends, shows a reminder that comes while the app is open and opens Home on a tap.
- **Schema:** the four `User` fields, `TrainingReminderTime` a `TimeOnly`. **Migration:** `AddReminders` in the app's SQLite; existing users get Reminders off, both kinds on and 18:00. Compiled model regenerated.
- **Tests:** `ReminderPlannerTests` (13, the list above), `RemindersTests` (6: defaults, turning on asks and schedules, refusal, saved time and kinds, sign-out clears, desktop has none).
- **Checked (2026-10-28):** Android emulator: the card, the permission prompt, refusal turning the switch back off with the note, allowing, the alarms in `dumpsys alarm` (today 18:00, the week's training days at 18:00, the 09:00 missed-day ones), a reminder posted (fired through a temporarily exported receiver) and its tap opening Home, a check-in dropping today's alarm, startup replanning the same list. iOS Simulator, by hand: the card, the prompt, Don't Allow turning the switch back off (iOS asks only once; reinstalling asks again), Allow, a reminder delivered at the picked time and its tap opening Home. Not checked: a real alarm firing on time, after a reboot, daylight saving on a device.

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
- Rate limits behind shared IPs: mobile carriers put many phones behind one IP, so "register 5 an hour per IP" will hit real users. Fix before the public release. **Done in 0.21.1** for sign-ups (only created accounts count, 30 an hour per address or IPv6 /64, a Discord alert past 100 an hour); App Attest / Play Integrity, or requiring a confirmed email before friend requests, stay options if the alert ever fires.
- Push notifications for friend requests (needs APNs/FCM and server work).
- Background sync, home-screen widgets (streak), Apple Health / Health Connect.
- GPS: "you're at a gym, check in?" (a privacy decision of its own).
- Workout sync between devices (would need Guid ids for rows created offline).
- Retiring or slimming the desktop head, if mobile makes it unnecessary. **Decided 2026-11-11:** the desktop is frozen at 0.21.0 (rules in CLAUDE.md, "Desktop freeze") and kept as is for the developer's uni presentation. After it, slim it down: drop the desktop layout branches in the shared views, the sidebar, Velopack and the desktop release scripts, keeping a bare window that hosts the EF commands and the TestBench (or move those elsewhere), then allow `MinClientVersion` above 0.21.0.
