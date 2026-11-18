# Phase 24: Home Screen widgets and Control Center controls on the iPhone — spec and as built

Version 0.24.0. iPhone only (the desktop is frozen, Android has none). The design is the "iOS widgets" row of the
GainStreak Mobile canvas: Home Screen, Edit Widget, Control Center and the gallery of every size.

## What the user asked for

- **Content:** streak, level and points, today's plan; weight and steps from Apple Health.
- **Surfaces:** Home Screen small, medium and large; Control Center controls.
- **Interaction:** a tap opens the app on the matching screen; Edit Widget chooses what a widget shows.
- Not wanted: weekly activity, recent PRs, friends, check-ins, calories, Lock Screen widgets, StandBy, buttons that act
  inside the widget.

## Decisions

1. **The app writes, the extension draws.** The app builds a `WidgetSnapshot` (`GymApp.Services`, same rules as Home)
   and saves it as `widget.json` in the App Group `group.<bundle id>`; the extension never opens the database. A wire
   format between C# and Swift: add fields freely, rename or remove one only on both sides.
2. **One extension.** The widgets and controls live in the Live Activity's extension
   (`src/GymApp.iOS.LiveActivity/Widget/HomeWidgets.swift`), still built by `build.sh` with swiftc and no Xcode project.
   It stays at iOS 16.2 for the Live Activity; the widgets are `if #available(iOS 17.0, *)` (App Intent configuration)
   and the controls `iOS 18.0`.
3. **App Intents without Xcode.** Edit Widget needs `Metadata.appintents` in the extension. `build.sh` compiles
   whole-module with `-emit-const-values-path` and runs `appintentsmetadataprocessor` itself, failing the build if the
   metadata is missing.
4. **When it refreshes:** every screen change, going to the background, the sync hooks (stats and check-ins), and
   sign-in or sign-out (`WidgetSync`, like `ReminderSync`). The bridge's `gs_widgets_reload` reloads the timelines only
   when the JSON changed. Each timeline has three entries: now and the next two midnights, so after midnight the widget
   shows tomorrow's plan from the snapshot. When yesterday was still open, or the snapshot is older, it asks to open the
   app instead of guessing the streak.
5. **Links:** `gainstreak://open/<home|plan|points|body|start>` (`GymApp.Core.WidgetLink`, the `CFBundleURLTypes` in
   the head's Info.plist). `MainViewModel.OpenLink` handles them; before sign-in the target waits for the first
   signed-in screen, like an invite link. Controls can't open a URL directly: their button runs `OpenGainStreakIntent`,
   which returns an `OpenURLIntent`.
6. **Steps** are read by the app (one Health query per day for the last 7 days, so days without samples count 0) only
   when Apple Health is on in Profile. Weight comes from the app's own weigh-ins (Health's are imported into them).
7. **The split isn't configurable.** The design's Edit Widget had a "Split" row; the plan always follows the active
   split, as Home does. Choosing another split would show a plan the app doesn't follow.

## Signing

- The app gets `com.apple.security.application-groups` = `group.<ApplicationId>` from the csproj
  (`-p:WidgetAppGroup=false` builds without it). The Simulator's ad-hoc build gives the extension the same group
  through an entitlements file `build.sh` writes.
- **Device and TestFlight builds:** both App IDs (`com.gainstreak.app` and `com.gainstreak.app.LiveActivity`, or the
  `.dev` ones for the free account) need the **App Groups** capability with `group.<app bundle id>`, and profiles made
  after it was added. `build.sh` warns when the extension's profile has no group. With a free account, add App Groups
  to both targets of the throwaway Xcode project in `build/run-on-phone.sh`'s step 3.

## Checks

- `WidgetTests.cs`: the link parser, the snapshot (statuses, rest day, no split, last time's set, weight and units,
  steps, JSON) and the shell opening each target, before and after sign-in.
- By hand on the Simulator (iOS 18+): add each widget and control, Edit Widget's choices, a tap on each, light and
  dark, logging a workout, signing out.
