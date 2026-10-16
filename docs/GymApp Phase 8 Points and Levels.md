# GymApp Phase 8: Points and Levels

Oct 14, 2026 · @Cristi

## Instructions for Claude Code

Build Phase 8 in five steps (8a to 8e), in order, and stop after each step so I can review it.

- Read the existing code before writing anything: StreakCalculator, SplitSchedule, WeekSchedule, TodayPlan, UserSchedule, WorkoutLog, CalorieLog, User, AppDbContext, the existing services and MainViewModel. Match their naming, folder layout and style.
- Pure logic goes in GymApp.Core, entities and migrations in GymApp.Data, services in GymApp.Services, views and view models in GymApp.Desktop, tests in GymApp.Tests.
- Do not run git commit, git push or any other git command that changes history or the remote.
- After each step run dotnet build and dotnet test. Report every file created or modified, with its path and a one-line reason.
- If something in this doc conflicts with the existing code, stop and ask instead of guessing.

## Goal and rules

Points are derived from the user's logs and stored in a PointsEntry ledger; User.Points is a cached sum and the level is computed from it. There are no points for PRs or sets of any kind.

| Reason | Amount | Earned when | De-dup key |
| --- | --- | --- | --- |
| Workout | +5 | A WorkoutLog exists for that date (the "I went today" log only) | workout:{date} |
| WeekComplete | +50 | Every training day of the week was logged on its scheduled date. Can be awarded as soon as the last training day is logged | week:{monday} |
| WeekCompleteGrace | +25 | Every training day was covered, but at least one only through the grace rule. Awarded only after the week has ended | week:{monday} |
| CalorieTarget | +10 | That day's CalorieLog hits the target for its goal type | calories:{date} |

- WeekComplete and WeekCompleteGrace share one key, so a week earns either 50 or 25, never both.
- Any training day missed and not paid by the grace rule means 0 for that week.
- Weeks are Monday to Sunday. A week only qualifies if the user had a schedule for all seven days and the week contains at least one training day; this blocks an easy bonus in a partial first week.
- Calorie target per goal type: lose = at or under the goal, gain = at or over, maintain = within plus or minus 10 percent. The log also has to be at least 50 percent of the goal, so logging 0 kcal never counts. Keep the 10 and 50 percent as named constants.
- Levels can go down. When entries are removed the total drops and the level is recomputed; no highest-level value is stored.

## Prerequisites

Two things from earlier phases have to exist first; check them before 8a and tell me if either is missing.

1. **Per-day outcomes from the streak replay.** The week bonus must not re-implement the grace rule. Extend StreakCalculator (or add a method next to it) so it returns, for each scheduled training day, one of: Done (logged on its date), Graced (missed but paid by the shift rule), Missed (not paid, streak broken). Points reads this timeline.
   - The grace rule (shift option) is still deferred. If it is not implemented yet, the timeline only produces Done and Missed, and the 25-point path stays inactive until the rule lands. Its tests use hand-built timelines.
   - Shift rule reminder: if the day after a miss is also a training day, that workout pays the missed day and the schedule slides back a day until a rest day absorbs it.
2. **Goal type snapshot on CalorieLog.** CalorieLog already stores GoalAtTime. It also needs GoalTypeAtTime, so a later switch from lose to gain does not change old results. If Phase 7 did not add it, add it in 8b and backfill existing rows from the user's current GoalType.

## 8a. Core: PointsCalculator and LevelCalculator

Both are pure, static and database-free, like StreakCalculator and PrCalculator.

**PointsCalculator.** Inputs: workout log dates, the per-day outcome timeline, schedule coverage per day (had a schedule or not), calorie logs (date, kcal, goal, goal type), today, and the window start date. Output: the list of entries that should exist inside the window, each with Reason, Amount, Date and DedupKey.

1. One Workout entry per workout log date in the window.
2. For each Monday-to-Sunday week that starts on or after the window start:
   - Skip it unless all seven days had a schedule and it has at least one training day.
   - All training days Done → WeekComplete (+50), dated the day the last training day was logged.
   - All training days Done or Graced, at least one Graced, and the week has ended (today is after its Sunday) → WeekCompleteGrace (+25), dated that Sunday.
   - A Graced day whose payment lands in the next week still counts for its own week; that week is settled once the paying workout is logged.
   - Any Missed day → nothing.
   - The week containing today can only earn the +50 (it has not ended).
3. One CalorieTarget entry per calorie log in the window that hits its target (rules above).

**LevelCalculator.** Input: total points. Output: level number, name, points at the start of this level, points needed for the next level (null at the top), and progress from 0 to 1 for the bar.

| Level | Name | From (points) |
| --- | --- | --- |
| 1 | Beginner | 0 |
| 2 | Novice | 100 |
| 3 | Regular | 300 |
| 4 | Dedicated | 700 |
| 5 | Athlete | 1,500 |
| 6 | Elite | 3,000 |
| 7 | Legend | 6,000 |

