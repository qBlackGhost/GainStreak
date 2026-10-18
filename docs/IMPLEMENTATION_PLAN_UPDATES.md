# Updates needed in GymApp_Implementation_Plan.docx

These are the places where the plan (dated 17 October 2026) no longer matches the code. They're listed in the plan's own order, each with the current text, the change, and the source. Items marked **(after 12e)** depend on 12e, which isn't done yet.

## Header

- **Intro paragraph:** "Phases 0 to 10 are done … Phase 11 … comes next, then Phase 12, Polish."
  → Phases 0 to 12 are done for **v1.0.0** (12e, theming, last). 10f (the sync server) is still not started and isn't part of 1.0. Update the date.

## Overview table

- **Phase 4 status:** "Done (grace rule added with Phase 8)" → add "missed-day choices added in Phase 12".
- **Phase 11:** "Next" → **Done**.
- **Phase 12:** "Planned" → **Done** (or "In progress" until 12e is finished).

## Phase 2: Accounts

- "case-insensitive unique usernames (3 to 32 characters)" → add: **letters, digits, `_ . -` only** (no spaces or emoji). New accounts only; existing names still log in. Passwords are **8 to 128 characters and not only spaces** (12d, `Credentials`).

## Phase 4: Workout logging and streaks

- "rest days count, a missed training day resets it" is out of date. Replace it with the current rules (docs/streak-grace-rule.md, sections 3 to 5):
  - **No streak, nothing to save:** a training day missed while the streak is 0 changes nothing, and after a break rest days don't count until the next logged workout. This fixes the Phase 10 known issue.
  - **Missed-day choice** the day after a missed training day: **shift my whole schedule by a day** (at most once per Monday-to-Sunday week), **make it up before my next rest day** (twice per week, the old grace rule), or **skip it and continue** (the streak resets). Check-in is blocked until the user chooses. A miss with no choice stored behaves like "make it up".
  - New table `MissedDayChoice` (migration `MissedDayChoices`).
  - While a make-up chain is open, today's workout is **yesterday's scheduled workout** (in the check-in, Home and the dashboard).

## Phase 6: Profile and body data

- "daily steps (a number you enter)" → the profile stores an **activity level** picked from step bands (`ActivityLevel`), not a step count.
- The 5-band step table → **3 levels**: Less Active (under 7,500 steps a day), Active (7,500 to 12,500) and Very Active (over 12,500). The factors are in `BodyMetrics.ActivityFactor`.
- "a body measurement log … optional later" → still not done; move it to the optional extras for 1.1.

## Phase 7: Calories and goal selection

- "Define hit the target per mode: lose = at or under, gain = at or over, maintain = within ±5–10%" → the code uses **one rule for every goal type**: within a leeway of **15% of the goal, kept between 200 and 300 kcal** (`CalorieGoals.Leeway`). For example, 1,530 to 2,070 for a 1,800 goal.
- "Optional: a weekly rate selector" → not done; move it to the optional extras.

## Phase 8: Points and levels

- "+50 weekly completion" → add **+25 for a week completed with a made-up day** (`WeekCompleteGrace`). It shares the week's key with the +50, so a week earns one or the other.
- "Optional PR bonus (for example +10)" → **not implemented. PRs and sets never earn points** (decided in the Phase 8 spec). Remove it, together with "the PR bonus cap above" under Suggestions → Anti-cheat.

## Phase 9: Gym map

- Add what was built beyond the plan: a home city on the profile, **custom gyms** (up to 10, with an address picked from a search inside the city), approximate addresses from reverse lookups, the optional "Want to log your gym?" prompt after check-in with "Don't ask me again", and the "Gyms visited per month" screen.
- "Gym picker on both ways of logging a workout (the I went today button and the Workouts menu)" → the picker opens **after check-in** and from Home's "Add gym" / "Change gym" link, for today's log only.

## Phase 10: Friends and leaderboard

- **Known issue** paragraph → mark as **fixed in Phase 12a** (no streak, nothing to save). Test: `LeaderboardServiceTests.InactiveFriend_ShowsZero_OnADayThatUsedToGiveOne`.

## Phase 11: Undo check-in and sets on check-in

- Heading "(next)" → **(done)**.
- Add what 11 also shipped: last session's sets as **placeholders** in the check-in rows, and the undo confirmation naming the **points it takes away** (for example "−5 Workout · −50 Week complete").

## Phase 12: Polish

Replace the two bullets with the sub-phases (full spec: docs/PHASE_12_POLISH.md):

- **12a, streak rules:** the "no streak, nothing to save" fix and the stored missed-day choice (see Phase 4).
- **12b, missed-day card:** a Home card with the three options, previews, a 7-day row for "shift", weekly limits (`MaxWeekShiftsPerWeek = 1`, `MaxMakeUpsPerWeek = 2`), a confirmation for each option, and "Your streak ended on …" shown once.
- **12c, dashboard:** Home as a two-column grid that drops to one column when narrow: streak and check-in, today's workout (exercises, planned sets, last top set), this week (✔ ↺ ✘ rest upcoming), calories, level, recent PRs (last 3), friends.
- **12d, validation and errors:** audit in docs/PHASE_12D_AUDIT.md. Messages next to each field, whole-number parsing, Retry for online searches, an offline note on the map, and a global error handler writing `errors.log` in the data folder.
- **12e, theming (after 12e):** a shared theme file with Light and Dark, a Theme setting in Profile (System, Light, Dark) stored as `User.ThemePreference`, the app icon, and empty states.
- **12f, release:** `Directory.Build.props` (version 1.0.0), the version at the bottom of Profile, README.md, CHANGELOG.md.

## Suggestions and decisions

- **Anti-cheat:** remove "and the PR bonus cap above" (there's no PR bonus).
- **Optional extras:** these are for **1.1 and later**. Add the weekly rate selector and the body measurement log (moved from Phases 6 and 7).
- Add a **Versioning** paragraph: 1.0.x is bug fixes only (no schema changes), 1.x.0 is new features (migrations allowed), 2.0.0 is breaking changes (a migration that can't carry data forward, or a change to seeded ids).
- Add **Known limitations (1.0.0)**: friends only on the same database, points computed on the client, set values can be made up, and the missed-day choice needs the app opened the next day.

## Appendix: data model

- **UserProfile:** "StepsPerDay, UnitSystem" → `ActivityLevel`, `Units`, plus `HomeCityId` (9) and `AskForGymAfterCheckIn` (9).
- **Gym (change), 9:** "Add OsmId" → `OsmType`, `OsmId` (unique per city), `CityId`, `OwnerUserId` (custom gyms), `Address`, `LookedUpAddress`, `AddressLookedUpAt`, `Latitude`, `Longitude`, `IsActive`.
- **Add City, 9:** `OsmType`, `OsmId`, `Name`, `DisplayName`, centre, bounding box, `GymsFetchedAt`.
- **Add WorkoutLog (change), 9:** `GymId`.
- **Add MissedDayChoice, 12:** `UserId`, `Date` (the missed day), `Choice` (ShiftWeek, ShiftToRest, Continue), `CreatedAt`; unique per user and date.
- **Add User (change), 12 (after 12e):** `ThemePreference`.
