# Future: 0.28 backlog — data safety, live workout, beginner help, recap, social (planned 0.28.x)

**Status: planned, not started.** A list of approved ideas for the 0.28 update, in the order to build them. Not a phase
spec yet: before work starts, each item gets its design reviewed (drafted as options for the developer to pick from),
the items marked "open" get their design chosen, and this file is split into phase specs (`PHASE_<N>_*.md`). The
live workout updates (section 2) were first planned as 0.27.2 and now belong to 0.28.

Phones only (the desktop is frozen, CLAUDE.md "Desktop freeze"): gate anything the desktop can't show through
`IPlatform`, and don't build a desktop layout. The desktop only has to keep building and starting.

## Ground rules (when this becomes a spec)

- Do not run `git commit` or `git push`.
- Checks follow CLAUDE.md "Checks, for every phase". Every sub-phase that changes an entity also regenerates the
  compiled model (`dotnet ef dbcontext optimize ...`) and gets an iOS Release build for the Simulator (full AOT).
- Keep `Contracts` backward compatible: only add optional fields, never raise `MinClientVersion` above 0.21.0.
  `ClientContractTests` and `ClientVersionTests` must pass.
- Scrolling stays smooth: after each sub-phase that adds or changes a list or a long screen, scroll it on both phones
  (Release build) and check for stutter or lag (CLAUDE.md, "Checks, for every phase"). The new screens in this doc
  (backup, tips, recap, set rows with last-time text) are the ones most likely to need it.
- Use `AppClock`, never `DateTime.Now`. A new segmented toggle uses `Views/SegmentedPill`.
- Every user-visible change gets a `CHANGELOG.md` entry ending with a `**Migration:**` line when there is one. Commit
  subjects start with the version.

## Ruled out (don't add these)

Rest timer (no per-exercise timers, rest notifications or countdown, and a drop set does not "skip" anything), plate
calculator, RPE or reps in reserve, and the deload prompt. Supersets and import from notes are deferred, not part of
0.28 unless the developer says so (don't add a `SupersetGroup` or an import screen).

## 0. Gym data cleanup and the override layer (build first)

The gym list is a copy of OpenStreetMap, imported weekly on the server (`GymImport`, `OsmGyms`, served by `GET /v1/gyms`),
so it is only as fresh as OpenStreetMap: a closed gym stays until a volunteer removes it, and a new one is missing until
a volunteer adds it (example: Gym One 1 in Timișoara is at an old location that closed years ago, and its new location
is not listed). It matters beyond the map: a live workout is verified by being near a gym, so a missing gym means an
unverified workout and reduced points.

**Plan (decided): a read-only pilot in Timișoara, then a reviewed override layer, then the other cities.**

### 0a. Pilot in Timișoara (read-only, changes nothing)

- Fetch Timișoara's gyms read-only: from the public OpenStreetMap servers (Overpass), or from a list exported from the
  server if the developer provides one.
- For each gym, search the web (its site, Facebook page, listings) to check whether it still seems open and where it is
  now. Also look for well-known gyms missing from the list (the big chains and well-reviewed local gyms).
- Write a **review table** (a working file in `docs/`, for example `docs/GYM_REVIEW_TIMISOARA.md`, not committed until
  the developer has reviewed it): one line per gym with the status (still open, closed, moved, missing from the list,
  unsure), what was found, source links, a confidence level, and for a moved or missing gym its address and coordinates.
- Rules for the research: no copying from Google Maps or other sources whose terms forbid storing the data; coordinates
  come from an address lookup on OpenStreetMap's public geocoder (with its usage limits); anything not clear stays
  "unsure" and is never guessed. Small gyms with only a Facebook page are the weak spot; say so in the table.
- **Nothing in any database is changed in this step.**

### 0b. Review, then the override layer

- The developer approves or rejects each line of the table.
- Approved changes go into an **override layer on the server**, not into the `OsmGyms` rows: the weekly import
  re-syncs `OsmGyms` by OpenStreetMap id and would undo a direct edit. The layer (a new server table, with a migration;
  names to be designed) can **hide** an OpenStreetMap gym (closed or moved), **rename** one (same place, new owner or
  name; it keeps the OpenStreetMap id, so past workouts keep pointing to the same gym; decided in the Timișoara review)
  and **add** a gym that has no OpenStreetMap id (new or missing). `GET /v1/gyms` applies it after the import data, so
  it survives every import.
- **When OpenStreetMap catches up** (the developer may fix gyms there too, `docs/OSM_EDITING_GUIDE.md`), an override
  must not double up: the import should report an added gym that now has an OpenStreetMap gym within about 50 m, a
  rename whose OpenStreetMap name now matches, and a hide whose gym is gone from the data, so the developer can drop
  that override line.
- Applied only through a reviewed change the developer can read first (a migration or an admin command like
  `migrate`), never by editing the live database by hand. Production changes always need the developer's go-ahead.
- **Contracts and the app:** an added gym has no `OsmType` or `OsmId`, so the gym record sent to the app needs an
  optional id of the server's own for these (additive, old builds ignore it and never call this endpoint anyway;
  confirm with the `server-compat-reviewer` agent). The app's local `Gym` (and the rule that a gym a `WorkoutLog`
  points to is never deleted, only deactivated) must handle a hidden gym and an added one, including a hidden gym that
  past workouts point to. Tests: the layer survives an import, a hidden gym leaves the box response, a renamed gym keeps
  its id with the new name, an added gym appears, and the app deactivates a hidden gym without deleting it.
