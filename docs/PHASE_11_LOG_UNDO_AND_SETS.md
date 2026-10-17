# Phase 11: Undo check-in and log sets on check-in — spec for Claude Code

This is the spec for GymApp Phase 11. It does three things to the "I went today" button on Home:
1. lets you **undo** today's check-in
2. opens a **popup** on check-in to enter kg and reps for today's exercises
3. **saves the check-in only if at least 50% of the exercises are filled in**, so nobody gets streak days and points for just pressing a button

Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added (this phase shouldn't need one)
- Follow the existing project layout:
  - **GymApp.Core**: pure logic, no EF and no HTTP. Unit-tested.
  - **GymApp.Data**: entities, `AppDbContext`, migrations.
  - **GymApp.Services**: services, EF access, HTTP clients.
  - **GymApp.Desktop**: Avalonia views and view models only, using CommunityToolkit.Mvvm.
  - **GymApp.Tests**: xUnit. Service tests run against in-memory SQLite with the real migrations (`TestDb`).
- Read `WorkoutService`, `SetService`, `DayExerciseService`, `PointsService`, `StreakService`, `GymService`, `HomeViewModel`, `HomeView`, `ExerciseViewModel` and `MainViewModel` before changing anything. Names below are proposals, so match the existing naming style.
- Points and streak **rules** don't change. They are still derived from `WorkoutLog` rows; this phase only changes when a `WorkoutLog` may be created or removed.

## Decisions already made (don't change these)

1. **Undo is today only.** Like logging (no backdating), only today's check-in can be undone. After midnight yesterday's log is read-only.
2. **The popup shows the exercises of today's day.** Today's day comes from the same plan the check-in already uses (`WorkoutService.PlanAsync` → `SplitDay`), and its exercises from `UserDayExercise` for that user and day, in `Order`.
3. **Each exercise gets one row per planned set:** `WarmupSets` warm-up rows, then `WorkingSets` working rows, each with a kg box and a reps box. Example on a push day: Bench press (1 warm-up + 3 working), Chest press (0 + 3), Triceps extensions (0 + 3).
4. **No exercises for today's day → no check-in.** The popup instead says "You haven't built a workout for {day} yet." with a button that opens the Workouts section on that day (`MainViewModel.ShowDay`).
5. **The 50% rule is enforced in the service, not just the UI.** `WorkoutService.LogTodayAsync` checks the sets saved for today. The popup only mirrors the same rule so the button can be disabled with a hint.
6. **Sets are saved through `SetService.SaveDayAsync`**, the same path the exercise screen uses, so PRs, history and "pre-fill from last time" keep working and all existing validation (reps 1–100, 0–1000 kg) applies.

## Defaults to confirm (change here if wrong)

- **What "filled in" means for one exercise:** every **working** set row has both kg and reps. Warm-up rows are optional (saved if filled, ignored by the rule). 0 kg is allowed for bodyweight exercises. A half-filled row (kg without reps or the other way round) is an error, not "empty".
- **Threshold:** filled exercises ≥ half of the day's exercises, rounded **up**. 1 exercise → 1, 3 → 2, 4 → 2, 5 → 3.
- **Undo keeps the sets.** Undo removes only the `WorkoutLog` (and with it the gym choice for today). Sets stay in history and PRs, the same as removing an exercise from a day keeps its sets. Re-checking in pre-fills the popup with them.
- **Undo asks for confirmation:** "Undo today's check-in? You'll lose today's +5 points and your streak goes back."
- **Rest days and no split:** there is no planned day, so the popup lets you pick which of your split's days you trained (only days with exercises are listed). With no split selected at all, it shows the "build a workout" prompt and links to Splits/Workouts. The 50% rule applies to the day you picked, and `WorkoutLog.SplitDayId` stores it.
- **Sets already logged today** (from the exercise screen) pre-fill the popup, and count towards the rule.

---

## 11a. Core: check-in rule

**`CheckInRule`** in Core, pure and static like `Leaderboard`.

**Input:** one item per exercise on the day: exercise id, planned working sets, and the sets logged today for it (`SetEntry` list).

**Output:** `CheckInStatus(int Filled, int Required, int Total, bool CanLog)`.

- An exercise is filled when it has **at least `WorkingSets` working sets** logged today (each with reps ≥ 1, which `SetService` already guarantees).
- `Required = (Total + 1) / 2` (half, rounded up). `CanLog = Total > 0 && Filled >= Required`.
- `Total == 0` → `CanLog` is false (the "build a workout" case).
- Keep the 50% as a named constant (`MinFilledShare = 0.5`), like the calorie percentages in Phase 8.

**Tests (`CheckInRuleTests`):**
- 0 exercises → can't log
- 1 exercise: empty → no, filled → yes
- 3 exercises: 1 filled → no, 2 filled → yes
- 4 exercises: 2 filled → yes (exactly half)
- warm-up sets alone don't count; fewer working sets than planned don't count
- more working sets than planned still count

---

## 11b. Services: check-in with sets, and undo

**`WorkoutService`** gets these changes (all today only, `today` parameter for tests like `SetService`):

| Method | Behaviour |
| --- | --- |
| `GetCheckInAsync(userId)` | What the popup needs: today's day (id, name) or null on a rest day / no split, the day's exercises in order (id, name, warm-up sets, working sets), the sets already logged today per exercise, and the `CheckInStatus`. On a rest day it also returns the split's days that have exercises, for the picker. |
| `LogTodayAsync(userId, splitDayId?)` | As now, but **first runs `CheckInRule`** against the sets saved today for that day's exercises. Fails with "Fill in at least {Required} of {Total} exercises to log this workout." or "Build a workout for {day} first." `splitDayId` is only used on rest days / no split; on a training day the planned day always wins. Already logged today → returns the current streak, as now. |
| `UndoTodayAsync(userId)` | Deletes today's `WorkoutLog` (none → returns success, harmless double tap). Then runs `StreakService.RecalculateAsync` and `PointsService.SyncAsync`, and returns a `LogResult` so Home can update the streak and level cards. |

- The popup saves sets with `SetService.SaveDayAsync` per exercise, then calls `LogTodayAsync`. If the check-in fails, the sets stay saved (they're real training data) and the popup stays open with the error.
- **Points after undo:** `PointsService.SyncAsync` already deletes ledger entries inside its window that the rules no longer want, so the +5 Workout entry and, if it was the last training day, the +50 WeekComplete both disappear. The level can go down (allowed since Phase 8). No points code changes.
- **Gym after undo:** the gym choice lives on `WorkoutLog.GymId`, so it goes with the log. Check that `GymService.GetTodayAsync` and the gym summary handle it without changes.

**Tests (`WorkoutServiceTests`, new file):**
- log fails with no sets, and with fewer than half of the exercises filled
- log succeeds at exactly half (4 exercises, 2 filled) and stores the right `SplitDayId`
- log fails with "build a workout" when the day has no exercises
- rest day: log with a chosen day that's filled succeeds; a day that isn't the user's fails
- warm-up-only sets don't count
- undo removes the log, the +5 entry and a +50 week bonus, and the streak goes back
- undo keeps today's sets
- undo when nothing is logged is harmless; undo never touches yesterday's log
- log → undo → log again works and ends with one log and one +5

---

## 11c. Desktop UI

**Home, today card:**
- Not logged: **"I went today"** opens the check-in popup (instead of logging straight away).
- Logged: the button reads **"Logged ✓"** and next to it (or under it) an **"Undo"** link. Undo asks for confirmation, then calls `UndoTodayAsync`, and refreshes the today, streak, level, gym link and friends cards. The gym prompt hides.

**Check-in popup** (an overlay on Home, like the gym prompt, or a modal; match whatever the app already uses for dialogs):
- Title: today's day name, e.g. "Push day".
- One section per exercise: the name, then a row per planned set labelled "Warm-up 1", "Set 1", "Set 2"…, each with a **kg** and a **reps** box. Pre-filled from today's sets if any, with a "Same as last time" button per exercise that uses `SetService.GetLastSessionAsync`.
- A live counter: "2 of 3 exercises filled · need 2". The **Log workout** button is disabled until the rule passes. Number parsing uses `Units`/`NumberParsing` like the exercise screen (comma or dot decimals).
- **Log workout**: saves each exercise with `SaveDayAsync`, then `LogTodayAsync`. On success the popup closes and the existing gym prompt shows, as now.
- **Cancel** closes without logging. Sets typed but not saved are dropped.
- **No exercises for today:** the popup shows "You haven't built a workout for {day} yet." and a **Go to Workouts** button that opens that day (`MainViewModel.ShowDay`). No log button.
- **Rest day / no planned day:** a day picker at the top; the list below follows the picked day.

All rule logic lives in `CheckInRule` and `WorkoutService`; the view model only shows `CheckInStatus`.

**Done when:**
- a push day with 3 exercises can't be logged with 1 filled, and can with 2
- a day with no exercises shows the Workouts prompt and opens that day
- undo brings back "I went today", removes today's points and resets the streak card
- logging again after undo works

---

## Done when

- [ ] `CheckInRule` in GymApp.Core, with tests
- [ ] `WorkoutService` check-in with the 50% rule, `UndoTodayAsync`, with tests
- [ ] Check-in popup with kg/reps per warm-up and working set, the counter and the Workouts prompt
- [ ] Undo on Home with confirmation, refreshing streak, points and gym
- [ ] `dotnet build` clean and all tests pass
- [ ] List of created and modified files reported, nothing committed or pushed

---

## Known limitations

- **The rule can still be gamed** by typing made-up numbers. It only stops one-tap check-ins; there's no way to verify real weights.
- **Undo then re-log at a different gym** works, but the first gym choice isn't kept anywhere, which is fine because only today's log is editable.
