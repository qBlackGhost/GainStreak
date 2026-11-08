# Streak Grace Rule (Shift model)

Spec for adding the grace rule to the existing streak calculator (Phase 4b). This document defines behaviour only; implement it inside the existing calculator and its tests.

> **Workflow note for Claude Code:** do not commit or push anything to git. Create or modify the files, then list every file you created or changed and where it lives, so I can review and integrate it myself.

---

## 1. The rule in one sentence

A missed training day is forgiven only if a workout is logged the next day. If that next day is also a training day, that workout pays for the missed day, and the next day becomes the new missed day. The schedule effectively **shifts back by one day** until a rest day absorbs the slip.

---

## 2. What stays the same

- Rest days count toward the streak (+1 each), whether or not a workout is logged on them.
  > **Changed in 0.18.1.** From the day a user first runs 0.18.1 (`User.StreakRestRuleFrom`, passed to the calculator as a `RestDayRule`), a rest day keeps the streak without adding to it, unless a **verified** workout is logged on it. That applies in rows 2 and 8: a rest day that pays a debt keeps the streak but adds only if verified, and after a break only a verified rest-day workout starts a new streak. A rest day today that added nothing (and paid nothing) returns `Stale`, shown as a streak on ice. Rest days before the start day still count +1.
- Whether a day is a training day or a rest day is decided by the **schedule active on that date** (existing schedule history). This applies to every day in the walk, including days inside a shift chain.
- Workout logging is today-only. Past days can never change, so a result computed for a past date is stable.
- Exercise set logs (SetLog) do **not** count. Only the "I went today" WorkoutLog counts.
- At most one WorkoutLog per day.
- **No new database fields or migrations.** Grace is fully computed from the WorkoutLogs and the schedule history.

---

## 3. Algorithm (forward walk)

> **Updated in Phase 12a.** The day after a missed training day the user chooses what to do about it, and the choice is stored (`MissedDayChoice`, one row per missed day). Section 2's "no new database fields" no longer holds for this. A miss with no stored choice behaves like **ShiftToRest**, so history from before 12a gives the same streaks and points.

The choices (`MissChoice`, keyed by the **missed** date):

- **ShiftWeek** ("Shift my whole schedule by a day"): the schedule moves one day later from the day after the miss (a new `UserSchedule` row). The workout that pays the miss is its new home, so nothing more is owed.
- **ShiftToRest** ("Make it up before my next rest day"): the shift chain below, until a rest day absorbs the slip.
- **Continue** ("Skip it and continue"): the streak resets before the next day, and that day counts normally.

Walk **forward**, day by day, from the user's first logged workout up to and including today. Carry:

- `count`: the current streak length, in days.
- `debt`: whether a training day is still owed, and `owed`: which one.
- whether the owed day is a **fresh miss** (missed while no debt was open), as opposed to a day owed inside a shift chain. Only a fresh miss takes a choice.

For each day `d`, take its type (training or rest, from the schedule active on `d`) and whether a workout is logged on `d`. Then apply the first matching row:

| # | Debt open? | Day | Logged? | Result |
|---|---|---|---|---|
| 1 | yes, owed day is a fresh miss with choice **Continue** | any | any | **Break before the day:** `count = 0`, `debt = no`, owed day Missed. Then handle `d` with rows 5 to 8 (the day is not consumed) |
| 2 | yes | any | yes | Paid: `count += 1`, owed day Graced. If the owed day is a fresh miss with choice **ShiftWeek**: `debt = no`. Otherwise `debt = d is training`, `owed = d` (the schedule shifts) |
| 3 | yes | any | no, `d` is **today** | Stop. **NeedsChoice** if the owed day is a fresh miss with no choice, otherwise **AtRisk** (count excludes today) |
| 4 | yes | any | no, `d` in the past | **Break.** `count = 0`, `debt = no`. Day `d` is consumed by the break |
| 5 | no | training | yes | `count += 1` |
| 6 | no | training | no, `d` is **today** | Stop with **PendingToday** (**Broken** if count is 0) |
| 7 | no | training | no, `d` in the past | **If count is 0: nothing** (no streak, nothing to save). Otherwise `debt = yes`, `owed = d` (a fresh miss), `count += 1` provisionally |
| 8 | no | rest | any | **If count is 0: nothing**, unless a workout is logged on it (that starts a new streak). Otherwise `count += 1` |

Rows 7 and 8 are the fix for the Phase 10 known issue: an inactive user's streak stays at 0 instead of flipping between 1 and 0, and rest days after a break don't count until the next logged workout.

