# Phase 26: Food log — spec for Claude Code

This is the spec for GymApp Phase 26 (version 0.26.0). The number is a placeholder: if another phase lands first,
renumber the file, the version and the sub-phase letters.

Today the user logs calories as **one number a day** (`CalorieLog.CaloriesConsumed`). This phase lets them log **what
they ate** instead: search a food, scan a packaged food's barcode or pick a recent one, choose the amount, and the
day's total adds itself up. Generic foods come from a **USDA catalog bundled with the app** (works offline), packaged
foods from **Open Food Facts** (online, cached once used), and the user can make their own. Phones only (the desktop is
frozen). Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase". 26a and 26d change the heads' own code, so they also build and
  run them right away. After each sub-phase, report: files created, files changed, the migration (26b adds one), what
  you checked by hand on each platform and what you couldn't.
- Read `CalorieLog`, `CalorieService`, `CalorieGoals`, `PointsCalculator` (the calorie target), `StatsProjectionService`,
  `CaloriesViewModel` / `CaloriesView`, Home's calories card (`HomeViewModel`, `HomeView`), `OsmHttp` and one OSM client
  (`GymAddressLookupService`), `IPlatform` (`HasLiveWorkout` is the model for a phone-only feature), `site/privacy.html`
  and `docs/STORE_LISTING.md` before changing anything.
- Every existing test must still pass. The points rules don't change, so no points test should need editing.

## Decisions already made (don't change these)

1. **Food data stays on the phone.** Foods and entries live in the app's SQLite (`AppDbContext`). Nothing about food is
   sent to the GainStreak server: no `Contracts` change, no server migration, no `MinClientVersion` change (the 0.21.0
   rule holds by not touching it).
2. **The day's total is still `CalorieLog.CaloriesConsumed`.** Points (+10 within the leeway), stats, Home's ring and
   the week bars keep reading only that. Food entries are a layer under it: every add, edit or delete of an entry
   recomputes the day's total, saves it through the same path as `LogTodayAsync` (goal and goal type snapshotted) and
   runs `PointsService.SyncAsync`. A day whose entries are all deleted keeps a log of 0 only if it had a log before the
   first entry; otherwise the `CalorieLog` row is removed, so the day reads as not logged.
