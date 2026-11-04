# Store listing notes (Google Play and App Store)

Notes for filling in the store forms for the GainStreak phone betas (Phase 16h). Nothing here has been submitted. Keep it in step with the app: a new kind of data sent to the server means new answers here.

## Identity

- **Name:** GainStreak
- **Package / bundle id:** `com.gainstreak.app` (Android `ApplicationId` and iOS bundle id). It can't change after the first upload.
- **Version:** `Version` in `Directory.Build.props` (0.16.0 for the first beta); the build number is the commit count (see "Releases for testers" in the README).
- **Category:** Health & Fitness.
- **Short description (Play, 80 characters):** Log your workouts, keep your streak, and climb a leaderboard with your friends.

## What the app stores and sends

**Only on the phone** (the app's SQLite database; never sent): workouts, sets and reps, PRs, splits, body data (gender, birth date, height, weight, activity level), weigh-ins, calorie goals and logs, reminder settings, and the error log.

**On the server** (Google Cloud Run in Belgium, with a Neon Postgres database in Frankfurt):

| Data | Why | Who sees it |
|---|---|---|
| Username, email, password (stored only as a hash) | The account; signing in | Username: friends and people searching for a username. Email: nobody else |
| A device name per signed-in session | Telling sessions apart | Nobody else |
| Points, weekly points, level, current streak | The friends leaderboard | Friends |
| Active split (which built-in preset, or "custom") | "Who uses it" percentages on split pages | Everyone, only as totals |
| Gym check-ins: gym name, its OpenStreetMap id and coordinates, city, date (optional: only once the user turns on "Visible to friends") | The friends map | Friends, as gym and day only, never the time |
| Friend requests and friendships | Friends | The two people involved |
| Shared splits and workouts (snapshots of exercises and sets) | Sending a plan to a friend | The receiving friend; removed once used or after 30 days |
| Users the person blocked | Keeping them apart | Nobody else |
| Reports about another user: who, the reason, an optional note | Dealing with abuse | The developer; forwarded to a private Discord channel |
| Feedback text and, for a problem, the error report the user chose to attach (app version, system, error log with user folder names removed) | Fixing bugs | The developer; forwarded to a private Discord channel |

The email address also receives codes, for confirming it and resetting the password, sent through Google's mail servers.

**Other services the phone talks to:** OpenStreetMap's map tiles, Nominatim (city search) and the Overpass API (the gyms in a city). They see the phone's IP address and the area asked for, nothing about the account.

**Used on the phone only (Phase 18):** precise location, during a live workout on Home. It's read at Start workout and after each set until the phone is found near one of the user's gyms, compared on the phone with the gyms already saved there, and then dropped. Only the result is kept, on the phone: the workout is verified, and at which gym. Positions aren't stored, logged, or sent to the server or OpenStreetMap. Foreground only: no background location permission on either platform. The desktop never reads it.

**Not collected:** location (see above: it never leaves the phone; the map centres on the home city you pick), contacts, photos, health-platform data, advertising ids. There are no ads, no analytics and no tracking SDKs. Crashes aren't sent anywhere unless the user sends them through Feedback.

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
  - **Messages:**
    - Other in-app messages (feedback text, notes on reports about other users): collected; optional; used for developer communications and fraud prevention, security and compliance.
- **Not collected:** location, financial info, health and fitness (workouts and body data never leave the phone), contacts, photos, files, device ids.
- **Location (Phase 18):** *accessed* (`ACCESS_FINE_LOCATION` and `ACCESS_COARSE_LOCATION`, foreground only) but **not collected and not shared**: Play's form only covers data sent off the device, and the position is used on the phone only. No background location, so no location permissions declaration is needed in the Play Console.

## App Store: App Privacy ("nutrition label")

- **Data linked to the user:**
  - Contact Info: Email Address (App Functionality).
  - Identifiers: User ID (App Functionality).
  - User Content: Other User Content, meaning shared splits and workouts, check-ins, feedback and reports about other users (App Functionality).
  - Usage Data: Other Usage Data, meaning points, streak and level (App Functionality).
  - Diagnostics: Crash Data and Other Diagnostic Data, only when attached to feedback (App Functionality).
- **Location (Phase 18):** **not collected.** Apple counts data as collected only when it's sent off the device, and the position never is. The permission prompt shows `NSLocationWhenInUseUsageDescription` from `Info.plist`: "GainStreak checks that you're at one of your gyms during a workout. Your location stays on your phone." When in use only, no "Always".
- **Data used to track you:** none.
- **Data not linked to the user:** none beyond the above.
- **Export compliance:** `ITSAppUsesNonExemptEncryption` is false in `Info.plist` (HTTPS only), so no questions on upload.

## Location: notes for the stores (Phase 18)

- **A note before Android's prompt:** built. Android's prompt shows no word from the app, so a "Verify your workouts" note comes first, with Continue / Not now. Google asks for a prominent disclosure only when location is collected, which it isn't; the note is there to make the request clear.
- **Approximate location:** built. With approximate only, the card says "Turn on precise location to verify your workouts" with Open Settings, instead of reading fixes too vague to count.
- **Store descriptions.** The long description should mention it ("Workouts done at your gym are marked verified; your location stays on your phone"), so reviewers see why the app asks. Both reviews check that the prompt matches a visible feature.
- **App Review notes (iOS).** Say where location is used (Start workout on Home) and that the review account's gyms are in Timișoara, so a reviewer elsewhere sees the "not at one of your gyms" path, which is expected.

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