If the walk finishes today without stopping: **Active** if `debt` is no, **Shifted** if it is yes. If `count` is 0 at the end, the state is **Broken**.

### Days with no active schedule

Unchanged: the first schedule entry also covers earlier days, and with no schedule at all every day is a training day.

---

## 4. Result returned by the calculator

`StreakCalculator.Calculate(workoutDates, schedule, choices, today)` returns the current count, the longest settled count, the state, and **`OwedDay`**: the training day the open debt is for, or null. `Timeline` takes the same `choices`.

- **NotStarted:** no workout logged yet.
- **Active:** everything is up to date, and today is kept.
- **PendingToday:** nothing is owed, but today's training hasn't been logged yet.
- **NeedsChoice:** yesterday's training day was missed, a streak is at stake, no choice is stored, and today isn't logged. Logging today is blocked until the user chooses.
- **AtRisk:** a day is owed (a choice was made, or a shift chain is open) and today isn't logged yet.
- **Shifted:** a workout was logged today, but the schedule slipped by a day, so a workout must be logged tomorrow too.
- **Broken:** the count is 0 after a break.

---

## 5. Worked examples

PPL: Mon Push, Tue Pull, Wed Legs, Thu Rest, Fri Push, Sat Pull, Sun Legs. ✔ means logged and ✘ means not logged.

**A. Shift whole schedule:** Mon ✔ (1), Tue ✘ (2, provisional), Wed: choose ShiftWeek, ✔ (3). State **Active**. From Wed the week is Wed Pull, Thu Legs, Fri Rest, Sat Push, Sun Pull, Mon Legs, Tue Push.

**B. Make it up before the rest day:** Mon ✔ (1), Tue ✘ (2), Wed: choose ShiftToRest, ✔ (3, **Shifted**), Thu (rest) ✔ (4, **Active**). Fri is Push as normal.

**C. Skip and continue:** Mon ✔ (1), Tue ✘, Wed: choose Continue. Count 0, **Broken**. Wed ✔ → 1, **Active**.

**D. Not chosen yet** (today is Wed): Mon ✔, Tue ✘, Wed ✘. Count 2, **NeedsChoice**, `OwedDay` = Tue.

**E. Chose but didn't train:** Mon ✔, Tue ✘, Wed: ShiftWeek, ✘. On Thu: Wed breaks (consumed), count 0.

**F. Inactive user (no schedule):** last workout 1 Oct. 2 Oct ✘ (2, debt), 3 Oct ✘ → break, 0. From 4 Oct the count stays **0**, status **Broken**, never NeedsChoice.

**G. Inactive user with rest days:** Mon ✔, Tue ✘, Wed ✘ → break. Thu (rest) stays **0** (before 12a it was 1).

**H. Old miss with no choice row:** Mon ✔, Tue ✘, Wed ✔ with no choice stored. Same as B on Wed: 3, **Shifted**.

**Shift chain across several training days:** a miss with ShiftToRest (or no choice) followed by workouts on every following training day and a logged rest day keeps the streak, with each shifted day Graced.

**Miss on the last day before a schedule change:** whether the next day is training or rest is decided by the **new** schedule.

---

## 6. Tests to add

> The original list from the grace rule phase. Phase 12a changed test 4 (two misses in a row now leave **0** on the Thu rest day) and added tests for every example in section 5; see `StreakCalculatorTests` and `StreakServiceTests`.

Add these tests to the existing streak calculator tests, alongside the current ones. All existing tests must still pass. Any existing test that relied on "a miss resets immediately" should be updated to the new rule; list each such test in your summary.

1. A miss followed by a workout logged on the next rest day keeps the streak (example A).
2. A shift chain across several training days ending in a logged rest day keeps the streak.
3. A shift chain ending in an unlogged rest day breaks the streak (example B).
4. Two consecutive misses break the streak (example C).
5. A miss yesterday with nothing logged today returns AtRisk (example D).
6. A miss yesterday with a workout logged today on a training day returns Shifted (example E).
7. A training day today that isn't logged, with no debt, returns TodayPending (example F).
8. A workout logged on a rest day with no debt has no special effect.
9. A miss right before a schedule change uses the new schedule for the next day (example G).
10. No logs at all returns None with a count of 0.
11. Rest days only, without logs, still count.

---

## 7. Out of scope

- Grace limits, such as one grace per week. (Phase 12b adds weekly limits on the missed-day choices.)
- Points and levels, including whether a week with a forgiven miss earns the weekly bonus. Do not touch the points code.
- Backdating workouts. Logging stays today-only.
