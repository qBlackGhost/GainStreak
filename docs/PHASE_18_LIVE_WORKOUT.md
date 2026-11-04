# Phase 18: Live workouts and "verified at gym" — spec for Claude Code

This is the spec for GymApp Phase 18 (version 0.18.0). On phones, logging moves from "fill in the check-in afterwards" to a live workout run from Home while the user is at the gym, one exercise and one set at a time. While a live workout runs, the phone's location is checked against the user's gyms, on the phone only; a workout done near one is **verified at gym** and earns full points. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase": `dotnet build` and the app tests after each sub-phase; the full `dotnet test`, both mobile heads (Android emulator and iOS Simulator, Release) and the desktop app at the end of the phase. After each sub-phase, report:
  - which files you created
  - which files you changed or moved
  - which migration you added, and to which database (all of this phase is the app's SQLite; the server doesn't change)
  - what you checked by hand on each platform, and what you couldn't check
- After any change to the app's entities, regenerate the compiled model (`dotnet ef dbcontext optimize`, see CLAUDE.md) and test the iOS Simulator with a **Release** build.
- Read `HomeViewModel`, `HomeView`, `CheckInViewModel`, `CheckInView`, `WorkoutService` (`GetCheckInAsync`, `LogTodayAsync`, `UndoTodayAsync`, `AutomaticGymAsync`), `SetService.SaveDayAsync`, `CheckInRule`, `PointsCalculator`, `PointsService`, `StreakService`, `GymService` (custom gyms), `GymPickerViewModel` (the address search), `INotificationScheduler` and both head implementations, `ReminderPlanner`, `IPlatform`, the two head platforms, `docs/streak-grace-rule.md` and `site/privacy.html` before changing anything.
- Every existing test must still pass, except where this spec changes a rule on purpose (the points for a workout, the custom gym address); update those tests and say which.

## Decisions already made (don't change these)

1. **On phones, the live workout is the only way to log a workout.** The "I went today" check-in screen stays on desktop only. This deliberately breaks Phase 16 decision 2 ("phone and desktop differ in layout only") for logging: say so in the changelog.
2. **Phones only.** Desktop gets neither the live workout nor location. "Phone" means the head, not the layout width: a narrow desktop window keeps the check-in screen. Decide it through `IPlatform` (like `INotificationScheduler.IsSupported`), never through `MainView.IsPhoneLayout`.
3. **Verification is a mark, not a gate.** A live workout can be started and logged anywhere. One done near one of the user's gyms is marked verified; one that isn't still logs, keeps the streak and earns reduced points.
4. **Points depend on how much of the workout was done and on verification.** 50% to 69% of the day's exercises filled earns half the workout points; 70% or more earns the full points. A verified workout earns those points; an unverified one, from a phone or the desktop, earns reduced points. Workouts logged before 0.18.0 keep their 5 points: recalculating history never changes them. The 50% rule for logging at all (`CheckInRule`) and the streak don't change.
5. **The app pushes for the full workout.** During a live workout nothing says the workout already counts. The card shows progress toward the full workout, and finishing below 70% asks first and says what the user would miss.
6. **Location never leaves the phone.** Positions aren't stored, logged, sent to the server, or sent to OpenStreetMap or Nominatim. Only the result is kept: the workout is verified, and at which gym. No background location: the app reads it only while a live workout is open on screen.
7. **Custom gyms need an address** picked from the address search, so every custom gym has a position.
8. **Logging is still today-only** (`docs/streak-grace-rule.md`). Nothing in this phase writes a workout for a past date.
9. **No rest timer** in this phase.
10. **The server doesn't change.** No `Contracts` change, no server migration.

## Defaults to confirm (change here if wrong)

- **The points table.** Only a verified workout (or one logged before 0.18.0) with 70% or more earns the full 5; everything else that counts earns 2:

  | | Verified | Not verified |
  |---|---|---|
  | **70% or more** | 5 | 2 |
  | **50% to 69%** | 2 | 2 |

  `PointsCalculator.WorkoutPointsFor(completion, verified)`. 70% is rounded up to whole exercises, like the 50% rule: 3 exercises → 3, 4 → 3, 5 → 4, 6 → 5. `CheckInRule.RequiredForFull(total)`.
