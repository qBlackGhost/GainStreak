# Phase 20: Apple Health — spec for Claude Code

This is the spec for GymApp Phase 20 (version 0.20.0). On iPhone, GainStreak connects to Apple Health when the user turns it on in Profile. It **writes** finished workouts, cardio sessions and weigh-ins to Health, and **reads** weigh-ins from other apps (a smart scale), the profile's height, birth date and sex, and daily steps to suggest the activity level. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase": `dotnet build` and the app tests after each sub-phase; the full `dotnet test`, both mobile heads (Android emulator and iOS Simulator, Release) and the desktop app at the end of the phase. 20a and 20b change the iOS head's own code, so they also build it and run it in the Simulator right away. After each sub-phase, report:
  - which files you created
  - which files you changed or moved
  - which migration you added, and to which database (all of this phase is the app's SQLite; the server doesn't change)
  - what you checked by hand on each platform, and what you couldn't check
- After any change to the app's entities, regenerate the compiled model (`dotnet ef dbcontext optimize`, see CLAUDE.md) and test the iOS Simulator with a **Release** build.
- Read `IPlatform`, `PhonePlatform`, `IosPlatform`, `ILiveActivity` and `IosLiveActivity` (the pattern this phase follows), `LiveWorkoutWatcher`, `LiveWorkoutService` (`FinishAsync`, `EndAsync`, the 4-hour stop), `CardioService`, `WorkoutService.UndoTodayAsync`, `ProfileService` (`SaveAsync`, `LogWeightAsync`, `DeleteWeightAsync`, `Estimate`), `BodyMetrics` (the activity levels and their step ranges), `ProfileViewModel`/`ProfileView`, `AppServices`, `site/privacy.html` and `docs/STORE_LISTING.md` before changing anything.
- Every existing test must still pass.

## Decisions already made (don't change these)

1. **iPhone only, and off until the user turns it on.** Profile gets an **Apple Health** row with a switch. Nothing asks for Health permission before the user turns it on. The desktop and Android show no row. Decide it through `IPlatform` (like `HasLiveWorkout`), never through the layout width.
2. **Health data stays on the phone.** Nothing read from Health is sent to the server, to OpenStreetMap or anywhere else, or written to `errors.log` (log the failure, never the values). The server doesn't change: no `Contracts` change, no server migration.
3. **Health never changes points or the streak.** Workouts from other apps don't count as GainStreak workouts or cardio, and nothing read from Health earns points. The friends leaderboard stays fair.
4. **Writes are what GainStreak itself recorded:** finished live workouts, cardio sessions and the user's own weigh-ins. Never anything read from Health (no loops), and no calories burned: the app has no heart rate, so any number would be invented.
5. **Reads never overwrite silently.** Weigh-ins from Health fill days the app has none for. Profile values from Health fill empty fields; a value that differs from one the user typed is offered, not applied. Steps only suggest an activity level.
6. **Follows the `ILiveActivity` pattern:** an `IHealthStore` interface in GymApp.App, created by `IPlatform.CreateHealthStore()`, a no-op for every head but iOS. Android's Health Connect can implement the same interface later.
7. **C# only.** HealthKit has an Objective-C API, and .NET for iOS binds it (the `HealthKit` namespace), so there's no Swift and no native build step this time. 20a confirms the bindings work under full AOT.

## Defaults to confirm (change here if wrong)

