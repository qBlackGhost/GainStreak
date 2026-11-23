# Phase 27: Macros and a calmer Calories screen — spec for Claude Code

This is the spec for GymApp Phase 27 (version 0.27.0). The number is a placeholder: if another phase lands first,
renumber the file, the version and the sub-phase letters. It builds on Phase 26 (`Food`, `FoodEntry`, the Add food
sheet), which is released as 0.26.0.

The Calories screen on the phones has grown crowded: the day's ring, the food diary, the 7-day chart with its legend,
the goal card with its suggestion, custom goal and safe-limits text, and the maintenance card with its formula. This
phase **cuts the screen down to what is read every day**, puts the food list inside the Add food sheet, shows
**protein, carbs and fat** as filled bars, opens a **past day as a popup with a ring split by macro**, makes
**macros required** for anything typed by hand, and lets the calorie goal carry an optional **target for each macro**.
Phones only (the desktop is frozen). Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase". 27a and 27b change the app's entities, so they also run the
  compiled-model step and an **iOS Release build for the Simulator** (full AOT). After each sub-phase, report: files
  created, files changed, the migration (27a adds one), what you checked by hand on each platform and what you
  couldn't.
- Read `CaloriesView.axaml` / `CaloriesViewModel`, `AddFoodView.axaml` and its view models (`AddFoodViewModel`,
  `FoodAmountViewModel`, `FoodFormViewModel`), `FoodService`, `FoodRules`, `CalorieService` (`CalorieGoalInfo`,
  `CalorieDay`), `User` (`DailyCalorieGoal`, `GoalType`), `Sheet.cs`, `QrCodeView.cs` (the model for a control that
  draws itself), `SegmentedPill`, `GainStreakResources.axaml` and `GainStreakStyles.axaml` before changing anything.
- Every existing test must still pass. The points rules don't change.
- **The desktop** only has to keep building and starting (CLAUDE.md, "Desktop freeze"). Its Calories screen stays as it
  is; everything here is behind `IPlatform.HasFoodLog`, as Phase 26.

## Decisions already made (don't change these)

