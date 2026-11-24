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

## 1. Data safety (build first)

Workouts live only on the device, so a lost phone loses all history. Build this before everything else, since it protects the most.

- **Backup and restore.** Back up the app's SQLite data (and the session settings that matter) to a file the user
  chooses, or to iCloud on iOS. Restore replaces local data after a clear confirmation. Open design points: what the
  backup contains (everything local: workouts, sets, PRs, body data, calories; not the account token), a format version
  so a newer app can read an older backup, and what happens when a backup is from a newer schema than the app.
  Restore must go through the same migrations as a fresh start and be tested against a database with every migration
  applied.
- **Export to CSV.** One file per kind of data (sets, body weight, calories), readable in a spreadsheet, shared through
  the phone's share sheet. Read-only, no schema change.

## 2. Live workout updates

Order: drop sets (1a), per hand (1b), last time on the set (1c), quick swap (1d). Each is reviewed before the next
starts. Read before changing anything: `LiveWorkoutCardView.axaml` and its view model, `LiveWorkout`,
`LiveWorkoutWatcher`, `SetLog`, `PrCalculator` (`SetKind`, `SetEntry`), `Exercise`, `UserDayExercise`,
`ExerciseBarSetting`, `Contracts/Shares.cs` (`SharedExercise`) and `SplitShareService`.

### 1a. Drop sets

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

### 1b. Per hand (unilateral exercises)

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

### 1c. Last time on the set

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

### 1d. Quick swap

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

## 3. Beginner help (free, no premium plumbing)

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

## 4. Weekly and monthly recap (no schema change expected)

The deeper insights (overload suggestions, plateau detection, per-muscle volume, trends, year in review) are premium,
see `FUTURE_PREMIUM.md`. The free insight is the recap.

- **Weekly and monthly recap.** A summary of volume, PRs and streak for the week or month, shareable as an image
  through the share sheet. Pure rules in `GymApp.Core` over `SetLog` plus an image drawn by a control, like
  `QrCodeView`; no server involvement. Warmups are ignored; drops never count toward PRs. Pairs with the PR card
  below, so build the image drawing once.

## 5. Training-day reminder body (small, no schema change)

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

## 6. Social (needs the server)

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

## Order and versions

1. Backup and restore, CSV export.
2. Live workout updates: drop sets, per hand, last time on the set, quick swap.
3. Beginner help: build a workout, tips and form tips, the weight increase hint.
4. Weekly and monthly recap (and the image-drawing control it shares with the PR card).
5. Training-day reminder wording (a few lines in `ReminderPlanner`).
6. PR card, then reactions and weekly challenges (server work, last).

The numbering is the developer's call: per CLAUDE.md, a feature that needs a schema change opens a new phase
(`0.N.0`), so expect `0.28.0` for backup and restore and later items to take their own numbers.
