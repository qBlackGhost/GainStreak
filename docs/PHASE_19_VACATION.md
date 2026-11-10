# Phase 19: Vacation mode and gyms where you are — spec for Claude Code

This is the spec for GymApp Phase 19 (version 0.19.0). A user away from their home city can put their streak on vacation: from today until the return date they pick (at most 7 days), the streak is frozen instead of breaking. Vacation can be used once every 2 months. While away, the app can show the gyms around them on the map, so a workout there is verified and still grows the streak. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase": `dotnet build` and the app tests after each sub-phase; the full `dotnet test`, both mobile heads (Android emulator and iOS Simulator, Release) and the desktop app at the end of the phase. After each sub-phase, report:
  - which files you created
  - which files you changed or moved
  - which migration you added, and to which database (all of this phase is the app's SQLite; the server doesn't change)
  - what you checked by hand on each platform, and what you couldn't check
- After any change to the app's entities, regenerate the compiled model (`dotnet ef dbcontext optimize`, see CLAUDE.md) and test the iOS Simulator with a **Release** build.
- Read `StreakCalculator` (`Replay`, `RestDayRule`, `Timeline`), `StreakService`, `PointsCalculator` (the week-complete bonus) and `PointsService`, `MissedDayRules` and `MissedDayService`, `ReminderPlanner` and `ReminderService`, `INotificationScheduler` and both head implementations, `ILocationProvider` and the head providers plus `BenchLocation`, `HomeViewModel` and `HomeView`, `ProfileViewModel`, `GymsViewModel` (`LoadAsync`, `CityGymsAsync`, the city picker), `CityService`, `GeocodingService`, `GymFetchService`, `GymProximity`, `docs/streak-grace-rule.md`, `docs/PHASE_18_LIVE_WORKOUT.md` (decision 6 and 18c) and `site/privacy.html` before changing anything.
- Every existing test must still pass, except where this spec changes a rule on purpose; update those tests and say which.

## Decisions already made (don't change these)

1. **A vacation lasts at most 7 days.** It starts today, or **tomorrow when today's workout is already logged** (so that workout keeps counting; on a frozen day an unverified workout doesn't add). The user picks a return date from the day after the start to 7 days after it. The frozen days are the start day up to the day before the return date (1 to 7 days). The return date is a normal day again.
2. **Once every 2 months.** A new vacation can start no earlier than 60 days after the start of the previous one. Ending a vacation early still uses up the allowance.
3. **Frozen means kept, not growing.** On a vacation day the streak neither grows nor breaks, unless a **verified** workout is logged that day, which adds +1 as usual. An unverified workout on vacation logs and earns its points (the 0.18 table) but doesn't add to the streak. This is the 0.18.1 rest-day rule applied to every vacation day, training day or not.
4. **No retroactive vacations.** A vacation can't cover a day before today, so it can't wipe a miss that already happened.
5. **No background location.** The app still never reads the position in the background or asks for the "Always" permission. "Away from home" is noticed when the user opens the app, not by a push notification the moment they arrive.
6. **The server doesn't change.** Vacations live only on the phone. The uploaded streak is simply the number the calculator gives. No `Contracts` change, no server migration, no `MinClientVersion` bump.
7. **The home city doesn't change.** Gyms where the user is travelling are shown as a visiting city; `UserProfile.HomeCityId` stays as it is.

## Defaults to confirm (change here if wrong)