- Both phones: the work is the server plus shared client code, with no platform-specific part.
- Attribution: the OpenStreetMap data stays under its license (credit it where the map credits it); the added gyms
  are the app's own data.

### 0c. The other cities

- If the pilot works well, repeat 0a and 0b for the other Romanian cities, biggest first (București, Cluj-Napoca,
  Iași, Constanța and so on), one review table per city. Each city goes through the same review before anything is
  applied.

### Not decided yet (ideas for later)

In-app **Report** buttons ("Closed", "Moved", "Wrong name") and an **Add a gym here** button feeding the same override
layer, promotion from several users' verified workouts, showing how long ago a gym was last edited, and loading more
countries on the server so the map is fast everywhere. None of these is in 0.28 until the developer picks them.

## 1. Data safety

Workouts live only on the device, so a lost phone loses all history. Build this right after the gym data cleanup (section 0), since it protects the most.

- **Backup and restore.** Back up the app's SQLite data (and the session settings that matter) to a file the user
  chooses, or to iCloud on iOS. Restore replaces local data after a clear confirmation. Open design points: what the
  backup contains (everything local: workouts, sets, PRs, body data, calories; not the account token), a format version
  so a newer app can read an older backup, and what happens when a backup is from a newer schema than the app.
  Restore must go through the same migrations as a fresh start and be tested against a database with every migration
  applied.
- **Export to CSV.** One file per kind of data (sets, body weight, calories), readable in a spreadsheet, shared through
  the phone's share sheet. Read-only, no schema change.

## 2. Live workout updates

Order: drop sets (2a), per hand (2b), last time on the set (2c), quick swap (2d), then the requested live workout
changes (2e to 2l). Each is reviewed before the next starts. Read before changing anything: `LiveWorkoutCardView.axaml` and its view model, `LiveWorkout`,
`LiveWorkoutWatcher`, `SetLog`, `PrCalculator` (`SetKind`, `SetEntry`), `Exercise`, `UserDayExercise`,
`ExerciseBarSetting`, `Contracts/Shares.cs` (`SharedExercise`) and `SplitShareService`.

### 2a. Drop sets

A drop set is a lighter set done right after a working set on the same exercise, with no break between them.

- `SetKind` gains `Drop`. Stored as an int, so the app's SQLite needs no data change for existing rows; confirm the
  column type and the compiled model.
- A drop set's weight is lower than the set it follows. It is shown under that set in the live workout and in the
  workout history.
- **Counting rules (confirmed):** drops count toward volume and points like a working set, but **never toward PRs**
  (`PrCalculator` ignores `SetKind.Drop` for best-set and 1RM). Warmups keep their existing rules.