3. **Today only, like now.** Entries can be added, edited and deleted for today only. Past days show their entries
   read-only. (Yesterday's dinner forgotten at midnight is a "Later".)
4. **Typing a number still works.** It becomes a **Quick add** entry ("Quick add · 450 kcal", no food, optional label)
   in the chosen meal. Days logged before this phase keep their number with no entries; the screen shows them as
   "Logged as a total". The old one-box form is no longer shown on the phones.
5. **Entries snapshot their numbers.** An entry stores its kcal and macros at the time it was logged, plus the food it
   came from. Editing a food later, or a catalog update, never changes past days (the same reasoning as `GoalAtTime`).
6. **Three sources, one `Food` table.** A food the user logs or saves is a row in `Foods` with its `Source`: `Usda`,
   `OpenFoodFacts` or `Mine`. Catalog and Open Food Facts foods are copied into `Foods` the first time they're used,
   so recents, favourites and entries never depend on the catalog file or the network. Editing a non-`Mine` food makes
   a `Mine` copy; the original stays.
7. **The USDA catalog is a separate read-only SQLite file**, not seeded through EF migrations: `foods.db`, built by a
   tool in `tools/food-catalog/`, embedded in `GymApp.Services` and extracted to the data folder when its version
   changes. It's read with `Microsoft.Data.Sqlite` directly (no `DbContext`, so no compiled model) and searched with
   **FTS5**. Same file on every head; no `IPlatform` change for it.
8. **Open Food Facts is read-only.** Barcode lookup and name search, with a User-Agent naming the app (generalise
   `OsmHttp`'s, see below), a `RequestThrottle` per endpoint and the 10-second offline rule. No account, no
   contributing products back in this phase.
9. **Phones only.** `IPlatform.HasFoodLog` (true on iOS and Android, false on the desktop), as `HasLiveWorkout`. On the
   desktop the Calories screen and Home's card stay exactly as they are.
10. **Calories, protein, carbs and fat** per entry and per day. Macros are shown, not judged: no macro goals and no
    points for them in this phase.

## Data model (26b)

`Food`
- `Id`, `UserId`, `Source` (`Usda` / `OpenFoodFacts` / `Mine`), `SourceKey` (USDA `fdc_id` or the barcode; null for
  `Mine`), `Name`, `Brand?`
- Per 100 g (or 100 ml when `IsLiquid`): `KcalPer100`, `ProteinPer100?`, `CarbsPer100?`, `FatPer100?`
- `ServingName?` and `ServingGrams?` ("1 slice", 30 g), from Open Food Facts' `serving_size` or USDA's portions
- `IsFavourite`, `LastUsedAt?`, `UseCount`, `IsHidden` (a deleted `Mine` food that entries still point to)
- Unique (`UserId`, `Source`, `SourceKey`) where `SourceKey` isn't null

`FoodEntry`
- `Id`, `UserId`, `Date`, `Meal` (`Breakfast` / `Lunch` / `Dinner` / `Snacks`), `FoodId?` (null for Quick add),
  `Label?` (Quick add's text), `Grams?`, `Servings?`, `Kcal`, `Protein?`, `Carbs?`, `Fat?`, `CreatedAt`
- Index (`UserId`, `Date`)

Migration `FoodLog` in `GymApp.Data`, then regenerate the compiled model (`CompiledModelTests` fails until you do). Test
the EF change with an **iOS Release build for the Simulator** (full AOT) before 26b is reported.

## Where the food data comes from

- **USDA FoodData Central**, Foundation Foods + SR Legacy (public domain, about 8,000 foods, names in English). The tool
  in `tools/food-catalog/` reads the CSV downloads (the user downloads them; the tool takes their folder as an
  argument), keeps energy (kcal, or kJ ÷ 4.184), protein, carbs, fat and the household portions, drops foods without
  energy, and writes `foods.db` with an FTS5 index on name. The file records its catalog version; target under 5 MB.
  The tool and its README live in `tools/`, its input never in the repo.
- **Open Food Facts** (database ODbL, contents DbCL, images not used): `GET /api/v2/product/{barcode}` with `fields=`
  limited to what we keep (`product_name`, `product_name_<lang>`, `brands`, `nutriments`, `serving_size`,
  `serving_quantity`, `quantity`). Search through the endpoint 26a picks (the search-a-licious API or `/cgi/search.pl`)
  with its documented rate limit enforced by the throttle. Names: the phone's language first, then `product_name`.
- **Sanity rules** for both (in `GymApp.Core`, `FoodRules`): kcal per 100 g between 0 and 900, each macro between 0 and
  100 g, protein + carbs + fat ≤ 100 g (+ 2 g for rounding), a serving between 1 g and 2,000 g. A product that fails
  gets "This product's numbers look wrong. Check them before logging." and opens the edit form (saving makes a
  `Mine` copy). A product without energy can't be logged as-is: same form.
- **Attribution:** Profile's About gets "Food data: USDA FoodData Central and Open Food Facts (openfoodfacts.org,
  ODbL)". Results from Open Food Facts show its name under the food.

## Search

One search box. Results in this order, de-duplicated by (`Source`, `SourceKey`):
1. the user's own foods matching the text (favourites, then by use count), `Mine` included
2. the USDA catalog (FTS5 prefix match on every word)
3. Open Food Facts, appended when online, after 500 ms without typing and 3 characters or more; a "Searching Open Food
   Facts…" row while it loads, "Offline: showing foods on this phone" when it can't

Empty box: **Recent** (last 20 distinct foods) and **Favourites**. Each row: name, brand or source, kcal per serving
(or per 100 g), and a quick "+" that logs the last amount used for that food.

## Barcode scanning (26d)

- `IPlatform.CreateBarcodeScanner()` returns an `IBarcodeScanner` with `bool IsSupported` and
  `Task<string?> ScanAsync()` (null when cancelled or refused); a no-op on the desktop.
- **iOS:** an AVFoundation capture session with `AVCaptureMetadataOutput` (EAN-13, EAN-8, UPC-E) in a full-screen view
  controller presented over the Avalonia view, with a Cancel button and a torch toggle. `NSCameraUsageDescription` in
  the head's `Info.plist` ("GainStreak uses the camera to scan food barcodes."). After changing `Info.plist`, delete
  the stale `AppManifest.plist` and `nativelibraries` (CLAUDE.md).
- **Android:** Google's **code scanner** (`GmsBarcodeScanning`, no camera permission, Google draws the UI) if 26a finds
  a working binding; otherwise CameraX + ML Kit barcode scanning with the `CAMERA` permission asked like location.
- **A scan that finds nothing** (unknown barcode, or offline): "We couldn't find this barcode." with **Add it as my
  food** (a `Mine` food with the barcode as `SourceKey`, so the next scan finds it) and **Search by name**.
- **Typing a barcode** is always possible from the scan screen's "Enter number" link. It's also how the Simulator and
  emulator are tested.

## Screens (26c)

- **Calories (phones):** the ring and goal stay at the top. Below, **Today** grouped by meal, each meal with its kcal
  total and an **Add** button; each entry shows name, amount and kcal; tap to edit, swipe or long-press to delete (the
  same pattern as sets). A line with the day's protein / carbs / fat. Past days in the week bars open read-only.
- **Add food** (a sheet): search box with a barcode button, then the lists above. The meal defaults by the time of day
  (before 11:00 Breakfast, before 16:00 Lunch, before 21:00 Dinner, else Snacks) and can be changed in the sheet.
- **Amount:** a `SegmentedPill` for **Servings** / **Grams** (Servings only when the food has one; "ml" for liquids),
  a number box, and the kcal and macros updating live. Imperial users also get **oz** in the pill (`UnitSystem`); the
  entry still stores grams.
- **New food** form: name, brand (optional), kcal and macros per 100 g **or** per serving (a pill), serving name and
  grams. Validated by `FoodRules`.
- **Home's calories card (phones):** the ring as now; the button is **Add food** (opens the sheet). The typed-number
  form moves into the sheet as **Quick add**.
- Styles from `GainStreakResources.axaml` / `GainStreakStyles.axaml`, light and dark. No desktop layout.

## Generalise the User-Agent

`OsmHttp.UserAgent` and `CreateClient` move to a shared `AppHttp` (keep `OsmHttp` forwarding to it, or rename the
usages; no behaviour change). Open Food Facts asks for "AppName/Version (contact)": use the app name, version and
platform as now plus the project's public site, never the user's email.

## Defaults to confirm (change here if wrong)

- **Meals:** the fixed four above, no custom meals.
- **USDA names in English only.** A Romanian generic catalog is a "Later".
- **Open Food Facts country:** the world database, names in the phone's language when present.
- **Recents:** 20 foods; favourites unlimited.
- **Quick "+"** logs the last amount used for that food, in the meal the sheet was opened for.
- **No photos** of products (smaller, fewer licence questions).

---

## 26a. Spike: catalog, Open Food Facts and scanners

Before building anything, confirm:
- **Catalog:** the tool builds `foods.db` from the USDA CSVs; size; FTS5 works from `Microsoft.Data.Sqlite` in an
  **iOS Release build for the Simulator** (full AOT, the bundled `e_sqlite3`) and an Android Release build; a prefix
  search ("chick bre") takes under 50 ms on the Simulator.
- **Open Food Facts:** a barcode lookup for a few real products (one Romanian, one without energy, one with only kJ),
  the fields we get, the search endpoint to use and its current documented rate limits, and the User-Agent accepted.
- **iOS scanner:** an AVFoundation view controller presented from the Avalonia app opens, returns a code on a real
  iPhone (the Simulator has no camera; check it opens and cancels there) and closes back to the app. Note whether a
  Release build needs anything kept from trimming.
- **Android scanner:** whether a binding for Google's code scanner exists and works on the emulator with Google APIs;
  if not, the CameraX + ML Kit route and its package sizes.
- Write the results under "26a results". Keep the catalog tool; throw away the rest of the spike code. **Stop and
  report**, with the catalog's size and the APK / IPA size change.

### 26a results

- **Catalog:** `tools/food-catalog` builds `foods.db` from the CSVs (Foundation 2026-10-29 + SR Legacy 2018-04): **8,223
  foods** (68 dropped for no energy), **2.3 MB**, 7,635 with a serving. Energy comes from kcal (1008), else Atwater
  (2048, 2047), else kJ ÷ 4.184. Eight foods fail the `FoodRules` macro sum (> 102 g per 100 g): they open the edit form.
- **FTS5 on the phone:** the bundled `e_sqlite3` has FTS5. In an **iOS Release build for the Simulator** (AOT) the file
  opens read-only through `Microsoft.Data.Sqlite`; "chick* bre*" takes 8.8 ms cold and under 1 ms after (target 50 ms).
  **Android Release build, emulator (Android 16, Google APIs):** same file and query: 54.6 ms the first time (file
  copied from assets to the cache folder, FTS5 cold), about 1 ms after. The first query is right at the 50 ms target;
  open the catalog when the Add food sheet opens, not on the first keystroke.
  (The emulator was never broken: the Homebrew `avdmanager` reports "Missing system image" because it reads another SDK
  folder; `~/Library/Android/sdk/emulator/emulator -avd Pixel` boots in about a minute.)
- **Open Food Facts:** barcode lookup (`/api/v2/product/{code}`) works with `fields=`; Nutella, Coca-Cola and a
  Boromir pretzel (Romanian) found. A Romanian product with **no energy** exists (Boromir covrigi, 5941487002932,
  serving "400g") and the same fields come back empty; no **kJ-only** product turned up, so that mapping is tested with
  fake data. Two sample barcodes weren't in the database ("We couldn't find this barcode" path).
- **Search endpoint:** the legacy `/cgi/search.pl` and `/api/v2/search` answered **503** from here; use
  **search-a-licious** (`https://search.openfoodfacts.org/search?q=...&langs=ro&fields=...`). It returns `brands` as a
  list, `nutriments` inline, and no `serving_size` for most hits (get it with a barcode lookup when the food is
  logged). 25 rapid calls all returned 200.
- **Rate limits (OFF docs):** product reads **15 req/min per IP**, legacy search **10 req/min**, and the docs say not
  to use them for search-as-you-type; for a mobile app the limits apply per user. Search-a-licious has its own
  (undocumented here). The throttle should allow about one product read per 4 s and use a conservative search spacing.
- **User-Agent:** accepted from `GainStreak/0.26.0 (spike; student project)`. OFF asks for "AppName/Version
  (ContactEmail)"; per the spec we use the public site, never the user's email.
- **Scanners:** iOS needs no package (AVFoundation); the simulator has no camera, so the real scan is a real-phone
  check. Android: NuGet `Xamarin.GooglePlayServices.Code.Scanner` 116.1.0.17 exists (Google's code scanner, no camera
  permission); the CameraX + ML Kit fallback packages also exist (`Xamarin.Google.MLKit.BarcodeScanning` 117.3.0.9,
  `Xamarin.AndroidX.Camera.*` 1.6.2). **Google's code scanner:** the binding compiles and links in an Android Release
  build (namespace `Xamarin.Google.MLKit.Vision.CodeScanner`, `GmsBarcodeScanning.GetClient(...).StartScan()`). On the
  emulator Play services answered "Waiting for the Barcode UI module to be downloaded" and the module hadn't arrived
  after a couple of minutes (no Google account on the emulator), so the scanner UI itself was **not seen**. Real-phone
  check. Add the manifest meta-data `com.google.mlkit.vision.DEPENDENCIES` = `barcode_ui` so Play services fetches the
  module at install time, and handle this error as "Scanner is getting ready, try again or Enter number".
- **Not measured:** APK and IPA size change (the Simulator bundle is 290 MB and not representative); the catalog adds
  2.3 MB uncompressed to the app. The Release APK with catalog and scanner package built was 55 MB.

## 26b. Data, catalog and services

- `Food`, `FoodEntry`, the `FoodLog` migration, the compiled model.
- `FoodCatalog` (extract by version, FTS5 search, read one food by `fdc_id`), `OpenFoodFactsClient` (lookup, search,
  throttle, mapping, `FoodRules`), `FoodService` (search merge, log, edit, delete, quick add, favourites, recents,
  `Mine` copies, and the roll-up into `CalorieLog` with points, decision 2).
- `CalorieService.LogTodayAsync` stays for the desktop and becomes the roll-up's save path; don't change what it does.
- Register both in `AppServices` (the same in local and remote social modes: food isn't social).
- **Tests** (`FoodTests.cs`, in-memory SQLite with the real migrations, Open Food Facts through `FakeHttpHandler`):
  the roll-up (add, edit, delete, delete the last, a day logged as a total before the first entry), points on the
  leeway edge, today only, snapshots unchanged after a food edit, search order and de-duplication, offline search, a
  product failing `FoodRules`, kJ to kcal, servings and grams, the catalog's version extraction.

**Stop and report.**

## 26c. The phone screens

- `IPlatform.HasFoodLog`; the Calories screen's food diary, the Add food sheet, the amount picker, the New food form,
  Home's card; desktop unchanged.
- Tests: the view models (meal default by time with `AppClock`, live kcal while typing an amount, Quick add, the
  desktop keeps the old form).
- On the iOS Simulator and the Android emulator: log a catalog food, an Open Food Facts food found by name, a Quick
  add, a custom food; edit and delete each; offline search; light and dark; the ring and points move as expected.

**Stop and report**, with screenshots.

## 26d. Barcode scanning

- `IBarcodeScanner` and both heads, `NSCameraUsageDescription` / the Android permission or code scanner.
- The unknown-barcode path and "Enter number".
- On the Simulator and emulator: the scanner opens and cancels; typed barcodes find a product, an unknown one, and
  one saved as my food. Scanning a real product goes on the real-phone list.

**Stop and report.**

### 26d results

- **Built:** `IBarcodeScanner` (`NoBarcodeScanner` on the desktop), the scan button and the Barcode page in the Add food
  sheet, "Enter a barcode number", the unknown-barcode card ("Add it as my food" saves under the barcode as `Mine`; "Search
  by name"), the barcode line on the New food form. iOS: AVFoundation full-screen scanner with Cancel, torch and camera
  permission (`NSCameraUsageDescription`). Android: Google code scanner (`Xamarin.GooglePlayServices.Code.Scanner`).
- **Fixed on the way:** re-opening "Enter a barcode number" showed the previous digits; it now starts empty, but keeps the
  number after a failed lookup. The iOS scanner shows no "Point the camera" hint when there is no camera.
- **iOS Simulator (iPhone 18 Pro, Debug):** the camera button opens the scanner screen ("No camera on this device"), Cancel
  returns to the sheet unchanged. Typed 3017620422003 finds Nutella (Ferrero, Open Food Facts); a long invalid number is
  refused with the 8 to 14 digits message; 5941000010000 gives "We couldn't find this barcode."; "Add it as my food" opens the
  New food form with "Barcode 5941000010000"; saving (name + 260 kcal) opens the amount page ("My food"), and looking the same
  number up again finds it as the user's own food.
- **Android emulator (Pixel, Android 16, Debug):** the scan button shows "The scanner is getting ready. Try again in a
  minute, or enter the number." (no Google account, so the Barcode UI module does not download: expected). Enter number
  with 3017620422003 finds Nutella.
- **Not checked:** scanning a real barcode on either platform (no camera in the Simulator or emulator), the camera
  permission prompt and its refusal, the Google module download, UPC-E codes. All on the real-phone list.
- **Checks:** `dotnet build`, `dotnet test tests/GymApp.Tests` (2,002 pass), Android Release APK and iOS Release for the
  Simulator build.

## 26e. Privacy, store and release

- `site/privacy.html`: food logs stay on the phone; barcodes and search text are sent to Open Food Facts to look foods
  up, with no account or identifier; the camera is used only to read barcodes and nothing is recorded.
- `docs/STORE_LISTING.md`: the camera permission's reason, App Store privacy and Play Data safety answers (food data
  not collected), the attribution.
- `CHANGELOG.md` 0.26.0 for testers in the existing style, ending with **Migration:** app database (`FoodLog`).
  `Directory.Build.props` to 0.26.0.
- The end-of-phase checks from CLAUDE.md.

**Stop and report**, with what must be checked on a real phone.

**Check on a real phone:**
- scanning on an iPhone and an Android phone: a few Romanian products, a product Open Food Facts doesn't have, a
  creased or curved barcode, low light with the torch
- the camera permission refused, then allowed in Settings
- a day of real meals logged in under a minute each, and the points for hitting the target
- search on a slow mobile connection (the 500 ms wait, the offline line)

## Later (not this phase)

- Editing yesterday's entries, and copying a meal from another day.
- Writing the daily total to Apple Health / Health Connect (`dietaryEnergyConsumed`, Phase 20's "Later") and
  importing it from diet apps.
- Macro goals, and a protein target from body weight.
- Recipes (a food made of foods) and saved meals.
- A Romanian generic catalog; contributing missing products to Open Food Facts.
- Photos of products, and nutrition labels read by the camera.
