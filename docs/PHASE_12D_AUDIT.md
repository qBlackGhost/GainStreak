# Phase 12d: validation and error handling audit

The audit below was reviewed and every proposed fix was approved, including the three decisions at the end. **All gaps are now fixed** (see "Status" right below); the tables keep the original findings.

## Status

| Gap | Status |
|---|---|
| G1 usernames | Fixed: `Credentials.CheckUsername` (letters, digits, `_ . -`; Unicode letters allowed). Registration only, so existing names still log in |
| G2 passwords | Fixed: 8–128 characters, not only spaces (`Credentials.CheckPassword`) |
| G3 inline errors | Fixed: `Field` on `AuthResult`, `ProfileResult`, `SplitResult`, `ExerciseResult`, `CustomGymResult` and `SetResult` (names in `FormField`). Register, profile (body data, weight, calorie goal), split editor, custom exercise, custom gym form, PR edit and Home calories show messages under the field (style `TextBlock.fieldError` in `App.axaml`) |
| G4 feet | Fixed: `NumberParsing.TryParseWhole` |
| G5 whole numbers | Fixed: `NumberParsing.TryParseWhole` only accepts `,` `.` or a space between groups of exactly 3 digits. `Units.TryParseWholeNumber` now uses it |
| G6 set rounding | **No change needed.** `SetService.SaveDayAsync` already rounded to 2 decimals; the audit missed it. A test now checks it |
| G7 search Retry | Fixed: Retry under the city search (gym picker, profile) and the address search, shown only when the request itself failed (`CanRetry` on the search results) |
| G8 address lookup Retry | Fixed: Retry next to "Couldn't look up every address" |
| G9 offline map | Fixed: the tile layer's `DataChanged` error shows "Map tiles need an internet connection. The gym list still works." under the map |
| G10 missed-day message | Fixed: a refresh failure after a saved choice says "Your choice was saved, but Home couldn't refresh." |
| Section 3 | Fixed: `ErrorLog` (Services) and `ErrorHandling.Install` (Desktop) hook the UI dispatcher, unobserved tasks and the AppDomain; a dismissible banner on the main window. Startup failures show a small window instead of crashing. Every method in 3b and 3c has its own `try` with a specific message |

Summary:
- **Services validate well.** Every write service checks its inputs and returns a message (set limits, body data, calories, splits, exercises, custom gyms, friends). The gaps are mostly in **parsing in the view models** and in **errors shown in one line at the bottom** instead of next to the field.
- **Network calls are already safe.** Overpass and Nominatim never throw for network trouble. They return friendly messages for offline, timeout, 429, 5xx and bad JSON, and there's a Retry button where gyms fail to load. The gaps are the map tiles and two searches that have no Retry.
- **The real risk is unhandled exceptions.** There is **no global handler**. 47 async view-model methods have no `try` of their own. Some of these are only called from inside another `try`, and section 3 lists the ones that aren't. If the database throws (locked, disk full, corrupt), a command crashes the app and a fire-and-forget call fails silently.

---

## 1. User inputs