- **Design: open.** Three options were drafted:
  - **A.** A "+ Drop" button after a logged working set; the drop row sits under its set, weight prefilled about 20%
    lower, same reps.
  - **B.** Drop as a third type next to Warmup and Working in the set-type pill.
  - **C.** Chained drop mode: after a drop, offer "Another drop" and "Finish drop set".
  The developer's lean was A with C's chaining. Replace this block with the chosen design.
- Tests: `PrCalculator` ignores drops; points and volume include them; a live workout with a drop set survives the
  app being killed and reopened (the `LiveWorkout` rule).
- **Migration:** none expected (enum value only). Say so in the changelog if that holds.

### 2b. Per hand (unilateral exercises)

- `Exercise` gains `IsUnilateral` (bool, default false). It is a **toggle on the exercise** (built-in and custom), set
  in the exercise editor; built-ins that are clearly one-sided (one-hand pushdown, preacher hammer curl, single-arm
  rows) are seeded with it on.
- **How a set is logged: open.**
  - **B (drafted lean).** One set, one weight, two rep fields: `Reps` and `RepsLeft` (nullable; null on bilateral
    exercises). Shown as "20 kg · L 12 · R 10". One new nullable column on `SetLog`.
  - **A.** Label only: "per hand" in the weight label, nothing else changes.
  - **C.** Two sets per number (left, then right), different weight allowed.
  Replace this block with the chosen design.
- **Counting rules (confirmed):** a per hand set counts **once** for points and volume; the **better side** is the PR
  set. Define "better side" as the side with more reps at that weight, and test it.
- Sharing: `SharedExercise` gets an optional `IsUnilateral` (default false) so a shared split keeps the flag. Old
  clients ignore it, so no `MinClientVersion` bump; run `ClientContractTests`. Run the `server-compat-reviewer` agent.
- **Migration:** app SQLite (`IsUnilateral`, `RepsLeft`), plus regenerate the compiled model.

### 2c. Last time on the set

Show what the user did last time, on the set they are about to log.

- No schema change. On opening an exercise in the live workout, load the most recent date before today that has
  `SetLog` rows for it, and match sets by kind and number: working set 1 shows last time's working set 1, and so on.
  A drop set compares to last time's drop; a per hand set compares left and right separately.
- Each set row shows "Last: 80 kg × 8" (or "Last: 20 kg · L 12 · R 10" for per hand).
- **Prefill:** the weight and reps fields start from that set. The user taps Log if it's the same or edits it.
- **"Beat it" hint:** optional, a quiet nudge when the entered weight is higher, or reps higher at the same weight,
  than last time. Reuse `PrCalculator`'s comparison, not new maths. No hint when there is no history.