1. **Nothing goes to the server.** No `Contracts` change, no server migration, no `MinClientVersion` change.
2. **Macros are required for anything typed by hand.** A **New food** (`Mine`) and a **Quick add** can't be saved
   without protein, carbs and fat (0 is allowed, empty isn't). Existing foods and entries that lack macros don't exist
   in practice, so there is no data conversion and no "Other" slice. The schema stays nullable; code reads a missing
   macro as 0 and never crashes on it.
3. **Kcal is calculated from the macros for what the user types**: `kcal = round(protein × 4 + carbs × 4 + fat × 9)`.
   The form shows the result live ("= 330 kcal") and has **no kcal field**. Foods from the USDA catalog and Open Food
   Facts keep the kcal they come with (label values), and a product that has energy but no macros opens the edit form
   before it can be logged, like a product without energy does today.
4. **The main screen shows today only.** A past day opens as a **popup** from the 7-day chart; the screen never
   switches to another day.
5. **The food list lives in the Add food sheet** ("Logged today"), not on the main screen. The main screen only says
   how many items are logged and opens the sheet there.
6. **Macro bars have no target by default.** Until the user sets targets, a bar's fill is that macro's **share of the
   day's calories** (protein and carbs 4 kcal/g, fat 9 kcal/g), so the three always add up to about 100%. When a target
   is set (decision 7), that bar fills **grams ÷ target** instead, capped at full with an "over" style past 100%.
7. **Macro targets are optional and live in the Calorie goal.** Grams per day for protein, carbs and fat, set on the
   goal sheet. Each can be empty. The sheet shows the kcal the targets add up to next to the calorie goal, and a hint
   (not a block) when they differ by more than 10%. The current targets are used for every day; they aren't
   snapshotted like `GoalAtTime` (a "Later").
8. **No points for macros.** Macros are shown, never judged: targets change bars, nothing else.

## Data model (27a)

- `User`: `ProteinGoalGrams?`, `CarbsGoalGrams?`, `FatGoalGrams?` (`int?`, 1 to 1,000).
- No change to `Food` or `FoodEntry`.
- Migration `MacroGoals` in `GymApp.Data`, then regenerate the compiled model (`CompiledModelTests` fails until you
  do). Test the EF change with an **iOS Release build for the Simulator**.

## Rules (27a, in `GymApp.Core`)

- `FoodRules.KcalFromMacros(protein, carbs, fat)`, as decision 3, and a validator for the hand-typed forms: all three
  present, each 0 to 100 g per 100 g (or per serving, up to the serving's grams), protein + carbs + fat ≤ 100 g per
  100 g (+ 2 g), the serving rule as today.
- `MacroSplit`: from grams to kcal per macro, share of calories (a whole number each, adjusted so the three add up to
  100), and the bar fill with and without targets.
- `MacroGoals.Check(kcalGoal, protein, carbs, fat)`: the implied kcal and whether it differs by more than 10%.
- **Tests:** the formula and its rounding, shares adding to 100 for awkward numbers (equal thirds, one macro 0, all 0
  gives no split), bar fill with and without a target, over-target capped, the 10% hint edge.

## Services (27a)

- `FoodService`: the New food and Quick add paths require macros and store the calculated kcal; editing a `Mine` food
  recalculates it and doesn't touch past entries (snapshots, Phase 26 decision 5). A catalog or Open Food Facts food
  with energy but missing macros is reported as "needs macros" and opens the form.
- `FoodService` (or a small `DayMacrosService`): `GetDayMacrosAsync(userId, date)` returning grams per macro and kcal
  for a day from its entries; a day logged only as a total has no split.
- `CalorieService`: `SetMacroGoalsAsync(userId, protein?, carbs?, fat?)`, and the three values on `CalorieGoalInfo`.
- **Tests** (`MacroTests.cs`, in-memory SQLite with the real migrations): a hand-typed food needs all three macros,
  kcal comes from the macros, a catalog food keeps its own kcal, a product without macros needs the form, day macros
  add up from several entries and meals, a day with no entries has no split, goals save, clear and validate.

**Stop and report.**

## The ring control (27b)

`MacroRing` (`GymApp.App/Views`, a `Control` that draws itself, like `QrCodeView`), used by the past-day popup.

- **Look (the chosen design "C"):** a thin ring, stroke about 6% of the diameter (12 of a 200 box, radius 82), made of
  **up to three arcs with round ends** and a visible gap of about 6 units between them (`PenLineCap.Round`; because
  the caps add half a stroke at each end, the gap along the circle is the visible gap plus one stroke width). Arc
  lengths follow `MacroSplit`'s shares; the first arc starts at 12 o'clock and runs clockwise: protein, carbs, fat. A
  macro at 0 draws nothing; one macro alone draws a full circle with no gap.
- **Centre:** the day's **kcal eaten over the day's calorie goal** ("2,010 / 1,900", the goal smaller and muted) with
  "kcal" under it. A day with no goal shows only the eaten kcal. Digits use the mono style.
- **Under the ring:** one row per macro: a round colour dot, the name, the grams, and the share as a percentage.
- **Colours:** new tokens `MacroProtein`, `MacroCarbs`, `MacroFat` in `GainStreakResources.axaml`, light and dark,
  pointing at the app's own colours: `Accent` (lime), `Flame` (orange) and `Ice` (blue). Their light-theme variants
  already exist, so don't hard-code hex values in the control. The mockups used slightly different oranges and blues;
  the tokens win.
- Accessibility: `AutomationProperties.Name` such as "Protein 26 percent, carbs 43 percent, fat 31 percent".
- Tests: the arc geometry for one, two and three macros (angles add up with the gaps, no overlap), rounding of shares,
  the zero and single-macro cases.

## Screens (27c, 27d)

### Main screen (27c), phones

Top to bottom, filling the screen height with no empty strip at the bottom:
1. Header: date and "Calories".
2. **Today card:** the progress ring with "N left" (or "N over"), "Goal G · eaten E" and the target range line, and under
   it three **macro bars** (Protein, Carbs, Fat): name, a filled bar, grams on the right (decision 6). No numbers other
   than grams.
3. **Add food** button, and a small line "N items logged today ›" under it that opens the Add food sheet scrolled to
   Logged today.
4. **Last 7 days** card: the chart without its legend; the header shows "N on target". Bars keep the on target / off
   target / not logged colours and the target band behind them; today's bar has an outline. **This card takes the
   remaining height** (a star-sized row with a minimum height), so on a tall phone the bars grow and on a short phone
   the card shrinks to its minimum and the page scrolls. Tapping a **past** day's bar opens the popup; tapping today's
   does nothing.
5. **Two rows in one card:** **Calorie goal** (value, ›) and **Maintenance** (value, ›). Each opens a sheet (below).
   The suggestion, custom goal field, goal-type pills, the safe-limits text and the formula chips are no longer on the
   page.

Remove the food diary card from this screen (it moves, decision 5). Keep the desktop layout exactly as it is.

### Add food sheet (27c)

- Keep search, barcode, meal chips and Recent. Add **Logged today** below them, with the day's total, grouped by meal
  (name, amount, kcal). Tapping an item opens the amount page to edit it, with **Save** and **Remove from today**.
- **New food** and **Quick add** forms: protein, carbs and fat fields (required), the live "= N kcal", no kcal field.
  Per 100 g or per serving as today.
- Past days stay read-only (Phase 26 decision 3): the popup's "See what you ate" opens this sheet's list in a
  read-only mode with that day's entries and no search or add.

### Goal sheet and Maintenance sheet (27c)

- **Calorie goal sheet** (`Sheet`): the current goal, the goal-type pills, the suggestion with Use and Keep, the custom
  goal field, the safe-limits note, and a new **Macro targets** section: three number fields (grams, optional), the kcal
  they imply, the 10% hint, and **Clear targets**.
- **Maintenance sheet**: "BMR × activity = TDEE" as the existing chips or rows, the note, and **Edit body data**.
- Reuse the existing view-model logic and strings; this is a move, not a rewrite. Sheets are bottom sheets on phones.

### Past-day popup (27d)

Opened from a past day's bar. A centred card on a dimmed page (a `Sheet` with the scrim), not a screen:
- Title: the day ("Wed 18 Nov") and a status line ("Over target · goal 1,900", "On target", "Not logged").
- The **`MacroRing`** with its legend (above).
- **See what you ate** (outline button) opening the read-only list. Close with the X, Escape or a tap on the dimmed
  page.
- A day with no entries but a logged total ("Logged as a total") shows the kcal and no ring split. A day with nothing
  logged says so and has no ring.
- Tests: the view models for the main screen (macro bars with and without targets, the items line, past-day taps open
  the popup, today's does nothing), the sheets, the popup's states.
- On the iOS Simulator and the Android emulator: light and dark, a small phone and a tall one (no scrollbar on the tall
  one, the graph grows), macros typed by hand, kcal calculated, a goal with and without targets, a past day opened.

**Stop and report**, with screenshots.

## Design review after 27d (decided with the developer)

These replace the matching parts above (decision 7, the main screen in 27c, the goal and Maintenance sheets).

- **Main screen (phones):** no date or "Calories" header; the first card is titled **Calories** with the status pill. The
  ring is 150 pt, its progress arc **lime** up to the goal and orange past the range (`Arc.phoneRing`). Beside it, **Goal** and
  **Eaten** as labelled numbers and "Target lo–hi". **Manage food** replaces Add food and the "N items logged today" line;
  it opens the sheet at Logged today when something is logged. The Calories card, Manage food and Last 7 days fill the
  screen (`FoldPage`); the Calorie goal and Maintenance rows are below the fold.
- **Calorie goal sheet:** Lose / Maintain / Gain, then a **Recommended / Custom** pill. Macros come first; the calorie goal
  is what they add up to (`MacroPlan`).
  - Recommended, grams per kg (carbs for Active; ±0.5 g/kg per activity step):

    | Goal | Protein | Fat (men / women) | Carbs (men / women) |
    |---|---|---|---|
    | Lose | 2.2 | 0.7 / 0.9 | 2.5 / 2.0 |
    | Maintain | 1.8 | 0.9 / 1.0 | 4.0 / 3.5 |
    | Gain | 1.8 | 1.0 / 1.1 | 5.0 / 4.5 |

    Protein uses the lower of the weight and the weight at BMI 27. Under the safe minimum (`CalorieGoals.SafeMinimum`)
    carbs go up until it is met. Needs the same body data as the maintenance estimate.
  - The weight is the **14-day average** of weigh-ins (else the latest). Recommended targets move only when that average is
    **1 kg** from the weight they were worked out from (`User.MacroBasisWeightKg`), and right away after a body data
    change. When the weight moved them, the Calories screen says so once ("Your targets changed to …").
  - Custom: protein, carbs and fat are all required (0 to 1,000 g); the calorie goal is calculated and read-only, within the
    existing safety limits. "Start from recommended" copies the suggestion in. Custom targets never move by themselves.
  - `User.MacroMode` (null until picked: the goal stays as it was), migration `MacroPlan`.
- **Maintenance:** the sheet is gone. The row shows the maintenance kcal and opens Profile, Body data.

## Later design changes (decided with the developer, in 0.27.0)

- **Macro rows:** "eaten / target" above each bar ("90 / 144 g"), no percentage. Past a target the bar and its text are red
  (`MissRed`); calories past the target range turn the ring, Eaten and the pill red; under the range the pill stays orange.
- **Shared card:** the Calories card's ring, Goal / Eaten, target and bars are one control (`TodayCaloriesCard` +
  `TodayCaloriesViewModel`), used by the Calories screen and Home's calories card (phones), which gets **Manage food**.
- **Profile (phones):** a **Body** card on Main with a weight chart after Apple Health's (`WeightTrendChart`,
  `WeightChartModel`: W / M / 6M, touch-and-hold scrub over whole-day columns, swipe for the previous window, tap to peek), Log,
  **History** (a sheet of every weigh-in by month with the change and Delete) and **Body data ›**. Vacation is its own card.
  Body data shows only the measurements; the Body widget and a weight-only setup step open the Body card.

## Gyms from our own server (in 0.27.0, before 27e)

Planned as Phase 28 in the handoff; shipped in 0.27.0 at the developer's request. Server: `OsmGyms` / `OsmGymCountries`
(migration `OsmGyms`), `GET /v1/gyms?south=&west=&north=&east=` (signed in, global read limit; box at most 2° each way and
2,000 gyms; `Covered` when a country loaded in the last 45 days contains the box), `import-gyms [--country romania]
[--file path]` (osmium filter + GeoJSON export, centre of the bounding box for ways and relations, one transaction, missing gyms
made inactive; osmium in the server image). Shared tag rule `Core/OsmGyms`. App: `GymFetchService` asks `IServerGyms` first
and falls back to Overpass when the server can't answer or the box isn't covered; the local (TestBench) mode has no server
source. Contracts: new `ServerGym`, `GymBoxResponse` only; `MinClientVersion` unchanged. To do outside the code: run
`migrate` and a first `import-gyms` on the beta server, and a weekly Cloud Run Job.

## Release (27e)

- `CHANGELOG.md` 0.27.0 for testers in the existing style: a calmer Calories screen, macro bars, the past-day popup,
  macros required for foods you add yourself, macro targets in the calorie goal. End with **Migration:** app database
  (`MacroGoals`, `MacroPlan`). `Directory.Build.props` to 0.27.0.
- `docs/STORE_LISTING.md` and `site/privacy.html` need no change (nothing new is collected). Say so in the report.
- The end-of-phase checks from CLAUDE.md.

**Stop and report**, with what must be checked on a real phone.

**Check on a real phone:**
- the Calories screen on the real phone: no scrollbar with a normal day, the graph fills the space, nothing cut off by
  the tab bar or the home indicator
- logging a food, then finding and editing it from "N items logged today"
- a hand-typed food and a Quick add, with the calculated kcal matching the macros
- macro targets: bars switch from share-of-calories to target fill, the 10% hint, Clear targets
- tapping several past days in the chart: popup ring, percentages adding up, "See what you ate"
- light and dark themes, large text sizes

## Defaults to confirm (change here if wrong)

- **Macro colours:** lime, orange and ice blue for protein, carbs, fat (from the app's tokens).
- **Rounding:** percentages are whole numbers adjusted to total 100; grams are shown rounded to a whole number.
- **Targets are grams**, not a percentage of the calorie goal.
- **Today's bar** in the chart does nothing when tapped.

## Later (not this phase)

- Snapshotting macro targets per day (like `GoalAtTime`) so changing a target doesn't rewrite the past.
- A protein target suggested from body weight; targets as a percentage split.
- Macros on Home's card and in the widgets.
- Editing yesterday's entries (Phase 26's "Later").