| Input | Where it's parsed | Current validation (service unless noted) | Gaps | Proposed fix |
|---|---|---|---|---|
| Register: username | `AuthService.RegisterAsync` | Trimmed, 3–32 chars, unique (NOCASE) | **G1:** any characters are allowed inside: spaces, tabs, emoji, control characters ("a  b", "‮admin") | Allow letters, digits, `_ . -` only (new `Usernames.Check` in Core). Applies to new accounts only; existing names keep working |
| Register: password | `AuthService.RegisterAsync` | At least 8 chars; "Passwords do not match" (VM) | **G2:** no maximum (a pasted 1 MB string gets hashed). All-spaces passwords are accepted | At most 128 chars; must contain a non-space character |
| Register/Login: errors | VMs | One `Error` line under the form | **G3:** not shown next to the field | Per-field messages (see "Inline errors" below) |
| Login | `AuthService.LoginAsync` | Wrong or empty → "Invalid username or password." | none | — |
| Profile: sex, activity level | ComboBoxes → `ProfileService.Validate` | Enum checked | none | — |
| Profile: birth date | `DatePicker` → `BodyMetrics.CheckBirthDate` | Age 13–100, not in the future | none | — |
| Profile: height (cm) | `Units.TryParseNumber` → `NumberParsing` | 100–250 cm | none | — |
| Profile: height (ft + in) | **`int.TryParse`** for feet, `Units.TryParseNumber` for inches | Inches < 12; total 100–250 cm | **G4:** feet don't go through `NumberParsing` (spec: number input always does) | Add `NumberParsing.TryParseWhole` and use it |
| Profile: weight | `Units.TryParseNumber` → `NumberParsing` | 30–350 kg, finite | none | — |
| Calories eaten (Home) | **`Units.TryParseWholeNumber`** | 0–15,000 kcal | **G5:** "." and "," are stripped as thousands separators anywhere, so **"210.5" saves as 2,105 kcal** and "2,1" as 21. Not in `NumberParsing` | Move into `NumberParsing.TryParseWhole`: digits, with an optional `,` `.` or space only between groups of exactly 3 digits ("2,100", "2 100"). Anything else is rejected with "Enter a whole number" |
| Calorie goal (custom) | Same parser | 1,200/1,500 floor by sex, at most 6,000 | **G5** (same parser) | Same |
| Sets: kg / reps (check-in, exercise screen) | `NumberParsing.TryParseKg/Reps` | Reps 1–100, kg 0–1,000, at most 30 sets per exercise per day | **G6 (minor):** kg isn't rounded on save (60.123456 is stored as typed) | Round to 2 decimals in `SetService.SaveDayAsync`, like weigh-ins and PRs |
| Modify PR: kg, reps, 1RM | `NumberParsing` | Same limits; 1RM ≥ weight and ≤ 2,000 | none | — |
| Planned sets (warm-up / working) | +/− buttons | 0–10 / 1–15 | none | — |
| Custom exercise: name, muscle group | `ExerciseService` | Required, ≤ 60, unique per user (NOCASE), enum, max 30 | none | — |
| Custom split: name, day names, weekdays | `SplitService.Validate` | Name ≤ 40, day ≤ 20, ≥ 1 day, no duplicate weekday, max 5 | none | — |
| Custom gym: name, address | `GymService` + `CustomGyms` | Name required, ≤ 60, unique per city; address ≤ 120; max 10 | none | — |
| Friend username search | `FriendService.SearchAsync` | Trimmed, ≥ 3 chars, at most 20 results, LIKE escaped by EF | none | — |
| City search (gym picker, profile) | `GeocodingService.SearchAsync` | Trimmed, not empty, max length; one request per second | none | — |
| Address search (custom gym form) | `GeocodingService.SearchAddressAsync` | Same | none | — |
| Exercise search, gym list search | In the database or in memory | Trimmed; nothing sent over the network | none | — |

### Inline errors (G3)

Every form today shows its first error in one line at the bottom: register, profile body data, custom gym form, split editor, custom exercise form and PR edit. The spec asks for the message next to the field.

**Proposed approach:** services keep returning one message, plus an optional `Field` name (for example `ProfileResult(false, "Height must be 100 to 250 cm.", Field: "Height")`). View models put the message under that field and fall back to the bottom line when `Field` is null. This touches the result records of `AuthService`, `ProfileService`, `GymService`, `SplitService`, `ExerciseService` and `SetService.SetPrAsync`. It's the largest change in 12d, so **confirm you want it** rather than keeping the single-line errors.

---

## 2. Network calls

| Call | Offline | Slow | Rate-limited | Bad data | Retry in the UI | Gaps |
|---|---|---|---|---|---|---|
| Overpass gym fetch (`GymFetchService`) | "Couldn't reach the map server. Check your internet connection." Tries the mirror, 3 rounds | 45 s timeout, then "took too long", no further rounds | 429 → tries the mirror, then "busy, wait a minute" | Bad JSON or a remark → friendly message | **Yes** (Retry button); the cached list is kept on refresh failure | none |
| Nominatim city search (`GeocodingService.SearchAsync`) | "Couldn't reach the city search…" | Timeout → "took too long. Try again." | Throttled to 1/s; 429 → "Too many requests…" | JSON error → friendly message | Message only; pressing Search again retries | **G7:** no Retry button (gym picker and profile) |
| Nominatim address search (custom gym form) | Same | Same | Same | Same | Message only | **G7** |
| Nominatim reverse lookup (`GymAddressLookupService`) | Stops and keeps progress; "Couldn't look up every address. The rest will be tried next time." | Same | Same | Same | None (tried again next open) | **G8 (minor):** no Retry |
| OSM map tiles (Mapsui in `GymPickerView`) | **Blank grey map, no message** | Tiles appear slowly | Not handled | Not handled | None | **G9:** a blank map with no explanation. The list still works offline |

No other network calls. Everything except gyms from OpenStreetMap already works offline: it's all local SQLite.

