# Phase 23: Health Connect on Android — spec for Claude Code

This is the spec for GymApp Phase 23 (version 0.23.0). Phase 20 linked the iPhone to Apple Health behind `IHealthStore`; this phase gives Android phones the same through **Health Connect**: when the user turns it on in Profile, GainStreak **writes** finished workouts, cardio sessions and weigh-ins, and **reads** weigh-ins from other apps, the height and daily steps. Same switch, same rules, same `HealthSync`; only the head's store and a few texts are new. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase". 23a, 23b and 23c change the Android head's own code, so they also build it and run it on the emulator right away. After each sub-phase, report: files created, files changed, any migration (none is expected: Phase 20's columns are reused), what you checked by hand on each platform and what you couldn't.
- Read Phase 20's spec (`docs/PHASE_20_APPLE_HEALTH.md`, the decisions and the "as built" notes), `IHealthStore`, `IosHealthStore`, `HealthPlan`, `HealthSync`, `HealthExportService`, `HealthImportService`, `HealthViewModel` and Profile's Apple Health card, `AndroidPlatform`, `MainActivity` (how permissions are asked today), `site/privacy.html` and `docs/STORE_LISTING.md` before changing anything.
- The iPhone must behave exactly as in 0.20.x–0.22.x: a shared change (texts, the card) keeps iOS's words.
- Every existing test must still pass.

## Decisions already made (don't change these)

1. **Phase 20's decisions 1 to 5 hold on Android too:** off until the user turns it on, Health data stays on the phone (never sent to the server, never logged with values), it never changes points or the streak, writes are only what GainStreak recorded, reads never overwrite silently.
2. **Behind the same `IHealthStore`:** a new `AndroidHealthStore` in `GymApp.Android`, created by `AndroidPlatform.CreateHealthStore()`. `HealthPlan`, `HealthSync`, the export and import services and the database stay as they are; change them only where Health Connect really differs (below), and keep the iOS path identical.
3. **The same Profile card,** named by the platform: "Apple Health" on the iPhone, "Health Connect" on Android. Its texts come from the store (or `IPlatform`), never from the layout or `OperatingSystem` checks in shared code.
4. **C# only,** through the `Xamarin.AndroidX.Health.Connect.ConnectClient` binding (1.1.0.x). No Kotlin and no AAR of our own unless 23a proves the binding can't be used; then stop and ask.
5. **The server doesn't change:** no `Contracts` change, no server migration, no `MinClientVersion` change.

## Where Health Connect differs from HealthKit

- **It may not be installed.** Android 14 and later have it built in; Android 8 to 13 need the Health Connect app from Play. `HealthConnectClient.getSdkStatus`: *available* → the card works; *provider update required* → the card shows and turning it on opens Health Connect's Play page instead; *unavailable* → no card (`IsSupported` false).
- **Permissions are asked through a contract,** `PermissionController.createRequestPermissionResultContract()`, not a runtime permission. Each one is declared in the manifest (`android.permission.health.READ_WEIGHT`, `WRITE_WEIGHT`, `WRITE_EXERCISE`, `READ_EXERCISE`, `READ_HEIGHT`, `READ_STEPS`), and the app needs the **permissions rationale** activity (`ACTION_SHOW_PERMISSIONS_RATIONALE`, and on Android 14+ an activity-alias for `VIEW_PERMISSION_USAGE` with `HEALTH_PERMISSIONS`) that shows the privacy policy, or Health Connect won't show the app's sheet at all.
- **Reads are known:** `getGrantedPermissions` says what was refused, reads included. Use it for `WriteAccess` as on iOS; the card may stay as quiet as on iOS about reads (don't add new texts for it in this phase).
- **Sync ids map one to one:** `Metadata.clientRecordId` is `HealthItem.SyncId` and `clientRecordVersion` is `HealthItem.Version`; inserting a record with the same `clientRecordId` and a higher version replaces it (an upsert). Delete by client record id. "Our own records" are those whose data origin is this package and whose id starts with `HealthPlan.Prefix`.
- **Workouts** are `ExerciseSessionRecord`: `EXERCISE_TYPE_STRENGTH_TRAINING` for a strength workout, and for cardio the closest single type (23a picks one; Health Connect has no "mixed cardio", candidates are `OTHER_WORKOUT` or `EXERCISE_TYPE_…` for the cardio kinds the app knows). The day name goes in `title`.
- **No birth date and no sex** in Health Connect. `ReadProfileAsync` returns the latest height only; the offer text already handles missing fields.
- **Weigh-in anchor:** a changes token (`getChangesToken` for `WeightRecord`, then `getChanges`) stored as UTF-8 bytes in `UserProfile.HealthWeightAnchor`. Tokens expire after about 30 days (`changesTokenExpired`): then read from the import start again, as with no anchor. Health Connect only lets an app read **30 days before the permission was granted** without `READ_HEALTH_DATA_HISTORY`; that's enough (Phase 20 already treats 90 days as a limit, not a promise). Don't ask for history.
- **Steps** through `aggregateGroupByPeriod` with `StepsRecord.COUNT_TOTAL` and a one-day period: Health Connect de-duplicates phone and watch itself.
- **Background:** Health Connect refuses reads from an app in the background (without `READ_HEALTH_DATA_IN_BACKGROUND`, which this phase doesn't ask for). `HealthSync` reads what it wrote before writing, so the run started by going to the background will fail on Android. 23a measures what happens; the default (below) is to run on Android only when the app comes to the front, when Profile opens, and right after a finish, cardio save or weigh-in while still on screen.
- **Suspend functions.** The client's API is Kotlin `suspend` functions; from C# each one takes an `IContinuation`. 23a writes one small helper that turns a call into a `Task<T>` (resuming on any thread, unwrapping Kotlin's `Result` failure into an exception) and every store call goes through it.

## Defaults to confirm (change here if wrong)

- **The card's texts on Android:** title "Health Connect"; line "Saves your workouts, cardio and weigh-ins to Health Connect, and reads your weight, height and steps from it."; Phase 20's other texts with "Apple Health" / "Health" / "iPhone" swapped for "Health Connect" / "phone". "Open Settings" opens Health Connect's own settings for the app (`HealthConnectClient.getHealthConnectManageDataIntent` or the `ACTION_HEALTH_CONNECT_SETTINGS` intent).
- **When it syncs on Android:** the same as on iOS (decided in 23b, after 23a's background check): coming to the front, going to the back, opening Profile, and a logged or deleted weigh-in. A run Health Connect refuses in the background is logged as a count and fixed by the next one.
- **Cardio's exercise type:** `OTHER_WORKOUT` unless 23a shows something reads better in the Health Connect app and in Google Fit / Fitbit.
- **Old Android (8 to 13) without Health Connect:** the card shows, and turning it on opens Health Connect's Play page with a line saying so; nothing is saved until it's installed. The switch goes back off.

---

## 23a. Spike: the binding on the emulator, Release

Before building anything, confirm on an Android emulator with Play (API 35 or later, so Health Connect is built in) with a **Release** build (trimmed, like 16a):
- The package restores and links, and the suspend helper works: `getSdkStatus`, `getGrantedPermissions`.
- The manifest permissions and the rationale activity / activity-alias; the permission sheet opens from `MainActivity` through the contract (`createIntent` + `StartActivityForResult` + `parseResult`, or an `ActivityResultLauncher` if `AvaloniaMainActivity` allows it) and returns what was granted.
- Insert one `WeightRecord` and one `ExerciseSessionRecord` with a `clientRecordId` and version; insert again with a higher version (replaced, not doubled); read them back filtered to our data origin; delete them by client record id.
- Steps: insert a few days (spike only) and aggregate them by day. Height: insert one, read the latest. Changes token for weight: get one, add a weigh-in, read the change.
- What happens to a read and a write started right after the app goes to the background.
- Write the results under "23a results": what worked, binding gaps, trimming problems (Release may strip classes the binding reaches by reflection: note any `TrimmerRootAssembly` / `Preserve` needed).

Throw away the spike code afterwards. **Stop and report.**

### 23a results (2026-11-17)

Checked on the `Pixel` emulator (Android 16, API 36, Google APIs, no Play Store; Health Connect is part of the system there), **Release** build (trimmed), `Xamarin.AndroidX.Health.Connect.ConnectClient` 1.1.0.4 (Kotlin stdlib 2.4.0.1). The spike ran from an intent extra and logged to logcat; it has been removed. **Kept:** the package reference, the manifest's six permissions and `<queries>`, the `ViewPermissionUsageActivity` alias and `HealthPermissionsRationaleActivity`.

**Everything worked in Release, with no binding gaps and no trimming roots needed:**
- **Suspend helper:** an `IContinuation` (`Context` = `EmptyCoroutineContext.Instance`) whose `ResumeWith` completes a `TaskCompletionSource`; compare the call's return value with `IntrinsicsKt.COROUTINE_SUSPENDED` (an immediate result is returned straight away). A failure arrives as a `kotlin.Result$Failure` object, which isn't bound: read its private `exception` field through Java reflection and throw it. Return values need `JavaCast<T>()` (lists come back as `Java.Util.IList`, sets as `Java.Util.ICollection`).
- `GetSdkStatus` gave `SdkAvailable` (3). `PermissionController.GetGrantedPermissions` returns the granted set, reads included.
- **Permission sheet:** `PermissionController.CreateRequestPermissionResultContract()` with **`RegisterForActivityResult` in `MainActivity.OnCreate`** (`AvaloniaMainActivity` is an androidx `ComponentActivity`) and `launcher.Launch(set)`. **`contract.CreateIntent` + `StartActivityForResult` fails on Android 14+** (`ActivityNotFoundException` for `androidx.activity.result.contract.action.REQUEST_PERMISSIONS`: there it's a runtime permission request only the registry can handle). The first time, Health Connect shows its own "Get started" page, then the app's sheet: per type switches, "Allow all", the 30 days line and a **privacy policy** link (shown because of the rationale activity). A second request asks only for permissions not yet granted. The callback gets the granted set.
- **Upsert:** a `WeightRecord` with `Metadata.ManualEntry(clientRecordId, version)` saved at v1 (80.5 kg) and v2 (81.2 kg): one record, 81.2 kg, v2. An `ExerciseSessionRecord` the same (title "Push" → "Push day"). Types: `ExerciseSessionRecord.ExerciseTypeStrengthTraining` (70), `ExerciseTypeOtherWorkout` (0).
- **Reading our own:** `ReadRecordsRequest(kclass, range, [DataOrigin(packageName)], …)` returns only this app's records, **including ones without our prefix** (a test record with client id `spike-scale` came back), so keep the `HealthPlan.Prefix` filter. A record type is a `KClass`: `JvmClassMappingKt.GetKotlinClass(Java.Lang.Class.FromType(typeof(WeightRecord)))`.
- **A null title reads back as `""`**: map it to null in `ReadOwnAsync`, or `HealthItem.SameAs` will see every cardio session as changed and rewrite it on every run.
- **Delete by client id:** `DeleteRecords(kclass, [], clientIds)` deleted them; a read afterwards found none.
- **Changes token:** `GetChangesToken(ChangesTokenRequest([WeightRecord], []))`, then a new weigh-in, then `GetChanges(token)`: one `UpsertionChange`, not expired, a next token. The token is a short string (1 character on a fresh Health Connect), fine as UTF-8 bytes in `HealthWeightAnchor`.
- **Height:** `ReadRecordsRequest(HeightRecord, Before(now), [], ascending: false, pageSize: 1)` gave the latest, 182 cm.
- **Steps:** `AggregateGroupByPeriod` with `StepsRecord.CountTotal`, `TimeRangeFilter.Between(LocalDateTime, LocalDateTime)` and `Period.OfDays(1)`: one group per day, **days without steps included with no value** (map them to 0, as iOS does).
- **Background:** a read, a write and a delete started **2 s after pressing Home all worked** on API 36 (Google's docs say reads need the foreground; the process apparently still counts as in front for a moment). So the "going to the background" run may work on Android as on iOS. 23b decides: keep iOS's triggers on Android and check `errors.log` on a real phone, or the Defaults' foreground-only triggers.
- **Java time:** `ZonedDateTime.ToInstant()`/`ToEpochSecond()` aren't bound; build `Instant`s from .NET (`Instant.OfEpochMilli(DateTimeOffset.ToUnixTimeMilliseconds())`). Each record wants a `ZoneOffset`: `ZoneId.SystemDefault().Rules.GetOffset(instant)`.
- **Dev only:** an incremental Release build after a manifest-only change produced an APK that crashed at start (`UnsatisfiedLinkError` in `AvaloniaAndroidApplication`); a clean Release build fixed it.

**Not checked:** Android 8 to 13 with the Health Connect app (no such emulator here), the rationale page from a real "privacy policy" tap with the policy published, and how the Health Connect app shows a session's title.

## 23b. The store and writing

- `AndroidHealthStore : IHealthStore`: `IsSupported`, `RequestAuthorizationAsync` (through `MainActivity`, like the location permission), `WriteAccess`, `ReadOwnAsync`, `SaveAsync`, `DeleteAsync`, `OpenSettings`. Times: the app's local times to `Instant` plus the zone offset Health Connect asks for.
- The card's name and texts from the platform (decision 3), with iOS's words unchanged; the "install Health Connect" case.
- The Android sync triggers (Defaults), without changing iOS's.
- Tests: the texts per platform and the triggers on a fake store in `HealthSyncTests` / a new test class.
- On the emulator: turn it on, finish a workout, log cardio and a weigh-in, see them in the Health Connect app; Undo and a deleted weigh-in disappear on the next run; turning it off and on again makes no duplicates.

**Stop and report**, with screenshots of the card.

### 23b as built (2026-11-17)

- **`AndroidHealthStore`** (`GymApp.Android`), created by `AndroidPlatform.CreateHealthStore()`. Calls go through **`KotlinSuspend`** (23a's helper). Writes: exercise sessions (`ExerciseTypeStrengthTraining`, cardio as `ExerciseTypeOtherWorkout`) with the day name as title, and weight records, each with `Metadata.ManualEntry(syncId, version)`, so inserting again replaces. Deletes go by client record id. `ReadOwnAsync` reads both types filtered to this package (every page), keeps only ids with `HealthPlan.Prefix`, and maps an empty title to null. Reads from Health (23c) return nothing yet.
- **Permissions:** `MainActivity` registers the contract's launcher in `OnCreate`; `MainActivity.RequestHealthPermissionsAsync` launches it. **Asked once, as on iOS:** Android shows the sheet again for anything refused, so the store remembers what it asked for (`SharedPreferences` `health_connect`, key `asked`) and only asks for permissions never asked before. Otherwise, opening Profile would bring the sheet back after every refusal. `WriteAccess` comes from the granted set, refreshed by every request and every sync: granted is `Allowed`, otherwise `Denied`, and `NotAsked` before the first look.
- **The card's words come from the store:** `IHealthStore.Texts` (`HealthTexts.AppleHealth` by default, `HealthTexts.HealthConnect` on Android) gives the title, the description, where the data is ("Health" / "Health Connect") and where it's managed ("the Health app" / "Health Connect"). `HealthViewModel` builds every text from it, including "Use Health's" / "Use Health Connect's". iOS's texts are unchanged, word for word (a test checks it).
- **Not installed** (Android 8 to 13 without Health Connect): `IHealthStore.NeedsInstall` and `OpenInstallPage()` are new default members, false and a no-op on iOS. Turning the switch on then opens Health Connect on Google Play and keeps the switch off, with the line "Health Connect isn't on this phone yet. Install it from Google Play, then turn this on again." `HealthSync` does nothing while `NeedsInstall` is true.
- **"Open Settings"** opens Health Connect's home (`android.health.connect.action.HEALTH_HOME_SETTINGS` on Android 14+, `androidx.health.ACTION_HEALTH_CONNECT_SETTINGS` before). Its "App permissions" list has GainStreak. **Changed from the plan:** an app can't open the page for one app (`MANAGE_HEALTH_PERMISSIONS` needs `GRANT_RUNTIME_PERMISSIONS`, a system permission), and the manage-data intent opens "Data and access", which has no permission switches. `androidx.health.ACTION_HEALTH_CONNECT_SETTINGS` doesn't exist on Android 14+.
- **Sync triggers:** unchanged, the same as iOS (see Defaults).
- **Tests** (`HealthSyncTests`): iOS keeps Apple Health's words; Android's card says Health Connect in the title, the refused-kinds line, the turned-off note and the offer button; when Health Connect must be installed, turning on opens its page and stays off; sync and read do nothing until it's installed.
- **Checked on the emulator** (API 36, Debug, local server, a new local account): the card shows as "Health Connect" with Android's description. Turning it on opens the app's sheet. With saving weigh-ins refused, the card says "Weigh-ins are off for GainStreak in Health Connect, so they aren't saved there." with Open Settings, and after a restart the sheet doesn't come back. Open Settings opens Health Connect's home. With the permission given, a weigh-in of 80.5 kg shows in Health Connect at 8:00 from GainStreak. Changing it to 81 kg left one entry, at 81 kg. Deleting it in the app removed it from Health Connect.
- **Not checked by hand:** a finished workout and a cardio session through the app (the same insert as the spike's exercise sessions, and the sync tests cover what's sent); Undo; turning off and on again; the not-installed case on a real Android 8–13 phone.

## 23c. Reading

- `ReadProfileAsync` (height only), `ReadWeightsAsync` (changes token, expired token, skipping our own records by client record id), `ReadDailyStepsAsync` (aggregate by day).
- On the emulator: a weigh-in added in the Health Connect app is imported once; steps give a suggestion; a height fills an empty field or is offered.

**Stop and report.**

### 23c as built (2026-11-17)

- **`ReadProfileAsync`:** the latest `HeightRecord` from any app (descending, one record), in cm. Birth date and sex are always null (Health Connect has neither); the offer text already leaves them out.
- **`ReadWeightsAsync`:** the anchor is a changes token as UTF-8. With one, `GetChanges` (every page while `HasMore`) gives the weigh-ins added or changed since; deletions are skipped, as on iOS. Without one, or when Health Connect says the token expired, it takes a new token **first** and then reads every `WeightRecord` from the import start (any app, every page), so nothing added in between is lost. Each weigh-in carries its client record id as the sync id, so `HealthImport` leaves GainStreak's own out. List items from the binding can be plain Java objects, so changes are matched by Java class name and then `JavaCast`.
- **`ReadDailyStepsAsync`:** `AggregateGroupByPeriod` with `StepsRecord.CountTotal`, one-day periods over local midnights; a day without a value is 0 (`HealthImport.AverageSteps` skips those).
- **Refusals look like no data:** a Java exception from Health Connect (a refused read permission, Health Connect saying no) returns an empty result and keeps the old anchor. .NET exceptions still reach `HealthSync`'s log.
- **Checked on the emulator** (API 36; Debug, then a clean Release build), with test records from a temporary seed (removed): Profile's empty Height filled with 182 cm. A weigh-in from the seed was imported ("1 weigh-in from Health Connect added."), then a second one through the changes token (one change, imported once). Steps gave "Your steps say Active: 9,105 a day over the last 30 days." **Use it** took the line away. The Release build cold-started three times and opened Profile with no ANR, no crash and no Health errors in logcat. It was built with an unreachable https server so the local account stayed signed in (Release refuses a plain-HTTP server).
- **Found on the way:**
  - **Health Connect totals steps only from apps on its "data sources" list for steps.** An app given `WRITE_STEPS` with `adb shell pm grant` isn't on it, so its steps are stored but every daily total is empty. Given through the permission sheet, they count. Test data must be written by an app the user allowed in the sheet.
  - **The emulator froze ("isn't responding") three times, each on the first launch right after a Debug fast-deploy install**, before any Health Connect call. Later cold starts with the same data and code, Debug and Release, didn't freeze. Taken to be the Debug fast-deploy start, not this phase; worth watching on a real phone.
- **Not checked by hand:** an expired changes token (it takes about 30 days); a refused read permission (should read as no data); weigh-ins from a real scale app, and steps from a phone plus a watch counted once.

## 23d. Privacy, store and release

- `site/privacy.html`: the Health Connect section next to Apple Health (what's read and written, stays on the phone). Google requires the policy to be reachable from the rationale activity.
- `docs/STORE_LISTING.md`: Play Console's **Health apps declaration** and the Health Connect permissions declaration (each permission and why), the Data safety answers (health and fitness: not collected, since it doesn't leave the device). The user fills these in on Play Console; flag it.
- `CHANGELOG.md` 0.23.0 for testers, in the Apple Health entry's style. No migration line unless one was added. `Directory.Build.props` to 0.23.0.

**Stop and report**, with what must be checked on a real phone (an Android 13 phone without Health Connect, a Pixel with a watch for steps, a smart scale app's weigh-ins).

### 23d as built (2026-11-17)

- `site/privacy.html`: a **"Health Connect (Android)"** section with the `health-connect` anchor (the page the app's rationale activity opens), the summary, and "What isn't collected". Last updated 17 November 2026. **Publish `site/` before a build with Health Connect reaches testers**: the permission sheet links to it.
- `docs/STORE_LISTING.md`: Health Connect in "What the app stores and sends"; the Data safety form (accessed, not collected or shared); a new **"Health Connect: Play Console"** section with the Health apps declaration, each permission and why, and the review video; and a checklist item. **The user fills these in on Play Console:** Google won't publish a build with health permissions, on any track, until they're approved.
- `CHANGELOG.md` 0.23.0 and `Directory.Build.props` 0.23.0. No migration and no server change, so no `MinClientVersion` change.
- **End-of-phase checks:**
  - the full `dotnet test`: 291 server tests and 1,680 app tests pass
  - the Android Release build (clean) on the emulator, as in 23c
  - the iOS Release build for the Simulator (iPhone 17) builds and starts at the login screen; not signed in, since Release talks to the beta server. The Apple Health card keeps its iOS words, which `Card_OnTheIphone_KeepsAppleHealthsWords` checks.
  - the desktop app builds and starts

**Check on a real Android phone:**
- the permission sheet, and the privacy policy link from it, once `site/` is published
- a weigh-in from a real scale app (Withings, Renpho…), imported once
- steps from a Pixel with a watch (or Fitbit), counted once against Health Connect's own daily total
- a finished workout and a cardio session in Health Connect, and how the day name shows there and in Google Fit / Fitbit
- Undo and turning it off and on (no duplicates)
- the background sync: `errors.log` after a few days
- on an Android 13 phone without Health Connect: the install line and the Play page

## Later (not this phase)

- Background reads (`READ_HEALTH_DATA_IN_BACKGROUND`) and history (`READ_HEALTH_DATA_HISTORY`).
- Phase 20's "Later" list (calories, heart rate, other apps' workouts) on both platforms.
