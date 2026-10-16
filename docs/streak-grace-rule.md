# Streak Grace Rule (Shift model)

Spec for adding the grace rule to the existing streak calculator (Phase 4b). This document defines behaviour only; implement it inside the existing calculator and its tests.

> **Workflow note for Claude Code:** do not commit or push anything to git. Create or modify the files, then list every file you created or changed and where it lives, so I can review and integrate it myself.

---

## 1. The rule in one sentence

A missed training day is forgiven only if a workout is logged the next day. If that next day is also a training day, that workout pays for the missed day, and the next day becomes the new missed day. The schedule effectively **shifts back by one day** until a rest day absorbs the slip.

---

## 2. What stays the same

- Rest days count toward the streak (+1 each), whether or not a workout is logged on them.
- Whether a day is a training day or a rest day is decided by the **schedule active on that date** (existing schedule history). This applies to every day in the walk, including days inside a shift chain.
- Workout logging is today-only. Past days can never change, so a result computed for a past date is stable.
- Exercise set logs (SetLog) do **not** count. Only the "I went today" WorkoutLog counts.
- At most one WorkoutLog per day.
- **No new database fields or migrations.** Grace is fully computed from the WorkoutLogs and the schedule history.

---

## 3. Algorithm (forward walk)

Walk **forward**, day by day, from the start of the user's schedule history up to and including today. Carry two values:

- `count`: the current streak length, measured in days.
- `debt`: a yes/no flag meaning "a training day is still owed from yesterday". In the shift model the debt is never more than one day, so a boolean is enough.

For each day `d`, take its type (training or rest, from the schedule active on `d`) and whether a workout is logged on `d`. Then apply the first matching row:

| # | Debt open? | Day type | Logged? | Result |
|---|---|---|---|---|
| 1 | yes | rest | yes | Debt paid; the rest day absorbed the slip. `debt = no`, `count += 1` |
| 2 | yes | training | yes | Debt paid, but `d`'s own training is now owed (shift). `debt` stays yes, `count += 1` |
| 3 | yes | any | no, and `d` is **today** | Not broken yet. Stop the walk with state **AtRisk** (count excludes today) |
| 4 | yes | any | no, `d` in the past | **Break.** `count = 0`, `debt = no`. Day `d` is consumed by the break; counting resumes on `d + 1` |
| 5 | no | training | yes | `count += 1` |
| 6 | no | training | no, and `d` is **today** | Not missed yet. Stop the walk with state **TodayPending** (count excludes today) |
| 7 | no | training | no, `d` in the past | Missed. `debt = yes`, `count += 1` (the day is counted provisionally; row 4 discards it if the debt is never paid) |
| 8 | no | rest | any | `count += 1` (an extra workout on a rest day with no debt has no special effect) |

If the walk finishes today without stopping:

- If `debt` is no, the state is **Active**.
- If `debt` is yes, today was logged but still owes a workout tomorrow, so the state is **Shifted**.

If `count` is 0 at the end, the state is **None**.

### Days with no active schedule

Treat them the same way the existing 4b calculator does. Do not change that behaviour as part of this work.

---

## 4. Result returned by the calculator

The calculator returns:

- **Count:** the streak length in days.
- **State:** one of the following.
  - **None:** there is no streak.
  - **Active:** everything is up to date, and today is kept.
  - **TodayPending:** nothing is owed, but today's training hasn't been logged yet.
  - **AtRisk:** a miss from yesterday is open, and today isn't logged yet. Home screen message: "Work out today to keep your streak".
  - **Shifted:** a workout was logged today, but the schedule slipped by a day, so a workout must be logged tomorrow too. Home screen message: "You're one day behind, so train tomorrow (or on your next rest day)".

If the existing result type has only a count, extend it with the state. Update the Home view model so it uses the state, and keep the UI change minimal: just a line of text under the streak.

---

## 5. Worked examples

These examples use PPL: Mon Push, Tue Pull, Wed Legs, Thu Rest, Fri Push, Sat Pull, Sun Legs. In the tables, ✔ means logged and ✘ means not logged.

**A. Miss absorbed by the rest day**

| Mon T ✔ | Tue T ✘ | Wed T ✔ | Thu R ✔ |
|---|---|---|---|
| 1 | 2 (debt) | 3 (debt, shifted) | 4 (paid) |

The final streak is 4 and the state is Active.

**B. Rest day not used to make it up**

| Mon T ✔ | Tue T ✘ | Wed T ✔ | Thu R ✘ | Fri T ✔ |
|---|---|---|---|---|
| 1 | 2 (debt) | 3 (debt) | break: 0 | 1 |

The final streak is 1.

**C. Two misses in a row**

| Mon T ✔ | Tue T ✘ | Wed T ✘ | Thu R |
|---|---|---|---|
| 1 | 2 (debt) | break: 0 | 1 |

**D. Missed yesterday, today not logged yet** (today is Wed)

| Mon T ✔ | Tue T ✘ | Wed (today) ✘ |
|---|---|---|
| 1 | 2 (debt) | stop |

The streak is 2 and the state is AtRisk.

**E. Missed yesterday, logged today, and today is a training day** (today is Wed)

The streak is 3 and the state is Shifted.

**F. Training day today, nothing owed, not logged yet**

The count runs through yesterday and the state is TodayPending.

**G. Miss on the last day before a schedule change**

Whether the next day is training or rest is decided by the **new** schedule.

---

## 6. Tests to add

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

- Grace limits, such as one grace per week. These may be added later.
- Points and levels, including whether a week with a forgiven miss earns the weekly bonus. Do not touch the points code.
- Backdating workouts. Logging stays today-only.