- **"Away" means more than 40 km outside the home city's box** (`City.South/West/North/East`), measured from the nearest edge. No home city: never away (the manual start in Profile still works).
- **When the app checks:** one fix when Home appears after the app comes to the foreground, at most once every 6 hours, only on phones, only if location is already allowed and precise. This feature never asks for the permission itself (the first Start workout does, as in 0.18). No check while a vacation is open, while the allowance is used up, or before the user has a streak (count 0). The fix is compared on the phone and thrown away.
- **Asked once per trip.** After **Not on vacation**, the prompt isn't shown again until a fix has seen the user back within the home city's box (plus the 40 km margin).
- **A missed day waiting on a choice comes first.** With `NeedsChoice` or `AtRisk` the vacation can't start until today is settled: the prompt says "Log today's workout or choose what to do about yesterday first, then your vacation can start." `Shifted` (today logged, tomorrow owed) is allowed: the first frozen day closes the open debt and the owed day counts as Graced.
- **Ending early ("I'm back"):** today stays frozen and tomorrow is a normal day again (`EndedEarlyOn` = today). A vacation that hasn't started yet (it starts tomorrow) is cancelled instead: the row is deleted and the allowance isn't used.
- **The week-complete bonus:** a training day inside a vacation is neither done nor missed. The week bonus (+50 / +25) is judged on the week's other training days; a week whose training days are all frozen earns no bonus. (Otherwise a vacation would be a free +50.)
- **Reminders:** no training-day or missed-day reminders on frozen days, and no missed-day reminder on the first frozen morning (the vacation closes that miss). On the first normal day after a vacation, at 9:00, "Welcome back!" (`ReminderKind.WelcomeBack`, ids 3,000,000 and up). Like every reminder it needs Reminders on.
- **Nearby gyms send a rounded position to Nominatim.** To find out which city the user is in, **Show me gyms near me** sends the position **rounded to 2 decimals (about 1 km)** to Nominatim's reverse lookup at city level (`zoom=10`), only when the user taps it. This loosens 0.18's "location never leaves the phone" (Phase 18 decision 6): say so in the privacy page and store answers. If the lookup fails, or the user prefers, the city search opens instead ("Search for the city instead").
- **The visiting city stays on the map until the vacation ends**, with a banner "Visiting Rome · Back to Bucharest". **Back to Bucharest** shows the home city; the banner then offers "Show Rome". After the vacation the map goes back to the home city, and the visiting city's gyms stay cached like any picked city's.
- **Verification on vacation needs no new rule.** `GymProximity` already checks every active gym in the local database, so once the visiting city's gyms are fetched, a live workout near one of them is verified. The verified gym is the workout's gym as in 0.18.
- **Desktop:** no location, so no prompt and no nearby gyms. The manual start in Profile works the same.

---

## Design first

