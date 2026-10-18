# Phase 12: Polish and v1.0.0 — spec for Claude Code

This is the spec for GymApp Phase 12, the last phase before **v1.0.0**. It does five things:
1. fixes the **streak bug** where an inactive user's streak flips between 1 and 0 (Phase 10 known issue)
2. replaces the silent grace rule with a **missed-day choice**: the day after a missed training day, the user picks how to handle it
3. turns Home into a **dashboard**
4. a **validation and error handling** pass, and **theming**
5. **release prep** for v1.0.0

Work through the sub-phases **in order** (12a to 12f) and stop after each one for review.

## Ground rules

- **Do not run `git commit`, `git push` or `git tag`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added, if any (12a adds one; 12e might add one)
  - which existing tests you changed, and why
- Follow the existing project layout:
  - **GymApp.Core**: pure logic, no EF and no HTTP. Unit-tested.
  - **GymApp.Data**: entities, `AppDbContext`, migrations.
  - **GymApp.Services**: services, EF access, HTTP clients.
  - **GymApp.Desktop**: Avalonia views and view models only, using CommunityToolkit.Mvvm.
  - **GymApp.Tests**: xUnit. Service tests run against in-memory SQLite with the real migrations (`TestDb`).
- Names below are proposals, so match the existing naming style.
- **No new features beyond this spec.** The "optional extras" in the implementation plan (rest timer, charts, plate calculator...) are for 1.1 and later. The sync server (10f) is not part of 1.0.

---

## 12a: Streak rules (bug fix and missed-day choices)

Read `StreakCalculator`, `StreakCalculatorTests`, `StreakService`, `PointsService` (it uses `StreakCalculator.Timeline`), `PointsCalculator`, `SocialStore` (it recalculates friends' streaks) and `docs/streak-grace-rule.md` first.

### Why the bug happens

When a training day is missed, the calculator counts it provisionally (+1) and opens a debt. That's fine while there's a streak to save. But it also happens when the streak is **already 0**, so a user who stopped training gets +1 for a missed day, then breaks back to 0 the next day, and so on. Rest days after a break also count +1, so the same user shows 🔥 1 on every rest day.

The missed-day choice alone doesn't fix this, because an inactive user never opens the app to choose. The fix is the rule **"no streak, nothing to save"**, which the choice model needs anyway (there's nothing to offer when the streak is 0).

### Decisions already made (don't change these)