- **One switch for everything**, not one per data type. iOS's own permission sheet already lets the user allow or refuse each type, and the app must work with any mix (see "Refused permissions").
- **The setting is per GainStreak account** (`UserProfile`), but Health belongs to the phone's owner. Two accounts on one phone would write to the same Health; that's accepted, and the row says "Uses this iPhone's Health data".
- **When it's turned on**, the app asks for permission, then once: fills the profile (see 20c), suggests an activity level from steps, imports weigh-ins, and writes the **last 30 days** of the app's own workouts, cardio and weigh-ins so Health isn't empty. Older history isn't written.
- **Weigh-ins from Health:** today's and every day back to **90 days** before the switch was turned on, for days the app has none. iOS 27's permission sheet lets the user share only the past 30 days (20a results), so 90 days is a limit, not a promise; nothing tells the app which was picked. A day with a weigh-in typed in the app keeps it. Several Health weigh-ins in a day: the last one. This is the one place a past-dated `WeightLog` is written; it's body data, not logging, so `docs/streak-grace-rule.md` doesn't apply.
- **A strength workout in Health** is `TraditionalStrengthTraining`, from the live workout's `StartedAt` to its end (Finish, End with a log, or the 4-hour/midnight stop's time), written only when the workout counted (a `WorkoutLog` exists). Ended below half: nothing is written. The day name ("Push") goes in `HKMetadataKeyWorkoutBrandName`; 20b checks how the Health app shows it (20a couldn't).
- **A cardio session in Health** is `MixedCardio`, from `StartedAt` to `StartedAt` + the saved minutes, so its length matches the minutes that earned points. Pauses aren't stored, so they aren't shown. Under a minute (nothing saved): nothing is written.
- **Changes follow:** every object GainStreak writes carries a sync identifier (`HKMetadataKeySyncIdentifier`, e.g. `gainstreak-u3-weight-2026-11-09`, with the account so two accounts on one phone never touch each other's objects) and a version (`HKMetadataKeySyncVersion`, the change time's ticks), so Health replaces it on a change instead of adding a second one. Undo today's workout deletes its Health workout; deleting a weigh-in deletes its sample. The app only ever deletes objects it wrote.
- **Steps → activity level:** the average daily steps over the last 30 full days, with Health's own de-duplication (a statistics query, iPhone and Watch counted once). Fewer than 14 days with steps: no suggestion. Mapped with `BodyMetrics`' existing step ranges. Shown in Profile as "Your steps say Active (9,800 a day). Use it?" with **Use it** / **Not now**; asked again only when the suggested level changes.
- **When it reads again:** when the app comes to the front (at most once an hour) and when Profile opens. Weigh-ins use an anchored query, so only new samples are read each time.
- **Writes happen by reconciling** (decided in 20b): each time the app comes to the front or goes to the back, `HealthSync` builds what Health should hold for the last 30 days from the app's records, reads what this account already wrote there, and saves or deletes the difference. Nothing is remembered locally, so Undo, a deleted weigh-in and a failed write are all fixed by the next run, and no service or screen needs a hook. Failures are logged as a count, never with values.
- **No design artboard:** the Profile row, the suggestion line and the "differs" offer are built from Profile's existing rows and styles. Show the user screenshots in 20b and 20c.

## Refused permissions

iOS never tells an app whether read access was refused: a refused read looks exactly like "no data". So:
- Reads treat an empty result as "nothing to show". No error text ever says "permission refused" for reads.
- Writes do report refusal (`HKAuthorizationStatus.SharingDenied`). The row then says which types are off ("Workouts are off in Health's settings") with **Open Health settings**.
- Turning the switch off stops all reads and writes. What was already written stays in Health (the user can delete it there, and the row says so); weigh-ins already imported stay in the app.

---

## 20a. Spike: HealthKit on the Simulator, Release

Before building anything, confirm on the iOS Simulator with a **Release** build (full AOT, like 16a):
- The entitlement (`com.apple.developer.healthkit`) in `Entitlements.plist`, and `NSHealthShareUsageDescription` / `NSHealthUpdateUsageDescription` in `Info.plist`:
  - Share (read): "GainStreak reads your weight, height, birth date, sex and steps to fill in your profile and suggest your activity level. It stays on your phone."
  - Update (write): "GainStreak saves your workouts, cardio sessions and weigh-ins to Health."
- `HKHealthStore.IsHealthDataAvailable`, the permission sheet, saving one workout and one weight sample with a sync identifier, saving it again with a higher version (replaced, not doubled), deleting it by its sync identifier, reading steps with a statistics collection query, reading birth date and sex.
- Write the results into this spec under "20a results", like Phase 16's spike: what worked, any binding gaps, any AOT or trimming problem.

Throw away the spike code afterwards. **Stop and report.**

### 20a results (2026-11-09)

Checked on the iPhone 18 Pro Simulator (iOS 27.0), **Release** build (full AOT, `MtouchInterpreter -all`, `MtouchLink SdkOnly`), .NET for iOS 27.1, Xcode 27.2. The spike ran at launch behind an environment variable and wrote its results to a file; it has been removed.

**Everything worked under full AOT, with no binding gaps that matter and no trimming problems:**
- `HKHealthStore.IsHealthDataAvailable`: true on the Simulator.
- **Permission sheet:** shows both usage strings. "Select All" plus **Continue**, then a second screen new in iOS 27: **"How much data would you like to share?"** with *Past 30 Days and Future Data* or *All Recorded Data and Future Data*. Picked 30 days. `RequestAuthorizationToShareAsync` then returned `true`, and `GetAuthorizationStatus` gave `SharingAuthorized` for weight and workouts.
- **Weight with a sync identifier:** saved v1 (80.5 kg) and then v2 (81.2 kg) with the same `HKMetadataKey.SyncIdentifier` and a higher `SyncVersion`. A query by the identifier found **one** sample, 81.2 kg: Health replaced it, as the Defaults rely on.
- **Workouts, both APIs:** `HKWorkout.Create(…, NSDictionary metadata)` + `SaveObjectAsync` saved a 60-minute `TraditionalStrengthTraining`; `HKWorkoutBuilder` (`BeginCollectionAsync`, `AddAsync(new HKMetadata(dict))`, `EndCollectionAsync`, `FinishWorkoutAsync`) saved a 25-minute `MixedCardio`. Both came back from a sample query with their sync identifiers. **Use `HKWorkoutBuilder` in 20b**: `HKWorkout.Create` is deprecated.
- **Delete by sync identifier:** `DeleteObjects(type, HKQuery.GetPredicateForMetadataKey(SyncIdentifier, [id]), callback)` deleted exactly 1.
- **Steps:** 20 days written (spike only), then `HKStatisticsCollectionQuery` with `CumulativeSum` and a one-day interval over 30 days: 20 days, average 9,050.
- **Anchored query:** `HKAnchoredObjectQuery` on weight returned the sample and an anchor; `NSKeyedArchiver.GetArchivedData(anchor, true, …)` gave 414 bytes, and `NSKeyedUnarchiver.GetUnarchivedObject(typeof(HKQueryAnchor), …)` read it back. Fine for `UserProfile.HealthWeightAnchor`.
- **Birth date and sex:** `GetDateOfBirthComponents` returns null and `GetBiologicalSex` returns `NotSet` when the Health profile has none, without an error. Not checked with values set (the Simulator's Health app needed its first-run setup).

**Binding notes for 20b** (names differ from Swift and older Xamarin docs):
- The workout type is `HKObjectType.WorkoutType` (a property), not `GetWorkoutType()`.
- Builder metadata is `AddAsync(HKMetadata)`; `FinishWorkoutAsync()` returns `(bool, NSError)` here, not the workout. Read a saved object's metadata through `Metadata.Dictionary`.
- Deleting by predicate has no `Async` overload; wrap the callback in a `TaskCompletionSource`. `SaveObjectsAsync` returns `(bool, NSError)`.
- `HKQuantitySample.FromType(…, null)` is ambiguous: cast the metadata `(NSDictionary?)null`.

**Two setup problems found (fix both in 20b):**
1. **`Missing application-identifier entitlement` on the Simulator.** A Simulator build is signed ad hoc, and .NET doesn't add `application-identifier` to its entitlements, so HealthKit refuses everything. Add `application-identifier` = `$(AppIdentifierPrefix)$(CFBundleIdentifier)` to `Entitlements.plist` (`com.gainstreak.app` on the Simulator; device builds get it from the profile anyway). The Live Activity extension doesn't need it.
2. **Incremental Release builds keep stale copies.** After `Info.plist` changed, `_CompileAppManifest` kept the old `obj/…/AppManifest.plist` (the bundle had no Health strings, and HealthKit threw `NSHealthUpdateUsageDescription must be set`). After `Entitlements.plist` changed, the native link was skipped, so the binary kept the old entitlements. Delete `obj/<config>/net10.0-ios/<rid>/AppManifest.plist` and `…/nativelibraries/GymApp.iOS` (or build clean) after changing either file, and check with `plutil -p` on the bundle's `Info.plist`.

**Also seen:** `HKHealthStore` calls from a thread-pool thread were fine, and the permission sheet still appeared.

**Still open (check in 20b):** how the Health app shows `WorkoutBrandName` (the day name), and the Health app's view of the workouts at all.

## 20b. The switch and writing to Health

20b is in two parts, because 0.19.0 (vacation) was being built in the same checkout and changes the same database and Profile files. **Part 1 is done** (2026-11-09); **part 2 waits until 0.19.0 is committed.**

### Part 1 (done): writing, without the database or Profile

- **`HealthPlan` (GymApp.Core, pure):** `HealthItem`, `HealthKind`, the sync ids (`gainstreak-u<user>-workout-<date>`, `…-cardio-<date>-<session id>`, `…-weight-<date>`) and `Diff`, the reconcile rule: new or changed objects are saved at a version above both the stored one and now's ticks; objects the app no longer has are deleted; other accounts' and other apps' objects are never touched. Times compare to the second (Health's precision). Tests: `HealthPlanTests`.
- **`HealthExportService` (GymApp.Services, read-only):** what Health should hold for a window: a strength workout for each logged day whose live session ended (its start to end, the day name or "Extra workout"), a cardio workout per finished session that saved minutes (as long as the saved minutes), each weigh-in at 08:00 (the app keeps only the day).
- **`IHealthStore` / `NoHealthStore` (GymApp.App):** `IsSupported`, `RequestAuthorizationAsync`, `WriteAccess(kind)`, `ReadOwnAsync(from, to)`, `SaveAsync(item)`, `DeleteAsync(item)`. `IPlatform.CreateHealthStore()`, a no-op except on iOS.
- **`HealthSync` (GymApp.App, singleton):** the reconcile above, for the signed-in account, only for kinds the user lets the app write; one run at a time; started from `GymApplication`'s foreground and background events. Tests: `HealthSyncTests` (a fake store over the test database).
- **`IHealthSetting`:** whether the account turned Health on. For now `HealthNotYetAvailable` says no to everyone, so **nothing reaches Health in any build yet**.
- **`IosHealthStore` (iOS head):** `HKWorkoutBuilder` for workouts (the day name in `WorkoutBrandName`), body mass samples for weigh-ins, reads limited to this app's objects (`HKSource.GetDefaultSource`), deletes by sync id. Checked on the Simulator with a throwaway round trip (since removed): a strength workout, a cardio workout and a weigh-in saved, read back identical (times, title, value), a second reconcile found nothing to do, a changed weigh-in replaced the old one, and all three deleted.
- **The build switch `GymAppHealthKit`** (GymApp.iOS.csproj), **off by default**: `-p:GymAppHealthKit=true` builds with `Entitlements.Health.plist` (HealthKit + `application-identifier`, the 20a fix), merges `HealthUsage.plist` (the two permission texts) into Info.plist, and compiles `IosHealthStore`. Off, because a signed device build with the HealthKit entitlement fails until the App ID has the capability: 0.19.0 must still ship from this tree. 20d turns it on for good and folds `Entitlements.Health.plist` into `Entitlements.plist`.

### Part 2 (done 2026-11-10): the database and the switch

**Data (app SQLite, one migration `AppleHealth`):**
- `UserProfile.HealthEnabled` (`bool`, false) and `UserProfile.HealthEnabledAt` (`DateTime?`).
- `UserProfile.HealthStepsSuggestionSeen` (`ActivityLevel?`): the last suggested level the user answered.
- `UserProfile.HealthWeightAnchor` (`byte[]?`): the anchored query's anchor (20c).
- `WeightLog.FromHealth` (`bool`, false): imported weigh-ins. **`HealthExportService` must then skip them** (decision 4; add a test).

**Then:**
- Replace `HealthNotYetAvailable` with an `IHealthSetting` over `UserProfile.HealthEnabled` (in `ProfileService`).
- **Profile (iPhone only, `HealthStore.IsSupported`):** an **Apple Health** row with a switch, "Saves your workouts and weigh-ins, reads your weight, steps and profile." Turning it on asks for permission, saves `HealthEnabledAt`, then starts a sync (the first one writes the last 30 days). The refused-writes line and **Open Health settings** (Refused permissions). Turning it off stops syncing; the row says what was written stays in Health.
- Start a sync right after a weigh-in is logged or deleted in Profile, so it doesn't wait for the app to go to the back.
- **Tests:** the setting on and off, the first sync, `FromHealth` weigh-ins never written, `CompiledModelTests`.
- **Check** on the Simulator with `-p:GymAppHealthKit=true`: the Profile row, and a finished live workout, a cardio session and a weigh-in in the Health app, including how the day name (`WorkoutBrandName`) shows there.

**Done as written**, plus: the weigh-in sync starts from `ProfileViewModel` after Log and Delete; the card is `HealthViewModel` (like `RemindersViewModel`), shown when `IHealthStore.IsSupported`; refused kinds are named ("Weigh-ins are off for GainStreak in Health, so they aren't saved there.") with **Open Settings** (`IHealthStore.OpenSettings`, the app's Settings page); turning it off says what stays in Health. Version 0.20.0 (with the Dynamic Island); the changelog only names the migration, since Health stays hidden until 20d.

**Checked** on the iPhone 18 Pro Simulator, Debug with `-p:GymAppHealthKit=true`, signed in to the local server: the Apple Health card under Reminders; turning it on (no sheet: access was already given in 20a) saved the setting and ran a sync (reads only, nothing to write yet); logging a weigh-in in Profile then saved it to Health at once (HealthKit's log: "Saving 1 objects"); no errors. Release (full AOT) with HealthKit on builds and starts. All app tests pass.

**Not checked:** the permission sheet on a fresh install (seen in 20a), a refused kind on the card, a finished live workout and a cardio session reaching Health through the app (covered by `HealthSyncTests` and 20b part 1's round trip), and how the Health app shows the day name: the Simulator's Health app needs its first-run setup, which is slow to drive. Do these by hand, or on a real iPhone once 20d is out.

## 20c. Reading from Health

- **Profile fill (`ProfileService`):** height, birth date and sex from Health fill empty fields. A different value from Health shows under the field: "Health says 1.82 m. Use it?" **Use it** / **Keep mine**. Sex maps only Female and Male; anything else is left to the user.
- **Weigh-ins:** the anchored query imports new samples into `WeightLog` with `FromHealth = true` (Defaults: the 90 days, days the app has none, the last of a day). Samples GainStreak wrote itself are skipped (their sync identifier starts with `gainstreak-`). The weight chart and calorie estimate use them like any weigh-in; deleting one deletes it only in the app.
- **Steps → activity level:** the suggestion line in Profile (Defaults). The rule is pure (`BodyMetrics.LevelForSteps(averageSteps)`), tested at both range edges.

**Tests:** fill only empty fields, offer on a difference, the weigh-in import rules (own samples skipped, typed day kept, last of a day, the 90-day limit, anchor stored), steps edges and the 14-day minimum, nothing read with the switch off.

**Stop and report.** Check in the Simulator with data added by hand in the Health app (weight, height, steps on several days).

### 20c as built (2026-11-10)

- **Rules (`GymApp.Core/HealthImport`, pure, `HealthImportTests`):** `WeighInsToImport` (the day's last, 90 days back from when Health was turned on, never a typed day, never any GainStreak sample, only weights the app accepts), `AverageSteps` (null under 14 days with steps), `LevelForSteps` (the picker's bands; it lives here, not in `BodyMetrics`), `StepsSuggestion`, `Compare` (what fills empty fields and what's offered; heights within 1 cm count as the same) and `OfferKey`.
- **`HealthImportService` (GymApp.Services):** imports weigh-ins and stores the anchor, fills the profile and returns the offer, answers the offer and the steps suggestion.
- **`IHealthStore` reads:** `ReadProfileAsync`, `ReadWeightsAsync(from, anchor)` (an anchored query), `ReadDailyStepsAsync`. The permission request now also asks to read height, steps, birth date and sex.
- **`HealthSync.ReadAsync(force)`:** after each sync when the app comes and goes, at most once an hour; Profile asks at once.
- **Changed from the spec:** the offers sit **in the Apple Health card**, not under each field ("Health says: height 182 cm." **Use Health's** / **Keep mine**; "Your steps say Active: 9,800 a day over the last 30 days." **Use it** / **Not now**). It works the same in Profile's phone and wide layouts, which repeat the body data fields. Filled fields and imported weigh-ins reload Profile, keeping any unsaved edits. "Keep mine" is remembered for exactly those values in a new column, `UserProfile.HealthProfileDismissed`; the `AppleHealth` migration, not yet shipped, was made again with it.
- **Fixed on the way:** typing a weigh-in on a day imported from Health makes it the user's (`FromHealth = false`), so it goes to Health; before, it would have been kept out for good.
- **Tests:** `HealthImportTests`, and the read cases in `HealthSyncTests` (import and anchor, typed days, typing over an import, fill and offer and Keep mine, steps once until they change, at most hourly, nothing when off, the card's texts and answers).
- **Checked on the Simulator** (iPhone 18 Pro, Debug with `-p:GymAppHealthKit=true`, a clean install): turning the switch on shows iOS's sheet with 8 topics (the new reads) and then the 30 days / all data question; after Allow, the card showed "Your steps say Active: 9,050 a day over the last 30 days." (20a's 20 days of steps); **Use it** set Activity to Active in Body data and the line went away. The weigh-in in Health (20a's, with a GainStreak sync id) wasn't imported, as it shouldn't be. **Not checked by hand:** the profile offer (the Simulator's Health has no height, birth date or sex) and a weigh-in from another app; both are covered by `HealthSyncTests`. **Dev only:** a Simulator that applied 20b's first `AppleHealth` migration fails to start with "duplicate column name: FromHealth"; reinstall the app. No shipped build had it.

## 20d. Privacy, store and release

- `site/privacy.html`: what is read and written, that Health data stays on the phone, isn't used for ads and isn't sent to the server.
- `docs/STORE_LISTING.md`: the App Store privacy answers (Health & Fitness data: not collected, since it doesn't leave the device), the App Review note on how HealthKit is used (App Review requires HealthKit to be clearly shown in the app and a privacy policy), and the Health capability on the App ID.
- `build/release-mobile.sh` and README "Releases for testers": the App ID `com.gainstreak.app` needs the **HealthKit** capability, so its App Store profile must be made again and downloaded. Flag this; the user does it on developer.apple.com.
- `CHANGELOG.md` 0.20.0 entry for testers: what's written, what's read, that it's off until turned on, that nothing from Health earns points. End with `**Migration:** app database (Apple Health setting, weigh-ins from Health).`
- No `MinClientVersion` change: the server doesn't change.

**Stop and report**, with what must be checked on a real iPhone (permissions sheet from TestFlight, a real scale's weigh-ins, steps from an Apple Watch counted once).

### 20d as built (2026-11-10)

- **HealthKit is on in every iOS build:** the `GymAppHealthKit` switch is gone; the HealthKit entitlements and `application-identifier` are in `Entitlements.plist`, the two permission texts in `Info.plist`, and `IosHealthStore` is always compiled. **Signed device builds need the App ID `com.gainstreak.app` to have the HealthKit capability and a profile made after it** (README "Releases for testers" step 2, `build/release-mobile.sh`, `docs/STORE_LISTING.md`'s checklist). The user does that on developer.apple.com.
- `site/privacy.html`: an "Apple Health (iPhone)" section, the summary and "What isn't collected". `docs/STORE_LISTING.md`: Health & Fitness not collected, the HealthKit rules, the App Review note, the capability. Publish `site/` before the build reaches testers.
- `CHANGELOG.md` 0.20.0: the Apple Health entry, and the migration line. No `MinClientVersion` change.
- End-of-phase checks: see the report.

**Check on a real iPhone** (TestFlight): the permission sheet and the 30 days / all data choice; a weigh-in from a real scale (Withings, Renpho…) imported once and the day's last kept; steps from an iPhone with an Apple Watch counted once (compare with the Health app's daily total); a finished workout and a cardio session in the Health app, and how the day name shows there; turning Health off and on again (no duplicates in Health); the Live Activity next to it (Dynamic Island while Health saves in the background).

## Later (not this phase)

- **Android Health Connect** behind the same `IHealthStore`.
- **Calories:** write the daily total (`dietaryEnergyConsumed`) and import it from diet apps.
- **Heart rate** from an Apple Watch during a live workout, shown on the workout summary.
- **Workouts from other apps as cardio:** needs a points decision first (decision 3).
- Sleep and resting heart rate for recovery hints.