**Proposed fixes:** G7, a Retry button under the search message (re-runs the last query). G8, a Retry link next to the address note. G9, show "Map tiles need an internet connection. The gym list still works." when a tile request fails (Mapsui's tile-fetch error event) or when the last gym fetch failed for lack of connection.

---

## 3. Where an exception can reach the UI

There is **no** `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` or `Dispatcher.UIThread.UnhandledException` handler. The services only throw for unexpected database problems (locked, full disk, corrupt file, a bug), so these are rare, but each one either crashes the app or leaves a screen half-loaded with no message.

### 3a. Startup (crashes before any window opens)

| Where | What can throw |
|---|---|
| `App.OnFrameworkInitializationCompleted`: `Database.Migrate()` | Locked, corrupt or read-only database, failed migration |
| Same: `PointsService.BackfillAllAsync()` (blocking) | Any database error |

`RememberMeService.TryRestore` already catches everything.

### 3b. Async commands without `try` (exception rethrown on the UI thread, so the app crashes)

| View model | Commands |
|---|---|
| `LoginViewModel` | `Login` |
| `RegisterViewModel` | `Register` |
| `HomeViewModel` | `LogCalories`, `UseLastGym` |
| `ProfileViewModel` | `Save`, `LogWeight`, `SearchCity`, `ClearHomeCity`, `UseSuggestion`, `KeepGoal`, `SetCustomGoal` |
| `CustomExercisesViewModel` | `Save` |
| `DayViewModel` | `ToggleCopy` (and the add/remove/move/copy helpers it calls) |
| `ExerciseViewModel` | `Prefill`, `SavePr`, the planned-set +/− commands (`ChangePlanAsync`) |
| `FriendsViewModel` | `Search` |
| `GymPickerViewModel` | `Refresh`/`Retry`, `ClearGym`, `SearchAddress`, `SaveGymForm`, `SearchCity` |
| `SplitDetailViewModel` | `Use` |
| `SplitEditorViewModel` | `Save` |

### 3c. Fire-and-forget calls without `try` (fail silently; the screen stays stale)

| View model | Method | Effect |
|---|---|---|
| `GymPickerViewModel` | `ReloadListAsync` (every search keystroke), `OpenCityAsync` | List doesn't update |
| `ProfileViewModel` | `SaveAskForGymAsync`, `ChangeGoalTypeAsync`, `ReloadGoalAsync` | The toggle looks saved but isn't |
| `HomeViewModel` | `CheckedInAsync` (after check-in) | Home doesn't refresh after a successful check-in |
| `HomeViewModel` | `ChoseAsync` (after a missed-day choice) | Called inside the card's `try`, so a refresh failure says **"Could not save your choice."** even though the choice **was** saved. **G10** |
| `SplitsViewModel` | `LoadAsync`, `DeleteAsync` | Empty list / delete not shown |

Screens whose load path already catches and shows a message: Home refresh, check-in, Workouts, Leaderboard, Friends load, Points history, Gyms visited, Profile load, Custom exercises reload, Day screen reload.

### 3d. Raw exception text

None found. No `ex.Message` or exception `ToString()` reaches the UI.

### Proposed fixes for section 3

1. **Global handler** (`GymApp.Desktop/ErrorHandling.cs`):
   - Hook `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` (marked observed) and `Dispatcher.UIThread.UnhandledException` (marked handled).
   - Each writes a timestamped entry with the full exception to `errors.log` in `AppDbContext.DataDirectory`, capped at about 1 MB (rolled over once).
   - The user sees "Something went wrong. The details were saved to errors.log." in a banner on `MainViewModel`, dismissible, never the exception text.
2. **Startup:** wrap migrate and backfill. On failure, log it and show a simple window: "GymApp couldn't open its data. The details were saved to errors.log." Don't crash.
3. **3b and 3c:** add a `try`/`catch` with a specific message to each command or helper, like the existing ones ("Could not save your split."). The global handler is the safety net, not the plan.
4. **G10:** split saving from refreshing in the missed-day flow, so a refresh failure after a saved choice says "Your choice was saved, but Home couldn't refresh."

---

## 4. Tests planned (one per fixable gap in Core or a service)

- **G1:** username characters (`Usernames.Check`, plus `AuthService` refuses "a b").
- **G2:** password maximum length and all-spaces password.
- **G4, G5:** `NumberParsing.TryParseWhole`: "2100", "2,100", "2 100" and "2.100" accepted; "210.5", "2,1", "-5", "1,00" and "" rejected.
- **G6:** sets saved with more than 2 decimals come back rounded.
- **Error log:** writes an entry, rolls over past the cap, and never throws, even when the folder can't be written.

G3, G7–G10 and the `try` blocks are UI-only, so they're checked by running the app.

---

## Decisions for review

1. **G1:** limit new usernames to letters, digits and `_ . -`?
2. **G3:** do the per-field error approach (`Field` on result records), or keep one error line per form?
3. **G9:** show a note for the offline map, or leave the blank map as it is?
4. Anything above you'd rather drop from 1.0.0.