1. **The day after a missed training day, the user chooses one of three options** (only when a streak is at stake, see the rules below):
   - **Shift my whole schedule by a day.** Yesterday's workout is done today, and every day after it (rest days too) moves one day later, permanently. The streak is kept if today's workout is logged.
   - **Make it up before my next rest day.** Yesterday's workout is done today, today's tomorrow, and so on, until the next rest day absorbs the slip. After that the normal schedule resumes. This is the existing grace rule (shift model). The streak is kept if every day of the chain is logged.
   - **Skip it and continue.** Today's workout stays as planned. The streak resets to 0 now, and today's workout starts a new one.

   Example on PPL (Mon Push, Tue Pull, Wed Legs, Thu Rest, Fri Push, Sat Pull, Sun Legs) with Legs missed on Wednesday:

   | Option | Thu | Fri | Sat | Sun | Mon | Streak |
   |---|---|---|---|---|---|---|
   | Shift my whole schedule | Legs | Rest | Push | Pull | Legs | kept |
   | Make it up before my next rest day | Legs | Push | Pull | Legs | Push | kept (Thursday's rest day disappears) |
   | Skip it and continue | Rest | Push | Pull | Legs | Push | reset to 0 |

2. **The choice is stored** in a new table, one row per missed day. Grace is no longer purely computed.
3. **Misses without a stored choice behave like "Make it up before my next rest day".** All history from before this phase has no choice rows, so existing streaks and points stay exactly the same.
4. **Logging today is blocked until the choice is made** when one is needed. Enforce it in `WorkoutService.LogTodayAsync`, not just the UI.
5. **No streak, no debt.** A training day missed while the count is 0 changes nothing: no provisional +1, no debt, no choice.
6. Points rules don't change. A forgiven day is still `Graced` in the timeline, so the week earns the +25 grace bonus instead of +50.

### Defaults to confirm (change here if wrong)

- **Weekly limits, counted separately for each option.** "Shift my whole schedule" can be used **once** per Monday-to-Sunday week, and "Make it up before my next rest day" **twice**. Both count by the week of the missed day, and using one doesn't use up the other. An option that's used up is hidden and the card says why ("You've already made up 2 workouts this week"). "Skip it and continue" is always available. Keep the limits as named constants (`MaxWeekShiftsPerWeek = 1`, `MaxMakeUpsPerWeek = 2`).
- **The choice is only offered on the day right after the miss.** If the user doesn't open the app that day, the streak breaks at midnight as it does now. Home then shows "Your streak ended on {date}" once, with no card.
- **The choice is final.** It can't be changed later that day, and undoing today's check-in doesn't undo it. Every option asks for confirmation.
- **After a break, rest days don't count until the next logged workout.** This is the second half of the bug fix.
- **"Make it up before my next rest day" is hidden** when the current week pattern has no rest day.
- **"Shift my whole schedule" is hidden** when the user has no split selected.

### Data

New entity **`MissedDayChoice`**:

| Field | Type | Notes |
|---|---|---|
| Id | int | |
| UserId | int | FK to User, cascade delete |
| Date | DateOnly | the **missed** training day, not the day the choice was made |
| Choice | `MissChoice` (int) | `ShiftWeek`, `ShiftToRest`, `Continue` |
| CreatedAt | DateTime | |

Unique index on (UserId, Date). Migration name: `MissedDayChoices`. Put the `MissChoice` enum in GymApp.Core so the calculator can use it.

### Calculator changes

`StreakCalculator.Calculate` and `Timeline` take one more argument, `IReadOnlyDictionary<DateOnly, MissChoice> choices`, keyed by the missed date. Every caller passes the user's choices: `StreakService`, `PointsService`, and anything `SocialStore` calls for friends.

`StreakResult` gets one more field, **`OwedDay`** (`DateOnly?`): the training day the open debt is for, or null. 12b uses it to know which day to offer and what today's workout is.

New status **`NeedsChoice`**: yesterday's training day was missed, a streak is at stake, no choice is stored, and today isn't logged.

The walk stays the same, with these changes. A **fresh miss** is a training day missed while no debt was open (not a day owed inside a shift chain).

| # | Debt open? | Day | Logged? | Result |
|---|---|---|---|---|
| 1 | yes, owed day is a fresh miss with choice **Continue** | any | any | **Break before the day:** `count = 0`, `debt = no`, owed day `Missed`. Then handle `d` with rows 5 to 8 (the day is **not** consumed). |
| 2 | yes | any | yes | Paid: `count += 1`, owed day `Graced`. If the chain started with **ShiftWeek**: `debt = no`. Otherwise as now: `debt = d is training`, `owed = d`. |
| 3 | yes | any | no, today | Stop. **NeedsChoice** if the owed day is a fresh miss with no choice, otherwise **AtRisk**. |
| 4 | yes | any | no, past | Break as now (day consumed). |
| 5 | no | training | yes | `count += 1` |
| 6 | no | training | no, today | Stop with **PendingToday** (or **Broken** if count is 0, as now). |
| 7 | no | training | no, past | **If count is 0: nothing** (day `Missed`). Otherwise as now: `debt = yes`, `owed = d`, `count += 1` provisionally. |
| 8 | no | rest | any | **If count is 0: nothing.** Otherwise `count += 1`. |

"The chain started with ShiftWeek" only matters for the first payment: ShiftWeek moves the schedule (12b adds a `UserSchedule` row from today), so the day that pays is the new home of the missed workout and nothing more is owed.

Also update `docs/streak-grace-rule.md` so it describes the new rules (sections 3 to 5), and mark the Phase 10 known issue as fixed in `docs/PHASE_10_FRIENDS_LEADERBOARD.md`.

### Worked examples

PPL: Mon Push, Tue Pull, Wed Legs, Thu Rest, Fri Push, Sat Pull, Sun Legs. ✔ means logged and ✘ means not logged.

**A. Shift whole schedule** — Mon ✔ (1), Tue ✘ (2, provisional), Wed: choose ShiftWeek, ✔ Pull (3). State **Active**, not Shifted. From Wed the week is Wed Pull, Thu Legs, Fri Rest, Sat Push, Sun Pull, Mon Legs, Tue Push.

**B. Make it up before the rest day** — Mon ✔ (1), Tue ✘ (2), Wed: choose ShiftToRest, ✔ Pull (3, **Shifted**), Thu (rest) ✔ Legs (4, **Active**). Fri is Push as normal.

**C. Skip and continue** — Mon ✔ (1), Tue ✘, Wed: choose Continue. Count is 0, state **Broken**. Wed ✔ Legs → 1, **Active**.

**D. Not chosen yet** (today is Wed) — Mon ✔, Tue ✘, Wed ✘. Count 2, state **NeedsChoice**, `OwedDay` = Tue.

**E. Chose but didn't train** — Mon ✔, Tue ✘, Wed: ShiftWeek, ✘. On Thu: Wed breaks (consumed), count 0.

**F. Inactive user (the bug)** — no schedule (every day is a training day). Last workout 1 Oct: 2 Oct ✘ (2, debt), 3 Oct ✘ → break, 0. From 4 Oct the count stays **0** every day, status **Broken**, never NeedsChoice.

**G. Inactive user with rest days** — PPL. After a break, the next Thu (rest) stays 0.

**H. Old miss with no choice row** — Mon ✔, Tue ✘, Wed ✔ with no choice stored. Same result as B on Wed: 3, **Shifted**.

### Tests

Add to `StreakCalculatorTests`:
1. Examples A to H, one test each.
2. Timeline: Continue makes the owed day `Missed`; ShiftWeek and ShiftToRest make it `Graced` once paid.
3. A ShiftToRest chain across several training days, ending in a logged rest day, keeps the streak.
4. Example C from the grace rule spec (two misses in a row) now gives **0** on the Thu rest day, not 1. Update that test and list it in your report.
5. All other existing tests pass unchanged. If one doesn't, stop and tell me which and why before changing it.

Add a `StreakServiceTests` (or extend the existing service tests) case that loads choices from the database: the same log history gives different results with ShiftWeek, ShiftToRest and Continue stored.

`LeaderboardServiceTests.FriendsBrokenStreak_ShowsZero_EvenWithAStaleCache` should still pass. Add one test where an inactive friend shows 0 on a date that used to give 1.

---

## 12b: Missed-day choice (service and Home card)

Read `WorkoutService` (especially `PlanAsync`, `GetCheckInAsync`, `LogTodayAsync`), `SplitService`, `SplitSchedule`, `StreakService`, `PointsService`, `HomeViewModel` and `HomeView` first.

### Service

Add **`MissedDayService`** (or put it in `StreakService` if that reads better):

- **`GetPromptAsync(userId, today)`** → null unless the streak status is NeedsChoice. Otherwise a `MissedDayPrompt` with: the missed date, the missed day's name ("Pull"), the streak at stake, how many of each shift option are left this week, and the options that are available, each with its preview text (below).
- **`ChooseAsync(userId, MissChoice, today)`**: checks the status is still NeedsChoice, `OwedDay` is yesterday and the option is allowed, then stores the row, recalculates the streak and syncs points. Return a result with an error message instead of throwing.
  - **ShiftWeek on a preset:** `SplitStartDay` moves one day later (Sunday wraps to Monday).
  - **ShiftWeek on a custom split:** every `SplitDay.Weekday` moves one day later.
  - In both cases, add a `UserSchedule` row effective **today** with the new pattern (replace it if a row for today already exists).

### Today's workout during a "make it up" chain

While a debt from a ShiftToRest chain (or an old miss with no choice) is open, **today's workout is yesterday's scheduled workout**. In `WorkoutService.PlanAsync`, when the streak has an `OwedDay` and the user is in that state, plan the owed day's date instead of today. This also applies on the rest day that ends the chain: it shows yesterday's workout, so there's no rest-day picker that day.

ShiftWeek needs nothing here, because the schedule itself has moved.

### Check-in

`LogTodayAsync` returns `"Choose what to do about yesterday's missed workout first."` while the status is NeedsChoice.

### Home card

When `GetPromptAsync` returns a prompt, Home shows a card above the check-in, and the check-in button is disabled with that hint.

> **You missed Pull yesterday.** Your 12-day streak is at stake. What do you want to do?
>
> **Shift my whole schedule by a day** — Pull today, and every day after moves one day later. Your rest day moves from Thursday to Friday. *(Then a small 7-day row showing the new week.)*
>
> **Make it up before my next rest day** — Pull today, Legs on Thursday instead of resting, back to normal on Friday.
>
> **Skip it and continue** — Today stays Legs. Your streak resets to 0.

Each option asks for confirmation. "Skip it" names the number: "Reset your 12-day streak to 0?". Shift options that aren't allowed this week are hidden, and the card says why.

`StreakNote` texts:
- **NeedsChoice:** "You missed yesterday. Choose what to do to keep your streak."
- **AtRisk** after a choice: "Do {day} today to keep your streak."

### Tests

- `ChooseAsync` with each option: the stored row, the streak result, and points after sync.
- ShiftWeek on a preset moves `SplitStartDay` and adds a `UserSchedule` row from today. On a custom split it moves every weekday.
- A second "Shift my whole schedule" in the same week is refused; a third "Make it up" in the same week is refused. "Skip it" is still allowed.
- Using "Shift my whole schedule" doesn't use up a "Make it up", and the other way round.
- A miss on Sunday and one on the next Monday count in different weeks.
- `ChooseAsync` is refused when the status isn't NeedsChoice (including the day after the deadline).
- `LogTodayAsync` is refused while NeedsChoice and works after a choice.
- During a ShiftToRest chain, the check-in uses yesterday's scheduled day, including on the rest day that ends the chain.
- Undoing today's check-in after a choice keeps the choice and puts the status back to AtRisk.

---

## 12c: Dashboard

Home already shows the streak, level, today's calories, the check-in and the friends card with your weekly rank. Add what the plan still lists, and arrange everything as one dashboard.

- **This week:** a Monday-to-Sunday row of 7 chips, from `StreakCalculator.Timeline`: done ✔, made up ↺ (Graced), missed ✘, rest, upcoming. Today is highlighted.
- **Today's workout:** the day's name and its exercises in `Order`, with planned sets (for example "3 × working, 1 warm-up") and last time's top set. Use the same plan as the check-in, so it follows shifts. On a rest day: "Rest day". With no exercises: the existing "build a workout" link.
- **Recent PRs:** the last 3 PRs (exercise, value, date), from the existing PR data in `SetService`/`PrCalculator`. Hidden when there are none.
- **Weekly rank:** keep the existing friends card.

Layout: a two-column grid of cards that drops to one column when the window is narrow. Card order: streak + check-in, today's workout, this week, calories, level, recent PRs, friends.

No new data and no migration. If the view model gets too big, split the cards into child view models.

### Tests

- A service or Core test for the week row: done, graced, missed, rest and upcoming chips for a mid-week date.
- Recent PRs: order by date and limit to 3.
- Today's workout during a ShiftToRest chain shows yesterday's day.

---

## 12d: Validation and error handling

**Start with an audit and stop for review before fixing anything.** List, in a table:
- every user input (register, login, profile and body data, calories, sets, custom exercises, custom splits, custom gyms, friend username, search fields), with its current validation and any gaps
- every network call (Overpass, Nominatim, anything else), with what happens when it's offline, slow, rate-limited or returns bad data
- every place an exception can reach the UI

Then fix the gaps with these rules:
- Validation lives in services (like the set limits in `SetService`) and returns a message. The UI mirrors it inline next to the field.
- Number input always goes through `NumberParsing`.
- Network failures show a friendly message with a **Retry** button. The app works offline for everything except gyms from OpenStreetMap.
- Add a global handler for unhandled exceptions (`AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, plus the UI dispatcher). It writes the error to a log file in the app's data folder and shows "Something went wrong" instead of crashing.
- No raw exception text in the UI.

### Tests

One test per gap you fix where it can be tested in Core or a service.

---

## 12e: Theming and visual polish

- Move colours, spacing, corner radius and font sizes into a shared resource file (for example `Styles/AppTheme.axaml`) with **Light** and **Dark** theme dictionaries. Replace hard-coded colours in views with these resources.
- Check every view in both Light and Dark. Nothing should be unreadable in either.
- A **Theme** setting in Profile: System (default), Light, Dark.
- Replace `Assets/avalonia-logo.ico` with a GymApp icon, and set the window title to "GymApp".
- Consistent empty states (no split, no exercises, no friends, no PRs, no calories logged) with one line of text and an action button.

### Defaults to confirm

- The theme setting is stored on the user (`ThemePreference`, migration `ThemePreference`) and applied on sign-in. The login screen follows the system.
- I'll provide the icon file. If it isn't in `Assets/` when you get here, make a simple placeholder and say so.

---

## 12f: Release prep (v1.0.0)

- Add a `Directory.Build.props` at the repo root with `<Version>1.0.0</Version>`, so every project shares it.
- Show "GymApp 1.0.0" at the bottom of Profile, read from the assembly version.
- Add a **`README.md`**: what the app does, how to build, run and test it, and the `dotnet publish` command for macOS (osx-arm64) and Windows (win-x64).
- Add a **`CHANGELOG.md`** with a 1.0.0 entry that summarises Phases 0 to 12 in a few lines each.
- Add `~$*` (Word lock files) to `.gitignore`.
- List everything in the implementation plan doc (the .docx) that needs updating, so I can update it myself.
- Run the full build and test suite, and run the app once through the main flows: register, pick a split, build a day, check in, undo, log calories, add a friend, open the leaderboard, open the gym picker.

I'll tag `v1.0.0` myself after reviewing.

### Versions after 1.0.0

- **1.0.x**: bug fixes only, no new features and no schema changes
- **1.x.0**: new features (optional extras, the sync server), migrations allowed
- **2.0.0**: a breaking change, like a migration that can't carry existing data forward or a change to seeded ids

---

## Known limitations (1.0.0)

- **Friends only work on the same database.** The sync server (10f) isn't part of 1.0.
- **Points are computed on the client** and can't be verified (from Phase 10).
- **Set values can be made up** (from Phase 11). The check-in rule only stops one-tap check-ins.
- **The missed-day choice needs the app opened the next day.** There are no notifications, so a user who forgets to open the app loses the streak at midnight.