The vacation screens are on the **GainStreak Mobile** design canvas (claude.ai artifact "GainStreak Mobile", row **Vacation (Phase 19)**): **Home on vacation** (the away prompt, the return-date sheet, the streak on vacation, I'm back, welcome back; pick them with its Scenario tweak) and **Map · visiting a city** (gyms near you, finding them, location not found). They must be approved by the user before 19b starts. 19a has no UI and could start before. Where the artboards and this spec disagree, ask.

## 19a. Vacation rule and data (no UI yet)

**Data (app SQLite, one migration):**
- `Vacation`: `Id`, `UserId`, `StartDate`, `ReturnDate`, `EndedEarlyOn` (nullable), `CreatedAt`. At most one vacation per user whose frozen days include today (enforced in the service; a unique filtered index isn't possible on a date range).
- `UserProfile.VacationPromptDismissedAt` (`DateTime?`) and `UserProfile.LastAwayCheckAt` (`DateTime?`) for 19c. Add them here so there's one migration.

**Rules (`GymApp.Core/VacationRules`, pure and tested):**
- `MaxDays = 7`, `CooldownDays = 60`, `AwayMarginKm = 40`.
- `FrozenDays(vacation)`: `StartDate` up to the day before `ReturnDate`, or up to `EndedEarlyOn` when ended early.
- `CanStart(today, previous vacations, streak status)`: returns allowed, or why not (cooldown, with the first date it's allowed again; a missed day to settle first).
- `StartDate(today, loggedToday)`: today, or tomorrow when today is logged.
- `ReturnDateRange(start)`: the day after the start to start + 7.
- `Current(vacations, today)`: the vacation with a frozen day today or tomorrow (in progress, or starting tomorrow).
- `IsAway(fix, homeCity)`: distance from the fix to the nearest edge of the home city's box is more than 40 km (inside the box: 0). Haversine, like `GymProximity`.

**Streak (`StreakCalculator`):** the frozen dates ride on the rule the calculator already takes: `RestDayRule.Frozen` (an `IReadOnlySet<DateOnly>?`), loaded by `StreakService.LoadRestRuleAsync`, so every caller (points, reminders, stats, profile) gets them without a new parameter. For a frozen day `d`, before the existing rows:
- A debt still open is closed: the owed day is Graced.
- A verified workout on `d` (in `RestDayRule.Verified`): `count += 1`, as a verified rest day. With count 0 it starts a new streak.
- Otherwise nothing changes: no debt is created and nothing is added. On today this returns Active with `Stale = true` (the iced streak).
- `StreakResult` gets `VacationUntil` (`DateOnly?`, the return date) when today is frozen, so Home can say so.
- `DayOutcome` gets `Frozen` for a training day inside a vacation that wasn't trained (a verified workout there is Done). Missed-day choices are never asked for a frozen day.

**Points (`PointsCalculator`):** the week-complete bonus ignores Frozen days, as in Defaults. Workout points don't change.

**Service (`VacationService` in GymApp.Services):** `GetCurrentAsync`, `CanStartAsync`, `StartAsync(returnDate)`, `EndEarlyAsync`, `GetFrozenDatesAsync`. Starting or ending recalculates the streak (`StreakService.RecalculateAsync`) and goes through `ISocialSyncHooks` so the uploaded streak follows. `StreakService` and `PointsService` load the frozen dates next to the rest rule (`LoadRestRuleAsync`).

**Docs:** add a "Vacation (0.19.0)" note to `docs/streak-grace-rule.md` with the frozen-day rows above.

**Tests:**
- `VacationRules`: return date range, cooldown counted from the previous start (day 59 refused, day 60 allowed), ended early still counts, `IsAway` inside / just outside / beyond 40 km.
- `StreakCalculator`: frozen training and rest days keep the count; a verified workout on a frozen day adds +1, an unverified one doesn't; no debt and no NeedsChoice after a frozen day; a Shifted debt closed by the first frozen day; today frozen gives Active + Stale + `VacationUntil`; ended early unfreezes from the next day; with count 0 only a verified workout starts a streak; every existing test unchanged with no vacations.
- `Timeline` and the week bonus: Frozen days ignored; an all-frozen week earns nothing.
- `VacationService` on in-memory SQLite: start, refused during cooldown or with a missed day to settle, end early, streak recalculated.
- `CompiledModelTests`.

**Stop and report.**

## 19b. Starting and ending a vacation (all heads)

- **Profile → Streak** (next to Reminders): **Vacation**, with "Freeze your streak for up to 7 days, once every 2 months." Its state line: "Available", "On vacation until Tue 18 Nov", or "Next available on 12 Jan".
- **The vacation sheet:** "Going away?" title, the return date picker limited to `ReturnDateRange`, the line "Your streak stays frozen until you're back. Workouts at a gym still count." and **Start vacation**. Refused cases show `CanStart`'s reason instead of the button.
- **Home on vacation:** the streak card goes on ice (the 0.18.1 look) with ON VACATION · FROZEN · "Back on Monday, 19 Oct", and "Your streak is frozen until you're back. A workout at a gym still adds a day." Vacation days in This week get the rest-day moon on an ice tile. **Start workout** (phones) / **I went today** (desktop) stay; **Show me gyms near me** joins them as the main button in 19d (approved on the canvas). A small **I'm back** link (or **Cancel vacation** for one that starts tomorrow) asks "End your vacation? Today stays frozen, and your streak counts again from tomorrow. Your next vacation is available on …" **Keep it** / **End vacation**. It's hidden on the last vacation day and after ending one (nothing left to end).
- **The return date:** the first time Home shows on it, a one-time note: "Welcome back! Your streak counts again from today." (`Vacation.WelcomeShown`). Ending or cancelling a vacation shows "Vacation ended. Your streak counts again from tomorrow." or "Vacation cancelled." the same way.
- **Reminders:** `ReminderPlanner` skips frozen days and plans the return-day notification (Defaults). Replanned on start and end.
- **TestBench:** "Vacation" in the admin window: start one (with a return date), end it, clear the cooldown.

**Tests:** `ReminderPlanner` with a vacation (no reminders on frozen days, one welcome-back notification, replanned on end); Home's status line and the Profile state line for each state.

**Stop and report.** Check on desktop and one phone head: start, the iced card, end early, the cooldown line, the clock shifted to the return date.

## 19c. Noticing the user is away (phones)

- **The check** (Defaults): `AwayWatcher` (GymApp.App, singleton) runs when Home loads and when the app comes back to the front. `VacationService.AwayCheckDueAsync` says whether to look (home city, streak above 0, no vacation or cooldown, 6 hours since `LastAwayCheckAt`); then one fix (`GetFixAsync`, 10-second limit) goes to `RecordAwayFixAsync`, which records the look whether or not a fix came and says whether to ask. A fix worse than 200 m accuracy is fine here (40 km margin). A prompt once shown stays (in memory) until it's answered or the app restarts.
- **Back home:** a fix within the home city's box (plus margin) clears `VacationPromptDismissedAt`, so the next trip asks again.
- **The prompt**, a card at the top of Home (not a modal): "Looks like you're away from Bucharest. On vacation?" with **Yes, freeze my streak** (opens the vacation sheet) and **Not on vacation** (sets `VacationPromptDismissedAt`). When `CanStart` refuses with a missed day to settle, the card says what to do first; when it refuses for the cooldown, no card is shown at all.
- **The permission is never asked from here.** No permission, or approximate only: no check, no card.
- **TestBench:** `BenchLocation` gets **In another city** (central Rome). Its existing **Away** (the middle of the Atlantic) also counts as away. **Look for a trip now** forgets the last look so Home looks again at once.

**Tests:** the check's conditions (once every 6 hours, never during a vacation or cooldown, never without precise permission, never with count 0); asked once per trip and asked again after a fix back home; a fake provider drives all of it.

**Stop and report.** Check with the Simulator's and emulator's custom location: home city (no card), another city (card), Not on vacation then reopen (no card), back home then away again (card).

## 19d. Gyms where you are (phones)

- **`GeocodingService.ReverseCityAsync(lat, lon)`**: Nominatim reverse at `zoom=10`, coordinates rounded to 2 decimals before the URL is built (a test checks the URL), parsed into a `CityCandidate` (`Nominatim.ParseReverseCity`). Same one-request-per-second limit.
- **Show me gyms near me** (Home's streak card on vacation, phones, as the main button with Start workout under it): opens the map, where `NearbyCityFinder` (GymApp.App) takes one fix (asking for location only if it isn't allowed yet: the user tapped), looks the city up, `CityService.UpsertAsync`, keeps it as the vacation's visiting city (`Vacation.VisitingCityId`, so it lasts until the vacation ends, across restarts) and fetches its gyms (`GymFetchService.RefreshIfNeededAsync`). While it works: "Finding gyms near you…" with the 1 km line. No fix, or the lookup failed: "Couldn't find where you are." with **Try again** and **Search for the city instead** (the city picker, picking a visiting city, not the home city).
- **`GymsViewModel` visiting mode:** `_visitingCity` next to `_homeCity`. While set, `CityName`, `Bounds` and `CityGymsAsync` use it, and the banner shows (Defaults). The home gym, the friends' map and the month list stay the home city's. "Trained here today" and the gym picker work on the visiting city's gyms, so the user can mark where they trained. Visiting mode ends with the vacation (checked in `LoadAsync`).
- **Verification:** nothing new; check that a live workout at a gym of the visiting city is verified (`GymProximity` sees the newly cached gyms) and grows the frozen streak.

**Tests:** the reverse URL is rounded and at city level; `ParseReverseCity` on a city, a town, a village and an answer with no settlement; visiting mode in `GymsViewModel` (banner, back to home, ends with the vacation); a verified workout in the visiting city adds +1 during the vacation.

**Stop and report.** Check on both phone heads with a custom location in another city: Show me gyms near me, the map on that city, a live workout at one of its gyms verified, the streak +1, Back to home city.

## 19e. Privacy, store and release

- `site/privacy.html`: location is now also read once when the app is opened (at most every 6 hours, only if already allowed) to notice a trip, and the check stays on the phone; **Show me gyms near me** sends an approximate position (about 1 km) to OpenStreetMap's Nominatim to find the city, only when tapped.
- `docs/STORE_LISTING.md`: update the App Store privacy answers and the Google Play data safety form for the approximate location sent to Nominatim on the user's tap. Write what's needed and flag anything that needs a decision (whether Apple counts it as "collected" when the developer never receives it).
- iOS `NSLocationWhenInUseUsageDescription`: extend it to "GainStreak checks that you're at one of your gyms during a workout and notices when you're travelling. Your location stays on your phone unless you ask for gyms near you." Android's note before the prompt gets the same sentence.
- README: the TestBench vacation controls and "In another city".
- `CHANGELOG.md` 0.19.0 entry for testers: vacation mode (up to 7 days, once every 2 months, frozen not growing, verified workouts still count), the away prompt, gyms near you on the map, no reminders on vacation. End with `**Migration:** app database (vacations).`
- `Directory.Build.props`: 0.19.0.