- No history for the exercise: nothing is shown and the fields behave as today.
- Tests: matching by set number when the counts differ (last time 4 sets, today's plan 3), a skipped exercise, a
  warmup set not matching a working set, and the date lookup across a vacation or a gap.

### 2d. Quick swap

Replace an exercise mid-workout (machine taken) without editing the split.

- A "Swap" action on the current exercise in the live card opens the exercise picker (the same one used to pick a rest
  day's exercises).
- The swap is **for this session only**: the split and `UserDayExercise` rows are unchanged. The sets logged from then
  on belong to the new exercise.
- `LiveWorkout` must remember it so it survives the app being killed: a new nullable column that maps the planned
  exercise id to the replacement (for example `SwappedExerciseIds`, like `SkippedExerciseIds`). Rules to settle in the
  design review: what happens to sets already logged on the original exercise (kept on it), whether the replacement
  inherits the planned warmup and working set counts (yes, by default), and whether a swap can be undone.
- **Design: open.** Decide where the Swap action lives in the card and what the list looks like; draft options before
  building.
- **Migration:** app SQLite (`LiveWorkout`), plus regenerate the compiled model.

### 2e. Add an exercise for today only

- In the live workout, an "Add exercise" action opens the exercise picker and adds the exercise to **today's workout
  only**; the split and `UserDayExercise` rows are never changed. It sits next to "Swap" (2d) in the same menu, and uses
  the same session record on `LiveWorkout` (a new nullable column, for example `ExtraExerciseIds`, like
  `SkippedExerciseIds`, so it survives the app being killed).
- Sets logged on it are normal sets: they appear in history, PRs, volume and points like any other.
- **Counting (decided):** an added exercise **changes the percent**. It is added to the plan's totals, so the ring, the
  Live Activity percent and the points' share of exercises done (`FilledExercises` over `TotalExercises`, the 50% and
  70% thresholds) all include it. Consequence to design for: adding an exercise and not finishing it lowers the share,
  so it can lower the points; show this in the Add exercise sheet in one line.
- **Migration:** app SQLite (`LiveWorkout`), plus the compiled model.

### 2f. Warm-up sets count in the progress

- Warm-up sets count toward the progress of every exercise, in the live workout (the ring, the "N of M" text and the
  chips) and in the Live Activity and the live notification (the ring, percent and set count). Today they are left out.
- Planned warm-ups come from `UserDayExercise.WarmupSets`; a warm-up that was not planned but was logged counts too.
- **Points are not affected (checked in the code):** the ring and the Live Activity show the share of planned **sets**
  done (`LiveWorkoutRules.SetProgress`, working sets only today). Points use a different number: the share of
  **exercises** with their sets filled (`PointsCalculator`, `FilledExercises` over `TotalExercises`: half points from
  50%, full from 70%). Counting warm-ups in the ring changes only what the ring and the island show.
- iOS (`GymApp.iOS.LiveActivity`, Swift) and Android (the ongoing workout notification) both change; the app side is
  `LiveWorkoutWatcher` and `LiveWorkoutRules`.

### 2g. Dynamic Island: the ring is cut off (iOS bug fix)

- On the full (expanded) Dynamic Island the progress circle is slightly cut off. Fix the layout in the widget
  extension (`GymApp.iOS.LiveActivity`): give the ring room inside the island's expanded region, and check the compact
  and minimal presentations and the Lock Screen at the same time. Check it on a real iPhone with and without a Dynamic
  Island, and with the largest text size.
- Android: check the ongoing workout notification's progress for the same cut-off; there is likely no equivalent.

### 2h. Starting a workout opens at the top

- When a workout starts, the live card's page shows its top (scroll position reset to the top), not wherever the
  previous screen was left. Applies to both phones; shared view code in `GymApp.App`.

### 2i. No streak line during a live workout

- The Home streak card shows a line about logging a workout to keep the streak ("Log today's workout to keep your
  streak.", "Do *workout name* today to keep your streak.", "Work out today to keep your streak.", and the other
  `StreakNote` texts in `HomeViewModel`, shown in `HomeView.axaml`). It appears in the card with the progress ring while
  a live workout is open, where it makes no sense.
- While a live workout is open (and during a live cardio session, 2k), **hide that line** (`StreakNote`); it comes back
  when the session ends. The streak count itself stays. Both phones, shared view code.
- The text under the set chips ("Tap a logged set to change it.") is **not** part of this and stays.

### 2j. Ring animation after logging a set

- After logging a set, or finishing an exercise, the page scrolls up a little so the progress ring (the semicircle in
  the live card's header) is fully visible, and then the ring plays its fill animation to the new percent. If the ring
  is already visible, only the fill plays.
- Respect the phone's "reduce motion" setting (no scroll animation, the ring just updates). Keep it short and smooth:
  see the scrolling check in the ground rules. Both phones; shared view code, with the platform's reduce-motion flag
  read through `IPlatform`.

### 2k. Cardio as its own live session

- **No more automatic cardio.** Finishing a workout no longer starts cardio by itself. A **Start cardio** button is
  offered instead: on the finished-workout screen, and also in the card with the progress ring, so cardio can be
  started **on its own, without a strength workout**.
- Starting cardio turns the live screen into a **live cardio session** with only the timer (no exercises, no ring).
- **Live Activity and notification for cardio:** a new Dynamic Island (compact, expanded and minimal) and Lock Screen
  layout showing the timer and a cardio label, and the Android ongoing notification for cardio. New design: draft
  options for the developer to pick from before building. Pass the session kind (workout or cardio) to the activity;
  keep the existing workout layout unchanged.
- Gym verification, pausing when the user leaves the gym, resuming when they return, and saving stay as they are in
  Phase 18.2.
- **Counting (decided):** a cardio-only session is **not** a workout, so on a training day it adds no streak day and no
  workout points. On a **rest day** it counts: it **increases the streak** (adds a streak day) and **gives cardio
  points** for it. Work out how this fits the rest-day rule (0.18.1: a rest day already keeps the streak alive without
  adding), the "Double Up" badge, the points calculator (`PointsCalculator`) and the streak rules (`StreakCalculator`),
  and test: cardio on a rest day adds one streak day and cardio points; cardio on a training day with no workout adds
  neither; cardio after a workout behaves as today.
- Cardio after a strength workout (the "Double Up" badge, Health export of both sessions) must still work.

### 2l. Live workout: both-phones and checks

- Every item from 2e to 2k is checked on both phones (CLAUDE.md "Both phones, every change"); the iOS-only ones are
  2g and the Swift side of 2f and 2k, with an Android notification counterpart named in each.
- Scrolling check on the live workout card after 2h and 2j, on a Release build.

## 3. PRs count per session (rule change)

Today `PrCalculator.Compute` replays every working set in order, so two sets in one session can each be a PR. The new
rule:

- **A PR is the session's best set against the best of all earlier sessions.** Order the sets of an exercise by
  session (date). Within a session only the **best set** counts (the existing `Beats` rule: heavier weight wins; at the
  same weight, more reps wins). It is a PR only if it beats the best set of every **previous session**.
- **At most one PR per exercise per session.** If two sets in a session both beat the last session, it is still one PR
  (the session's best set). A heavier or longer set later in the same session raises that session's PR value without
  adding a second PR.
- A first set that is lower than a later set in the same session never makes the later set an extra PR.
- Warm-ups (and drops, from 2a) still never count. Manual PR adjustments (`PrAdjustment`) are applied before the day's
  sets, as now, and a session on the adjustment's date is compared against it.
- The first session of an exercise is the baseline (one baseline entry, its best set).
- **Live workout (decided):** a PR is shown **only when the exercise is finished** (or when the workout is
  finished), using the session's best set for that exercise, so it appears once per exercise per session. It does not
  pop up on a set while the exercise is still being done.
- **What changes in the numbers:** PR history is computed from the sets, so existing history recalculates by itself
  and past same-session "extra" PRs disappear. Check everything that counts PRs: the PR list, points for PRs (if any),
  the badges PR Breaker and PR Week, the weekly recap and the friend stats. The Phase 25 re-earn-from-history run
  (section 4) recalculates badges once, so users don't lose or gain them silently.
- Tests: two improving sets in one session = one PR; a higher set later in the same session keeps one PR; a set that
  only beats an earlier set of the same session is no PR; ties; manual adjustment; first session baseline.

## 4. Leg and upper-body badges (Phase 25 update)

A person who leg-presses 200 kg would fill most strength badges instantly, so leg lifts get their own badges with much
higher weight thresholds, and the existing strength badges count the upper body.

- **Muscle groups:** legs = Quads, Hamstrings, Glutes, Calves; upper body = Chest, Back, Shoulders, Biceps, Triceps,
  Forearms; Core goes with the upper body. Open: confirm this split and where Core belongs.
- **Separate badges** with their own name, their own leg-themed artwork and their own thresholds, for the badges that
  depend on weight: Heavy Lifter (lifetime volume), Bodyweight Club (a multiple of body weight in one set) and Triple
  Digits (100 kg in one set). Heavy Lifter, Bodyweight Club and Triple Digits are the ones to split (confirmed). Still to
  choose: the leg names and thresholds (draft a proposal for the developer to pick from, with the thresholds much
  higher than the upper-body ones). Count-based badges (Century,
  Rep Machine, Exercise Explorer) stay as they are.
- Existing badges keep their ids (never renamed, they are sent on the wire); the leg versions are new ids. The
  upper-body versions count only upper-body exercises, so their progress changes.
- **0.28 launch: re-earn from history.** On the first start of 0.28 the app clears earned badges and recalculates them
  from the logged sets with the new rules, with the one "You earned N badges from your history" sheet and no unlock
  sheets. No other user data changes. It runs once (a stored flag), per phone.
- **Weigh-in Habit counts Health weigh-ins, but only from when the user started using the app.** Today (0.27.1)
  Weigh-in Habit skips every weigh-in imported from Apple Health (`WeighIn.FromHealth`), so a smart-scale user gets no
  credit. Change it: weigh-ins imported from Health **count**, but only those dated **on or after the day the user
  started using the app**. Weigh-ins from before that day never count, so a user who shares their whole Health history
  (or whose last weeks of weigh-ins are imported on first connect) doesn't get the badge from past data; it starts
  from that point on.
  - **Cutoff (decided):** the local date the user first started using the app on this account
    (`User.CreatedAt`, as a local date), not the day Apple Health was connected. A
    restore from backup (section 1) must keep the original cutoff, so a new phone doesn't reset it or let the old
    history count.
  - Weigh-ins typed in the app always count, as today. A weigh-in typed on a day that already has an imported one
    replaces it (existing behaviour), and then counts as typed.
  - **Bodyweight Club** keeps using the latest weight (typed or from Health) as the body weight for its multiple;
    that is a value, not a count, so the cutoff doesn't change it (decided).
  - Applies to both phones: `FromHealth` covers Apple Health (iOS) and Health Connect (Android, Phase 23); the rule
    lives in shared code (`BadgeInputs`, `BadgeEvaluator`, `BadgeCatalog`), so one change covers both.
  - The 0.28 re-earn-from-history run uses this rule, so an existing user's Weigh-in Habit is recalculated once.
  - Tests: a Health weigh-in before the cutoff doesn't count, one on the cutoff day does, an imported flood of past
    weeks adds nothing, a Health weigh-in after the cutoff counts, a typed weigh-in always counts, and the weeks-in-a-row
    logic still breaks on a missing week.
- Friends see badges through the server: new badge ids are additive (old clients ignore unknown ones), but confirm
  with the `server-compat-reviewer` agent, and plan what a friend on an older build sees.
- Both phones; shared code and the badge artwork (new leg glyphs in the badge set).

## 5. Beginner help (free, no premium plumbing)

Free: the beginner basics are what bring people in, so none of this is paywalled. Deeper analysis is premium (see
`FUTURE_PREMIUM.md`). Phones only. Read the existing live workout card, `SplitService`, and the built-in exercise list
before designing; every item below needs its design drafted as options first.

- **"Build a workout" button.** Pick a goal or muscle groups and how much time there is, and the app builds a workout
  from the built-in exercises (a few per muscle group, compound lifts first, a sensible number of sets). The result is
  a one-off workout or a new split day the user can edit, like the existing hand-picked workout for a rest day.
  Rules-based, offline, no schema change expected. Open design: the inputs (muscle groups, time, equipment), and how
  it avoids repeating the same workout every time.
- **Tips and form tips (text).** Short written cues per exercise (setup, the common mistake, breathing) on the
  exercise in the live workout and the exercise detail, plus general tips on a Tips screen. Built-in exercises only;
  stored as app content, not on the server. The animated GIFs are separate (premium, see `FUTURE_PREMIUM.md`).
- **Weight increase hint from a rep range (basic).** The user picks a rep range for an exercise (for example 8 to 12);
  when every working set reaches the top of it, the live workout suggests adding weight next time (a fixed step such
  as 2.5 kg). A hint only, never applied automatically; no history beyond the last session. The deeper versions
  (plateau detection, trends) stay premium. Pairs with "last time on the set" (section 2). Warmups are ignored and
  drops never count.
- **Rest guidance as a tip, not a timer.** The rest timer is ruled out (see above). A static line on the exercise
  ("heavy lifts: 2 to 3 minutes, lighter work: about a minute") is just a tip and is allowed; it has no countdown, no
  notification and no Live Activity change.

## 6. Weekly and monthly recap (no schema change expected)

The deeper insights (overload suggestions, plateau detection, per-muscle volume, trends, year in review) are premium,
see `FUTURE_PREMIUM.md`. The free insight is the recap.

- **Weekly and monthly recap.** A summary of volume, PRs and streak for the week or month, shareable as an image
  through the share sheet. Pure rules in `GymApp.Core` over `SetLog` plus an image drawn by a control, like
  `QrCodeView`; no server involvement. Warmups are ignored; drops never count toward PRs. Pairs with the PR card
  below, so build the image drawing once.

## 7. Training-day reminder body (small, no schema change)

The reminder that tells the user to go to the gym (the "Training day" reminder, `ReminderPlanner.Training` in
`GymApp.Services`, scheduled by both phone heads) has a new body.

- **Today:** title "Today is *workout name*" (or "Training day" when there is no name), body "Check in when you've
  trained to keep your *N*-day streak." (or "...to start your streak.").
- **Title: unchanged.** It stays "Today is *workout name*" (and "Training day" when there is no name).
- **New body:** "You still have time to train today, go to the gym and start your workout!"
- **The streak line is dropped (decided).** The body is just the new sentence; the reminder no longer says how long
  the streak is, and the `StreakDays` text is no longer used by this reminder.
- The wording lives in the shared planner, so iOS and Android change together; nothing in a head needs editing.
  `ReminderPlanner` tests that check the body text must be updated.
- The "Missed day" reminder ("Your streak is at stake") and "Welcome back" are unchanged.

## 8. Social (needs the server)

These change `Contracts` and the server. Plan each against `ClientContractTests` and `ClientVersionTests`, add optional
fields only, and run the `server-compat-reviewer` agent. Server migrations are a separate command and the production
server must run `migrate` before deploy.

- **Share a PR as a card.** Render a PR as an image (exercise, weight, reps, date) and share it through the phone's
  share sheet. Starts as an app-only image with no server change; a "send to a friend" version would reuse the shares
  feature.
- **Reactions or nudges.** A friend can react to a check-in or PR. Needs a server table, an endpoint, and a push
  notification (Phase 22 exists). Open design: which reactions (a small fixed set), limits per day, and blocking
  (`UserBlock`) must hide them.
- **Weekly friend challenges.** For example most workouts or most volume among friends, using the existing leaderboard
  rules in `GymApp.Social`. Needs both a `Remote*` and a `Local*` implementation (CLAUDE.md "Two wirings"). Open
  design: how a challenge starts (one friend invites, or an automatic weekly one), how volume is uploaded (stats are
  uploaded already; volume is not), and what happens when someone joins late.

## Before the 1.0 App Store launch

- **Wipe all badges to zero** when 1.0 launches on the App Store (a one-time reset on the first start of 1.0, with
  no history re-earn), as the developer decided. This is separate from the 0.28 re-earn-from-history run. Plan it as
  its own item for the 1.0 release.

## Order and versions

1. Gym data cleanup: the Timișoara pilot, then the override layer, then the other cities.
2. Backup and restore, CSV export.
3. Live workout: drop sets, per hand, last time on the set, quick swap, then the requested changes (add exercise,
   warm-ups in the progress, island fix, top of page, streak line hidden, ring animation, cardio as its own session).
4. PRs count per session.
5. Leg and upper-body badges (and the 0.28 badge re-earn).
6. Beginner help: build a workout, tips and form tips, the weight increase hint.
7. Weekly and monthly recap (and the image-drawing control it shares with the PR card).
8. Training-day reminder body (a few lines in `ReminderPlanner`).
9. PR card, then reactions and weekly challenges (server work, last).

The numbering is the developer's call: per CLAUDE.md, a feature that needs a schema change opens a new phase
(`0.N.0`), so expect `0.28.0` for backup and restore and later items to take their own numbers.