A full PPL week earns about 150 points (30 workouts, 50 week, up to 70 calories), so Regular takes two to three weeks. Keep the table in one place so it can be tuned.

## 8b. Data: entity and migration

One migration (for example PointsAndLevels) covers all of these changes.

| Change | Fields | Notes |
| --- | --- | --- |
| New PointsEntry | Id, UserId, Reason (stored as text), Amount, Date, DedupKey | Unique index on (UserId, DedupKey). Index on (UserId, Date). Cascade delete with User |
| User | PointsBackfilled (bool, default false) | Marks that the one-time full rebuild has run |
| User.Points | already exists | Stays a cached sum of the user's entries; never edited by hand |
| CalorieLog | GoalTypeAtTime | Only if Phase 7 did not add it (see Prerequisites) |

The unique index is the safety net: even a bug in the sync cannot store two Workout entries for one day or two bonuses for one week.

## 8c. Services: PointsService

PointsService keeps the ledger in step with the calculator, in a sync window that covers the current week and the previous one; everything older is frozen.

**Freezing (decided).** The window starts on the Monday of the previous week. Entries dated before it are never touched again, so a later rule change or a schedule edit cannot rewrite old history. The previous week stays open because a grace payment on Monday can still settle its 25-point bonus.

**SyncAsync(userId).**

1. Load the data the calculator needs from the window start minus a few days (enough for the streak replay to know each day's outcome).
2. Run PointsCalculator for the window.
3. Compare with the stored entries in the window by DedupKey: insert missing ones, delete ones that no longer apply, update the amount if it changed (a +50 week can never turn into +25, but keep the update for safety).
4. Set User.Points to the sum of all the user's entries, frozen ones included.
5. Do steps 3 and 4 in one transaction.
6. Return the old total, new total, old level, new level and the list of added entries, for the UI notice.

**RebuildAsync(userId).** The same steps with the window starting at the user's first log. Run it once at startup for any user with PointsBackfilled = false, then set the flag. This gives existing users points for the workouts and calorie logs they already have.

**Hooks.** Call SyncAsync after each of these succeeds:

- "I went today" is logged (workout and week)
- A calorie log is saved or edited
- The schedule changes: a split, variant or start day is selected, or a custom split is edited (the current week's bonus)
- App start, after the session is restored (catches a week that ended while the app was closed)

Saving or deleting sets does not call it.

## 8d. Desktop UI

The UI only reads results from PointsService; no point rules live in view models.

- **Home level card:** level name and number, total points, a progress bar, and "X points to Novice" (or "Max level" at Legend).
- **Notice after a sync that added points:** a short message such as "+5 Workout" or "+50 Week complete", plus "Level up: Regular" when the level rose. When a level drops, update the card quietly with no message.
- **Points history screen,** opened from the level card: the ledger newest first, with date, reason and amount, plus the total at the top. Use readable reason names ("Week complete (grace)" for the 25-point bonus).
- Add the new screen to the MainViewModel navigation the same way the other screens are added.

## 8e. Tests

Calculator tests are pure (no database); service tests use the existing in-memory SQLite setup with the real migrations. Use dates from October 2026 as in TodayPlanTests (2026-10-05 is a Monday).

**PointsCalculatorTests**

- One Workout entry per log date; no entry on days without a log
- Full week all Done → +50, dated the day of the last training log, even before Sunday
- Week with one Graced day → +25, but only once the week has ended; nothing while it is still running
- Grace payment in the next week still gives the earlier week its +25
- Any Missed day → no week entry
- Partial week (schedule began mid-week) and a week with no training days → no week entry
- A week never gets both 50 and 25
- Schedule change mid-week uses the right snapshot for each day
- Calories: hit and miss for lose, gain and maintain (including the 10 percent edges), the 50 percent minimum, and GoalTypeAtTime used instead of the current goal type
- Entries before the window start are not produced

**LevelCalculatorTests**

- Exact boundaries (99, 100, 299, 300 and 6,000 points)
- Progress maths and the null next level at Legend
- A lower total gives a lower level

**PointsServiceTests**

- Running SyncAsync twice changes nothing
- A source change inside the window removes the matching entry and lowers User.Points
- Entries before the window are never deleted or changed
- RebuildAsync on old data, then PointsBackfilled is set and it does not run again
- Logging sets never creates an entry
- User.Points always equals the sum of the entries

## Done when

- [ ] Day outcome timeline (Done / Graced / Missed) available from the streak logic
- [ ] PointsCalculator and LevelCalculator in GymApp.Core
- [ ] PointsEntry migration applied; CalorieLog has GoalTypeAtTime
- [ ] PointsService sync, rebuild and all hooks wired; nothing for sets or PRs
- [ ] Home level card, points notice and points history screen
- [ ] dotnet build clean and all tests pass
- [ ] List of created and modified files reported, nothing committed or pushed