- **The week-complete bonus (+50 / +25) and cardio points don't depend on completion or verification.**
- **Behind the scenes the workout is logged as soon as half the exercises are filled, without telling the user.** At that moment the app writes today's `WorkoutLog` (the same rule as `CheckInRule`), then updates its completion as more exercises are filled, and its verification and cardio when they come. Finish only closes the session. A session that is stopped, times out or is killed at that point needs nothing written later, so decision 8 holds without exceptions. The card never shows that this happened (decision 5).
- **Completion is stored on the log**, not recalculated from the sets later, because the plan for that day can be edited afterwards. It's updated whenever today's sets change (the live card, the Day and Exercise screens, the desktop check-in), today only. Past days never change.
- **Desktop check-ins follow the same table**, always unverified.
- **Radius:** within 150 m of an OpenStreetMap gym and 250 m of a custom gym (addresses are approximate), plus the fix's reported accuracy. A fix with accuracy worse than 200 m is ignored.
- **When location is read:** at Start workout and at each Next until the workout is verified, then never again in that session. One fix each time, with a 10-second limit.
- **Which gyms count:** every active gym in the local database (the cached OpenStreetMap gyms of the user's cities and their own custom gyms). No lookup around the user's position (that would send it to OpenStreetMap).
- **The verified gym becomes the workout's gym** (`WorkoutLog.GymId`), so the "which gym?" question after logging isn't asked for it, and the friends' gym check-in uses it. An unverified workout keeps today's behaviour (`AutomaticGymAsync`, then the gym question).
- **Nudge after 12 minutes** away from the app, and **the session stops after 4 hours** or at midnight, whichever comes first.
- **Skip for now:** the card lets the user skip an exercise (the machine is taken) and come back to it after the others.
- **Any exercise, in any order:** tapping an exercise in the card's list puts it on the card, so a skipped one can be done whenever its machine frees up. Finishing it hands the card back to the plan's order. Tapping a finished exercise opens it for changes ("Next: Leg press" takes the user on).
- **Focus mode:** while a workout runs, Home shows only the top bar, the streak card with its arc and the logging card, plus a line that calories, PRs and friends come back when it ends. The greeting, the level card, This week, Calories, Recent PRs and Friends are hidden. The level card must stay hidden anyway: its points would jump when the workout is silently logged at half (decision 5). The tab bar stays, so other tabs can still be opened.
- **Set chips** show only the set and its numbers ("1 90 × 8"); their state is in the style: logged sets tinted, the set to do now outlined, later sets dashed. All except later sets can be tapped; tapping the current one goes back to logging it.
- **Logged sets can be changed:** tapping a logged chip loads its weight and reps into the fields. Until they differ, the only button is **Logged ✓**, which goes back to the current set; once they differ, **Cancel** and **Save set** replace it. Saving goes through `SetService` like any set, so limits and PRs stay right, and completion and points follow (today only).
- **Bar exercises** (`BarWeight.UsesBar`) get the Exercise screen's Total / Per side switch and bar weight above the fields (artboard "Per side bar options", option 1), with the total shown under a per side weight. The set always saves the total. The mode is chosen before the exercise's first set; once a set is saved it's locked for that exercise (a lock line says "Per side · 20 kg bar"), so every set of it is typed the same way. It's the same setting the Exercise screen uses.
- **Undo** stays as it is. Undoing today's log also drops its verification; a new live workout can be started the same day.
- **Bump `MinClientVersion` to 0.18.0** when the phones ship, so pre-0.18 installs (which award full points for everything) stop uploading stats. This gates desktop testers too, which is fine because Velopack updates them on restart.

---

## Design first

The Home screen during a workout is designed on the **GainStreak Mobile** design canvas (claude.ai artifact "GainStreak Mobile", artboard **Home during a workout**) and approved by the user before 18a starts. Build it as the approved artboard shows; where it and this spec disagree, ask.

## 18a. The live workout card (no location yet)

**Data (app SQLite, one migration):**
- `LiveWorkout`: `Id`, `UserId`, `Date`, `SplitDayId`, `StartedAt`, `LastActiveAt`, `CurrentExerciseId` (nullable), `SkippedExerciseIds` (a short text list), `EndedAt` (nullable). At most one open session per user (unique filtered index on `UserId` where `EndedAt` is null).
- `WorkoutLog.Verified` (`bool?`): null for workouts logged before this phase, false for unverified, true for verified. 18a writes false; 18c starts writing true.
- `WorkoutLog.FilledExercises` and `WorkoutLog.TotalExercises` (`int?`): null for workouts logged before this phase (full points).

**Rules (`GymApp.Core/LiveWorkoutRules`, pure and tested):**
- The next set to show: the current exercise's next empty set (warm-ups first, then working sets), then the next exercise in plan order that isn't filled or skipped, then the skipped ones, and finally "all done".
- When the session must stop: 4 hours after `StartedAt`, or the date changed. `AppClock` only.
- The outcome of ending a session: `Full` (70% or more), `Half` (50% to 69%; both already logged) or `NotCounted`.

**Service (`LiveWorkoutService` in GymApp.Services):** start (picks today's planned day, or asks which day like the check-in does), save a set through `SetService` (so limits, bar settings and PRs behave exactly as today), skip, end, get the open session. As soon as `CheckInRule` is met it calls `WorkoutService.LogTodayAsync` with `Verified = false`; after that every saved set updates the log's completion and recalculates points.

**Points (`PointsCalculator`, `PointsService`):** the workout award uses the table in Defaults. The points history labels it "Workout", "Workout (partial)", and from 18c "… at Iron Gym" or "… not verified".

**Home, phones only:**
- **The streak card's semicircle shows the workout's progress.** While a workout runs, the arc fills with the fire gradient in proportion to the exercises filled (1 of 3 fills a third), animating as each exercise is completed. Under it: "WORKOUT IN PROGRESS", the day name and "2 of 4 exercises". Nothing marks 50% or 70% on the arc. After Finish it keeps the final share.
- **The logging card sits directly under the streak card** while a workout runs, and the "Today's workout" card is hidden then (it shows again after Finish).
- **Before starting:** the streak card as today, with **Start workout** in place of "I went today". Rest day: "Rest day" and **Start workout anyway** (picks a day like the check-in's day picker). No split or no workouts built: the same messages and buttons the check-in shows today.
- **During:** the card shows "Bench press · Set 2 of 4" (warm-up sets labelled), weight prefilled from last time (`GetLastSessionAsync`), reps empty, and **Next**. Next is enabled once both are valid. Below: the exercises as a short list with ticks, **Skip for now**, and a small **End workout** link. A progress bar toward the whole workout: "3 of 6 exercises". Nothing on the card mentions half, 70%, "counted" or points while the workout runs.
- **Finish** appears next to Next once half the exercises are filled (the user's rule for being allowed to finish), and becomes the main button once every exercise is filled. Cardio is asked at Finish (the field the check-in has today) and saved onto today's log.
- **Finish below 70% on a verified workout** asks first: "You've done 4 of 6 exercises. Finish now for half points, or do 1 more for full points." **Keep going** (the main button) / **Finish anyway**.
- **Finish before every exercise is done, otherwise** (70% or more, or an unverified workout, where finishing now earns the same points) asks lightly: "1 exercise left. Finish it before you go?" **Keep going** (the main button) / **Finish anyway**. The app still pushes for the whole workout.
- **End workout** with less than half filled asks first: "End without logging? Your sets are kept, but this workout won't count and earns no points. Your streak is at risk unless you finish a workout today or tomorrow." **Keep going** / **End workout**. The sets are kept.
- **After Finish:** the card shows today's summary with the points earned ("+5 pts", or "+2 pts · half points, 4 of 6 exercises"), as Home does after a check-in today, with Undo. This is the first place the result appears.
- The "I went today" button and `CheckInView` don't show on phones.
- Rest of Home stays below the card.

**Tests:** `LiveWorkoutRules` (order, skips, warm-ups, stop at 4 h and midnight); `CheckInRule.RequiredForFull` (1 to 10 exercises); `LiveWorkoutService` on in-memory SQLite (start, sets go through `SetService`, the log is written exactly once at half, completion follows later sets, end without logging keeps the sets, one open session per user); `PointsCalculator` for every cell of the table and for old logs (nulls → 5); completion updated from the Day screen and the desktop check-in; `CompiledModelTests`.

**Stop and report.**

## 18b. The nudge and the return popup

- **`INotificationScheduler`** gets `ScheduleWorkoutNudge(DateTimeOffset at)` and `CancelWorkoutNudge()`, with their own notification id so `Replace` (reminders) never cancels it and it never cancels reminders. Desktop and tests: no-op.
- **Permission:** asked the first time the user taps Start workout. Refused: nothing changes except that no notification comes; don't ask again from Start (Profile → Reminders still can).
- **When the app goes to the background** with an open session that has at least one set: schedule the nudge for 12 minutes later, text "Continue your workout?", body "Bench press is next." Coming back to the foreground cancels it. Tapping it opens Home.
- **Coming back** (foreground, or cold start) after 12 minutes or more away with an open session: a popup over Home:
  - Title "Continue your workout?", the counter ("3 of 6 exercises").
  - **Continue**: back to the card; the clock restarts.
  - **Finish workout early**: with half or more filled, ends the session as Finish does; below 70% the popup first says "Finishing now earns half points. Do 1 more exercise for full points." With less than half, the popup says beforehand: "This workout won't count and earns no points. Your sets are kept. Your streak is at risk unless you finish a workout today or tomorrow." The user must see this line before choosing.
- **Stopping at 4 hours or midnight:** checked when the app comes to the foreground and once a minute while it's open. The session ends by itself. Next time Home shows, a one-time note says what happened: "Your workout stopped after 4 hours. You did 6 of 6 exercises: +5 pts.", "…You did 4 of 6 exercises: half points, +2 pts." or "…It didn't count: fewer than half the exercises were filled. Finish a workout today or tomorrow to keep your streak."
- Session state lives in `LiveWorkout`, so all of this works after the phone kills the app.

**Tests:** the nudge is scheduled and cancelled at the right moments (a fake scheduler), reminders and the nudge don't cancel each other, the popup appears only after 12 minutes, both stop notes.

**Stop and report.** Check on both heads: background → notification after 12 minutes (shorten it with a TestBench or debug setting for the check), tap opens Home with the popup, kill the app mid-workout and reopen.

## 18c. Location and "verified at gym"

- **`ILocationProvider`** in GymApp.App: `IsSupported`, `RequestPermissionAsync()`, `GetFixAsync(TimeSpan timeout)` returning latitude, longitude and accuracy, or null. Desktop: not supported.
  - **iOS:** `CLLocationManager`, when-in-use only, `NSLocationWhenInUseUsageDescription`: "GainStreak checks that you're at one of your gyms during a workout. Your location stays on your phone."
  - **Android:** `ACCESS_FINE_LOCATION` and `ACCESS_COARSE_LOCATION` (both must be requested on Android 12+), `LocationManager` (no Google Play Services dependency). No background location permission.
  - **TestBench:** a fake provider with "At gym…" (pick a gym) / "Away" / "No permission", next to the clock shift.
- **Permission:** asked at the first Start workout, after the notification permission. Refused: workouts run unverified, and the card says "Location is off, so this workout can't be verified" with a link to the phone's Settings.
  - **Android shows a note first** ("Verify your workouts": why it's asked, that the location stays on the phone, and to pick Precise), because its prompt has no word from the app. **Continue** opens the prompt; **Not now** skips it. iOS needs none: its prompt shows `NSLocationWhenInUseUsageDescription`.
  - **Approximate only** (approximate picked on Android, Precise Location off on iOS): fixes are too vague, so the card doesn't read them and says "Turn on precise location to verify your workouts. Your location stays on your phone." with Open Settings.
  - **A fix too vague to count** (worse than 200 m, often indoors) isn't taken for being elsewhere: the card says it couldn't find the location yet and tries again at the next set.
- **Rule (`GymApp.Core/GymProximity`, pure):** the nearest gym within its radius (see Defaults), given a fix and the gyms with positions. Haversine distance.
- **When the workout is verified:** the card shows "At Iron Gym ✓". If the log already exists, it's updated to `Verified = true` and `GymId` = that gym (marking the gym check-in dirty for sync, as `SetGymForTodayAsync` does).
- **Not near a gym at Start:** the card says "You're not at one of your gyms. This workout won't be verified." with **Add this gym**, which opens the custom gym form with the address required. Each Next tries again until it's verified.
- **Points:** the verification column of the table starts to matter: `Verified` true or null → verified; false → not verified. The points history shows "Workout at Iron Gym" or "Workout (not verified)", with "(partial)" when below 70%. Desktop check-ins are written with `Verified = false`.
- **Custom gyms:** the address is required in the add and edit forms ("Pick the gym's address so workouts there can be verified."). Existing custom gyms without one keep working, and Gyms shows "Add an address so workouts here can be verified" on them.

**Tests:** `GymProximity` (inside, outside, accuracy, the two radii, poor fixes ignored); verification updates an existing log; points for every cell of the table and for old workouts, including a recalculation that leaves old history unchanged; custom gym without address refused.

**Stop and report.** Check with the Simulator's and emulator's location setting: at a cached gym, away, permission refused.

## 18d. Privacy, store and release

- `site/privacy.html`: what location is used for, that it is read only during a live workout, and that it stays on the phone.
- `docs/STORE_LISTING.md`: the App Store privacy answers (location: not collected, since it isn't sent off the device) and the Google Play data safety form (location accessed, not collected or shared). Write what's needed and flag anything that needs a decision.
- README: the TestBench location setting.
- `CHANGELOG.md` 0.18.0 entry for testers. It must explain: logging on phones is now the live workout; the points table (half points from 50%, full points from 70%, reduced when not verified); the nudge; the 4-hour and midnight stop; that desktop workouts now earn reduced points. End with `**Migration:** app database (live workouts, workout verification).`
- `MinClientVersion` (see Defaults): prepare it, and say in the report when it should go live.

**Going live (prepared in 18d, not done).** The server doesn't change, so there's nothing to migrate or deploy on it. In this order:
1. Release 0.18.0 for every head: the desktop through Velopack (`build/release.sh`, then `build/upload-github.sh`), Android to Play internal testing, iOS to TestFlight. Wait until each one is available to testers.
2. Then turn older builds away, so pre-0.18 installs (which still award +5 for everything) stop uploading stats:
   ```bash
   gcloud run services update gymapp-server --region europe-west1 --project gainstreak-beta-1234 --update-env-vars MinClientVersion=0.18.0
   ```
   Desktop testers update on their next restart. A phone on 0.17 shows "please update" until it installs 0.18.0, and its workouts keep working offline meanwhile.
3. Publish `site/` (the new privacy section) to the releases repository's `docs/` before the phone builds reach testers: the iOS prompt and the store answers point to it.

**Stop and report**, with a list of what must be checked on real phones (GPS indoors at a real gym, precise location off on iOS, the nudge on a locked phone).

## Later (not this phase)

- A rest timer between sets.
- "You're at Iron Gym. Start Push day?" when the user arrives (needs background location, a privacy decision of its own).
- Friends seeing each other's verified count on the leaderboard (a `Contracts` change).
- Reordering exercises during a live workout beyond Skip for now.
