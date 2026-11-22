# Store listing notes (Google Play and App Store)

Notes for filling in the store forms for the GainStreak phone betas (Phase 16h). Nothing here has been submitted. Keep it in step with the app: a new kind of data sent to the server means new answers here.

## Identity

- **Name:** GainStreak
- **Package / bundle id:** `com.gainstreak.app` (Android `ApplicationId` and iOS bundle id). It can't change after the first upload.
- **Version:** `Version` in `Directory.Build.props` (0.16.0 for the first beta); the build number is the commit count (see "Releases for testers" in the README).
- **Category:** Health & Fitness.
- **Short description (Play, 80 characters):** Log your workouts, keep your streak, and climb a leaderboard with your friends.

## What the app stores and sends

**Only on the phone** (the app's SQLite database; never sent): workouts, sets and reps, PRs, splits, body data (gender, birth date, height, weight, activity level), weigh-ins, calorie goals and logs, the food log (foods, favourites, entries; Phase 26), reminder settings, and the error log.

**On the server** (Google Cloud Run in Belgium, with a Neon Postgres database in Frankfurt):

| Data | Why | Who sees it |
|---|---|---|
| Username, email, password (stored only as a hash) | The account; signing in | Username: friends and people searching for a username. Email: nobody else |
| A device name per signed-in session | Telling sessions apart | Nobody else |
| Points, weekly points, level, current streak | The friends leaderboard | Friends |
| Active split (which built-in preset, or "custom") | "Who uses it" percentages on split pages | Everyone, only as totals |
| Gym check-ins: gym name, its OpenStreetMap id and coordinates, city, date (optional: only once the user turns on "Visible to friends") | The friends map | Friends, as gym and day only, never the time |
| Friend requests and friendships | Friends | The two people involved |
| Invite code (random, one per account, resettable; Phase 21) | Adding friends by QR code or link | Whoever the user shows it to; the `/add/<code>` page never says whose it is |
| Push token per signed-in phone (Phase 22; only while friend notifications are on) | Friend notifications: a request, an accepted request, an accepted invite | Nobody; deleted on logout, account deletion, turning them off, or when APNs/FCM report it dead |
| Shared splits and workouts (snapshots of exercises and sets) | Sending a plan to a friend | The receiving friend; removed once used or after 30 days |
| Users the person blocked | Keeping them apart | Nobody else |
| Reports about another user: who, the reason, an optional note | Dealing with abuse | The developer; forwarded to a private Discord channel |
| Feedback text and, for a problem, the error report the user chose to attach (app version, system, error log with user folder names removed) | Fixing bugs | The developer; forwarded to a private Discord channel |

The email address also receives codes, for confirming it and resetting the password, sent from noreply@gainstreak.app through Resend (an email delivery service, since 11 November 2026; Gmail's servers before), which acts for the developer.

**Push notifications (Phase 22):** Apple's push service (iPhone) and Firebase Cloud Messaging (Android, Google) carry each friend notification to the phone's push token: a per-event title ("Friend request", "Request accepted", "New friend") and the sentence with the friend's username, plus the event's kind and the sender's user id as data for the tap. Nothing else. They act for the developer (service providers), so this isn't sharing with third parties.

**Other services the phone talks to:** OpenStreetMap's map tiles, Nominatim (city search, and the city at a rounded position for "Show me gyms near me", Phase 19) and the Overpass API (the gyms in a city). They see the phone's IP address and the area asked for, nothing about the account.

**Used on the phone only (Phase 18):** precise location, during a live workout on Home. It's read at Start workout and after each set until the phone is found near one of the user's gyms, compared on the phone with the gyms already saved there, and then dropped. Only the result is kept, on the phone: the workout is verified, and at which gym. Positions aren't stored, logged, or sent to the server or OpenStreetMap. Foreground only: no background location permission on either platform. The desktop never reads it.

**Phase 19 (vacation):** two more uses, both foreground only. (1) **Noticing a trip:** when Home appears, at most every 6 hours and only if location is already allowed and precise, one fix is compared on the phone with the home city's box and dropped; only the time of the check is stored. (2) **Show me gyms near me:** on the user's tap, one fix is rounded to 2 decimals (about 1 km) and sent to OpenStreetMap's Nominatim, at city level, to name the city; the city is kept on the phone. This is the only position that leaves the phone, and it never reaches the GainStreak server.

**Apple Health (Phase 20, iPhone):** off until turned on in Profile. It saves the app's own workouts, cardio sessions and weigh-ins to Health and reads weight, height, birth date, sex and daily steps from it. Everything read stays on the phone: it fills the body data on the phone, never reaches the server, earns no points and isn't used for ads.

**Health Connect (Phase 23, Android):** the same as Apple Health, off until turned on in Profile. It saves the app's own workouts, cardio sessions and weigh-ins and reads weight, height and daily steps (Health Connect has no birth date or sex). Everything read stays on the phone.

**Food log (Phase 26):** foods and entries stay on the phone. To look up a packaged food, the barcode or the search text goes to Open Food Facts (openfoodfacts.org, no account, no identifier; it sees the IP address). The camera is used only to read barcodes on the user's tap: no picture is kept or sent. USDA FoodData Central is bundled in the app and works offline.

**Not collected by GainStreak:** location (see above: the server never gets it; the only position that leaves the phone is the rounded one sent to Nominatim on the user's tap), contacts, photos, health data (Apple Health and Health Connect are read and written on the phone only), advertising ids. There are no ads, no analytics and no tracking SDKs. Crashes aren't sent anywhere unless the user sends them through Feedback.

## Google Play: Data safety form

- **Does the app collect or share data?** Collects: yes. Shared with third parties: no. Discord forwarding of feedback and reports is a service the developer uses, which Play counts as processing, not sharing.
- **Encrypted in transit:** yes (HTTPS only; release builds refuse plain HTTP).
- **Users can request deletion:** yes (see "Deleting an account").
- **Data types:**
  - **Personal info:**
    - Name (the username): collected; required; used for app functionality and account management.
    - Email address: collected; required; used for account management.
    - User IDs: collected; required; used for account management.
  - **App activity:**
    - Other actions (points, streak, check-ins, shares): collected; optional, with check-ins shared only once turned on; used for app functionality.
  - **App info and performance:**
    - Crash logs and diagnostics: collected only when the user attaches them to feedback; optional; used for analytics (bug fixing).
  - **Device or other IDs (from 0.22.0, decided 2026-11-15, to confirm before entering):**
    - Device or other IDs (the push token): collected; not shared (FCM is the developer's service provider); optional (only while friend notifications are on); used for app functionality (friend notifications). Not processed ephemerally: kept until logout, turning it off or account deletion.
  - **Messages:**
    - Other in-app messages (feedback text, notes on reports about other users): collected; optional; used for developer communications and fraud prevention, security and compliance.
  - **Location (from 0.19.0):**
    - Approximate location: collected; shared; processed ephemerally; optional; used for app functionality. Only the rounded position (about 1 km) sent to OpenStreetMap's Nominatim when the user taps "Show me gyms near me" on vacation.
- **Not collected:** precise location, financial info, health and fitness (workouts, body data and what's read from or saved to Health Connect never leave the phone), contacts, photos, files. Device ids apart from the push token (0.22.0, above).
- **Location (Phase 18):** *accessed* (`ACCESS_FINE_LOCATION` and `ACCESS_COARSE_LOCATION`, foreground only). The precise position is **not collected and not shared**: it's used on the phone only (for the rounded one, see Phase 19 below). No background location, so no location permissions declaration is needed in the Play Console.
- **Approximate location (Phase 19), decided:** declared as above. "Show me gyms near me" sends a rounded position (about 1 km) to Nominatim, a third party (OpenStreetMap), on the user's tap; Play counts data sent off the device as collected, and sent to a third party that isn't the developer's service provider as shared. Ephemeral: GainStreak doesn't keep it, and it isn't used for anything but naming the city. Enter it in Play Console before 0.19.0 goes to the store.

- **Food log and camera (Phase 26), decided:** food data is **not collected** (it never leaves the phone). Barcodes and search text sent to Open Food Facts are not tied to the user, hold no identifier and are used only for the lookup: no extra data type to declare beyond what's above; if Play's form asks, treat it like the Nominatim case (sent to a third party, ephemeral, app functionality) and answer "search history" only if you decide it counts. **Camera:** the Android scanner is Google's code scanner (Google Play services), which needs **no CAMERA permission** in the manifest and returns only the number. The Barcode UI module downloads on first use.

## App Store: App Privacy ("nutrition label")

- **Data linked to the user:**
  - Contact Info: Email Address (App Functionality).
  - Identifiers: User ID (App Functionality).
  - Identifiers: Device ID (App Functionality), from 0.22.0: the APNs push token, kept per signed-in phone while friend notifications are on. To confirm before entering.
  - User Content: Other User Content, meaning shared splits and workouts, check-ins, feedback and reports about other users (App Functionality).
  - Usage Data: Other Usage Data, meaning points, streak and level (App Functionality).
  - Diagnostics: Crash Data and Other Diagnostic Data, only when attached to feedback (App Functionality).
- **Location (Phase 18):** precise location is **not collected.** Apple counts data as collected only when it's sent off the device, and the precise position never is (for the rounded one, see Phase 19 below). The permission prompt shows `NSLocationWhenInUseUsageDescription` from `Info.plist`: "GainStreak checks that you're at one of your gyms during a workout. Your location stays on your phone." When in use only, no "Always".
- **Coarse Location (Phase 19), decided:** declared under "Data not linked to the user" below: the rounded position sent to Nominatim for "Show me gyms near me". Apple counts data as collected when it leaves the device and a party can keep it longer than the request needs, and OpenStreetMap's servers keep request logs for a while. Precise location stays not collected. Enter it in App Store Connect before 0.19.0 goes to the store.
- **The prompt (Phase 19):** `NSLocationWhenInUseUsageDescription` now reads "GainStreak checks that you're at one of your gyms during a workout and notices when you're travelling. Your location stays on your phone unless you ask for gyms near you." Android's note before its prompt says the same.
- **Health & Fitness (Phase 20):** **not collected.** What's read from Apple Health and what's saved there never leaves the phone (same reasoning as precise location). Nothing to declare; Apple's HealthKit rules still apply: no ads or marketing use, no iCloud storage of Health data (the app keeps it in its local database only), and a privacy policy that says how it's used (`site/privacy.html`, "Apple Health").
- **Food log and camera (Phase 26), decided:** food data is **not collected** (stays on the phone). Barcodes and search text sent to Open Food Facts carry no identifier and aren't linked to the user; they are the same kind of lookup as the OpenStreetMap ones, so nothing new to declare (if you want to be strict, add "Search History, not linked, App Functionality"). Camera prompt, `NSCameraUsageDescription`: "GainStreak uses the camera to scan food barcodes." Nothing is recorded.
- **Data used to track you:** none.
- **Data not linked to the user:**
  - Location: Coarse Location (App Functionality), from 0.19.0. Not used for tracking.
- **Health Connect (Phase 23):** *accessed*, **not collected and not shared** (it never leaves the phone), so nothing to add to the data types. What Play asks for separately is below.
- **Export compliance:** `ITSAppUsesNonExemptEncryption` is false in `Info.plist` (HTTPS only), so no questions on upload.

## Location: notes for the stores (Phase 18)

- **A note before Android's prompt:** built. Android's prompt shows no word from the app, so a "Verify your workouts" note comes first, with Continue / Not now. Google asks for a prominent disclosure only when location is collected, which it isn't; the note is there to make the request clear.
- **Approximate location:** built. With approximate only, the card says "Turn on precise location to verify your workouts" with Open Settings, instead of reading fixes too vague to count.
- **Store descriptions.** The long description should mention it ("Workouts done at your gym are marked verified; your location stays on your phone"), so reviewers see why the app asks. Both reviews check that the prompt matches a visible feature.
- **App Review notes (iOS), Apple Health (Phase 20).** Apple asks that HealthKit be clearly visible in the app. Say: "Apple Health is in Profile → Apple Health. Turning it on shows the permission sheet; GainStreak saves finished workouts, cardio sessions and weigh-ins, and reads weight, height, birth date, sex and steps to fill in the profile and suggest an activity level. Health data never leaves the phone." The review account can turn it on; Health on a review device is usually empty, so expect the step suggestion to stay hidden.
- **App Review notes (iOS), notifications (Phase 22).** Notifications are asked for only when the user turns on Reminders or Friend requests in Profile → Notifications, or taps Turn on on Friends' card. A reviewer sees a friend notification by sending the review account a request from a second account.
- **Store descriptions (0.22.0).** Mention it: "Get a notification when a friend sends you a request or accepts yours."
- **App Review notes (iOS).** Say where location is used (Start workout on Home) and that the review account's gyms are in Timișoara, so a reviewer elsewhere sees the "not at one of your gyms" path, which is expected.

## Health Connect: Play Console (Phase 23)

Google reviews every app that asks for Health Connect permissions. Before the first build with them goes to any track (internal testing included), in Play Console:
- **App content → Health apps declaration:** the app's health features are fitness: "activity and fitness" (workouts, cardio, steps) and "body measurements" (weight, height). Not a medical device, no medical records.
- **App content → Health Connect permissions:** declare each permission the manifest asks for, with why:
  - `READ_WEIGHT`: weigh-ins from a scale or another app fill the days the user hasn't logged in GainStreak.
  - `WRITE_WEIGHT`: the weigh-ins the user logs in GainStreak are saved to Health Connect.
  - `READ_EXERCISE`: GainStreak reads back the workouts it saved, to keep them in step (replace or delete them when the user changes or undoes a workout). Other apps' workouts aren't used.
  - `WRITE_EXERCISE`: finished workouts and cardio sessions are saved to Health Connect.
  - `READ_HEIGHT`: fills an empty height in the body data, or offers it where it differs.
  - `READ_STEPS`: the average daily steps of the last 30 days suggest an activity level for the calorie estimate.
  - No background reading and no history permission.
- **Privacy policy:** must describe the Health Connect use; it does (`site/privacy.html`, "Health Connect (Android)", which the app's permission page opens). Publish `site/` before uploading.
- **Review video, if Play asks for one:** Profile → Health Connect → turn it on → the permission sheet → a logged weigh-in appearing in Health Connect.

## Deleting an account

- **In the app:** Profile → Account → **Delete account**. It asks for the password, then deletes the online account and everything that hangs off it: sessions, friendships, stats, check-ins and shares. It also deletes the workouts and the rest of the data on that phone. Required by the App Store; built in Phase 14.
- **Without the app (Google Play needs a web link):** `site/delete-account.html`, which gives the in-app steps and an email request (deleted within 30 days), and lists what's deleted and what's kept. The link goes in the Data safety form.

## Still needed before the first upload

- [x] **Privacy policy URL** (both stores): https://qblackghost.github.io/GainStreak-releases/privacy.html, from `site/privacy.html` (see "Public pages" below).
- [x] **Account deletion web link** for Google Play: https://qblackghost.github.io/GainStreak-releases/delete-account.html
- [ ] **Terms of use URL:** https://qblackghost.github.io/GainStreak-releases/terms.html, from `site/terms.html`. Sign-up requires agreeing to it. Publish it before a build that links to it goes out.
- [x] **Support URL or email** (App Store): https://qblackghost.github.io/GainStreak-releases/ (names `gainstreak.app@gmail.com`).
- [ ] **Content rating questionnaire** (Play) and **age rating** (App Store). There's no violence, gambling or purchases. Other people see usernames (to friends and in username search) and the names in what friends share. Users can report and block each other.
- [ ] **Screenshots** (see below).
- [ ] **Push Notifications capability (Phase 22):** on developer.apple.com, App ID `com.gainstreak.app` → Capabilities → Push Notifications, then make the profiles again. The APNs key (Keys → +, Apple Push Notifications service) is separate and goes to the server through Secret Manager.
- [ ] **Firebase (Phase 22):** an Android app `com.gainstreak.app` in the Firebase project, with both SHA-256 fingerprints, and its `google-services.json` for the build (see the README).
- [ ] **HealthKit capability (Phase 20):** on developer.apple.com, App ID `com.gainstreak.app` → Capabilities → HealthKit, then make the App Store profile again and download it. A signed build fails without it, since the app's entitlements now include HealthKit.
- [ ] **Health Connect declarations (Phase 23):** the Health apps declaration and the Health Connect permissions form in Play Console (see "Health Connect: Play Console"). Google won't publish a build with health permissions until they're approved.
- [ ] **Test account for App Review:** Apple's reviewers need to sign in. Make a review account with a few friends and some check-ins, and give its username and password in App Store Connect, not in this repo.

## Public pages (`site/`)

`site/` holds the pages the stores link to: `index.html` (support), `privacy.html`, `terms.html` and `delete-account.html`, with one `style.css`. They're plain HTML with no scripts, cookies or outside requests, in light and dark.

- **Contact:** `gainstreak.app@gmail.com` on all four pages, the address that answers account requests. Use the same one as the Play Console's developer email and the App Store's support contact.
- **Publishing:** GitHub Pages serves the `docs/` folder on `main` of the public releases repository (qBlackGhost/GainStreak-releases) at https://qblackghost.github.io/GainStreak-releases/. After changing a page here, copy `site/` over that `docs/` folder and push; Pages rebuilds in a minute or two.
- **Keep them in step** with "What the app stores and sends": new data sent to the server means a new row in the privacy table and a new "Last updated" date.

## Screenshots (the 16e phone layouts)

Take them in dark mode with a seeded account: a week of workouts, a few PRs, two or three friends and some check-ins.

**The seeded account** comes from `tools/GymApp.ScreenshotSeed`, which runs the app's own services against the local server (`docker compose up -d && dotnet run --project src/GymApp.Server`):

```bash
dotnet run --project tools/GymApp.ScreenshotSeed -- /tmp/gainstreak-seed
```

It makes **alex** (six weeks of Push / Pull / Legs, PRs, weekly weigh-ins, calories most days, today a training day not yet started, dark theme) and three friends, **maya**, **jonas** and **sam**, each with their own split, history and gym check-ins in Timișoara, all friends with alex. The password for all four is in the tool's `Program.cs` (local test accounts only). A rerun signs in to the same accounts and replaces their data. Then put `gymapp.db` on a phone running a Debug build (which talks to the local server) and sign in as alex:

- **iOS Simulator:** `xcrun simctl get_app_container booted com.gainstreak.app data` gives the app's folder; copy the file to `Library/Application Support/GymApp/gymapp.db` there while the app is closed.
- **Android emulator:** `adb push gymapp.db /data/local/tmp/` then `adb shell run-as com.gainstreak.app cp /data/local/tmp/gymapp.db files/GymApp/gymapp.db` (Debug builds only), with the app stopped.

1. **Home:** the greeting, level card and today's workout.
2. **Workouts → a day:** the exercise list.
3. **Exercise:** sets, plan and the PR block.
4. **Calories:** the today ring and the last 7 days.
5. **Friends:** the leaderboard.
6. **Map:** friends' check-ins, with the Your gym sheet closed.
7. **Splits:** the active split banner.

**Sizes:**

- **App Store:**
  - 6.9" iPhone at 1320 × 2868, taken on an iPhone 17 Pro Max Simulator with `xcrun simctl io booted screenshot home.png`. App Store Connect scales these down for smaller iPhones.
  - 13" iPad at 2064 × 2752, because the app also runs on iPad (`UIDeviceFamily` 1 and 2). Alternatively, remove iPad support before the first upload.
- **Google Play:** at least 2 phone screenshots, 1080 × 2400 from the emulator (`adb exec-out screencap -p > home.png`), plus a 512 × 512 icon and a 1024 × 500 feature graphic.

## Food data attribution (Phase 26)

- Profile's About shows "Food data: USDA FoodData Central and Open Food Facts (openfoodfacts.org, ODbL)"; results from Open Food Facts show its name under the food. USDA FoodData Central is public domain (CC0). Open Food Facts' database is ODbL and its contents DbCL; product images aren't used.
- **Store long description:** mention "log what you eat: search foods, scan barcodes" so reviewers see why the app asks for the camera (iOS review notes: the scan button is on Calories > Add food; a typed barcode works without a camera, e.g. 3017620422003).
