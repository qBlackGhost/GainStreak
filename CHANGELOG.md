# Changelog

Versions follow the rules in the README: before 1.0, **0.&lt;phase&gt;.&lt;fix&gt;**, where the middle number is the implementation plan's phase and the last counts fixes within it. 1.0.0 will be the first public release.

## 0.15.3

- **Split usage:** a split's page has a "Who uses it" card under the setup card: a donut of the share of lifters on each preset and on custom splits, with this split's slice in the accent and its share in the middle. Splits under 10% (other than the one shown) are grouped into "Other", whose legend row names them. Preset cards in the split list show "N% of lifters". The numbers come from the server; without them the card and the line stay hidden, and offline the last fetched numbers are shown.
- **Recommended days:** preset cards and the split page show "Recommended for N days / week", from the preset's default number of days.
- **Server:** the stats upload (`PUT /v1/me/stats`) also carries the active split: the preset's id, or no id for a custom split. Older builds don't send it and aren't counted. `GET /v1/splits/usage` (signed in) returns how many accounts use each preset and custom splits.
- **Map:** turning "Visible to friends" on or off reloads the map, your pin and your friends' check-ins at once. Your pin now also shows when today's gym is in a city whose gyms were loaded after the Map opened (before, it appeared only after reopening the Map). Your pin never depended on the setting.
- **Gym list loading:** the public map servers can take minutes when busy. After 15 seconds the loading line says "Still loading. The map servers are busy right now." with **Try again**, which drops the waiting request and sends a new one.
- **Migration:** `SplitUsage` in the server's Postgres (`Users.SplitPresetId` and `Users.UsesCustomSplit`). Run it before deploying. The app's database is unchanged.

## 0.15.2

The first build for beta testers, with 0.15.1's light theme. 0.15.1 had no release of its own.

- **Light theme (12e):** Profile → Appearance now works: System (the default, follows Windows or macOS), Light or Dark, applied at once and kept on the account. The login screens follow the system. Every screen takes its colors from one set of light and dark tokens (`IronlogResources.axaml`) instead of fixed dark colors, after the Light page of the Gym App design canvas: off-white page, white cards, ink text, and a darker lime for accent text and outlines on white.
- **Installers and updates:** releases are built with Velopack (`build/release.sh`): a `.pkg` installing `GainStreak.app` for Apple silicon and Intel Macs, and a `Setup.exe` for Windows. Installed copies check the release folder at startup and download a newer version in the background; a banner offers "Restart now", or it installs at the next start.
- **Server address built in:** a release now carries the social server's address (`GymAppServerUrl` in `Directory.Build.props`), so testers don't set `GYMAPP_SERVER_URL`. The variable still overrides it, and publishing a release without an address stops with an error.
- **Hosted social server:** the beta server runs on Google Cloud Run (scales to zero, one instance at most) with Postgres on Neon, and release builds point at it. Secrets (the connection string and the Discord webhook) live in Secret Manager.
- **Server works on hosts that scale to zero:** the check-in cleanup runs from check-in uploads, at most once a day per instance, instead of on a daily timer an idle instance never reaches. Feedback is posted to Discord before the request answers instead of from a background queue. A failed post still answers 204, and each new feedback retries the last week's unforwarded rows (10 at most, stopping at the first failure).
- **Proxy check:** `Diagnostics__LogProxyHeaders=true` logs the proxy's address, `X-Forwarded-For`/`X-Forwarded-Proto` and the client IP the server settles on, for setting `ForwardedHeaders__KnownNetworks` on a new host. Off by default, since it logs client IPs. On Cloud Run that's `169.254.0.0/16`.
- **Migration:** `ThemePreference` in the app's database (`Users.ThemePreference`, "System" for existing accounts).

## 0.15.0

- **New name and icon:** the app is now GainStreak. Window titles, the sidebar and sign-in brand, messages and the version line in Profile use the new name, and the app icon is the fire-orange flame-arrow with a lime outline. The executable is `GainStreak` (`GainStreak.exe` on Windows), and on macOS the Dock shows the GainStreak name and icon. Code, project names and the data folder are unchanged.
- **Points history as a popup:** "History" on the dashboard's level card opens it over the page instead of a separate screen. It shows the total and progress to the next level, the ledger by week with a subtotal each, a filter (All, Workouts, Weeks, Calories) and how points are earned. Escape, the close button or a click outside closes it.

- **Phase 15, crash reports and the version check:**
  - Every `errors.log` entry names the app version and system. **Feedback**, in its own Support section of the sidebar, opens a popup to report a problem or suggest an idea. **Send** posts it to the server, which stores it and forwards it to the developer's Discord channel. A problem attaches the error report (the app, the system and the log, newest 100 KB) unless turned off; "See what's sent" shows it first, and user names are taken out of file paths before anything leaves the device. Offline or refused, the note stays and "Copy instead" puts it on the clipboard. "Show log file" opens its folder.
  - Server: `POST /v1/feedback` (signed in, 5 an hour per user), the `Feedback` table (deleted with the account) and `Feedback__DiscordWebhookUrl` for forwarding.
  - The app sends its version (`X-App-Version`) with every server call. With `MinClientVersion` set, the server answers older builds, and builds that send no version, with 426 and "please update". The app shows that as a banner, keeps signing in from its cache so workouts still work, and keeps its pending uploads for after the update.
  - **Migration:** `AddFeedback` in the server's Postgres (the `Feedback` table). The app's database is unchanged.

## 0.14.1

The app on the social server. Workouts stay on the device; accounts, friends, the leaderboard and shared check-ins move to the server.

- **Phase 14, the app on the server:**
  - Server additions: change the account email, and replace all of your shared check-ins in one call.
  - `GymApp.Client`: a typed client for the server, where every call is Ok, Refused (with the server's message) or Offline, a 10-second timeout, and the session token stored in a file only you can read.
  - Accounts on the server: register, sign in with your username or email and stay signed in until you log out. Change your email or password, and delete your account (online and on the device) from Profile. "Remember me" is gone.
  - Offline sign-in on your own device with your last password; a wrong password from the server never falls back to it.
  - "Move this account online" for accounts from before the server: keeps their workouts, points and streak.
  - Friends, requests and both leaderboards from the server, kept on the device and shown offline with "Last updated ...". Friend actions need a connection.
  - Uploads of your sharing setting, the last 31 days of check-ins and the 14-day points and streak projection, retried until they get through (after a check-in or undo, a gym change, a sharing change, at sign-in, every 15 minutes and when the connection comes back).
  - The friends map from the server, matched to the gyms and cities on this device.
  - TestBench: a Social switch, Local (the default, no server needed) or Server.
- **Migration:** `SocialServerLink` in the app's database (the link to the server account, the offline cache and the pending uploads). Existing data is unchanged.

## 0.13.0

- **Phase 13, the social server:** an ASP.NET Core API on Postgres for accounts, friends, the leaderboard and shared gym check-ins, with rate limits, a Docker image and migrations run on their own. The app doesn't use it yet.

## 0.12.0

The first complete version of the app. It covers Phases 0 to 12 of the implementation plan.

- **Phase 0, project setup:** a solution with Core, Data, Services, Desktop (Avalonia) and Tests projects.
- **Phase 1, data model:** EF Core with SQLite, the first migration, and seeded preset splits (PPL, Upper/Lower, PPL + Upper/Lower, Full Body, Bro Split).
- **Phase 2, accounts:** register and sign in with hashed passwords, and a "remember me" sign-in.
- **Phase 3, splits:** pick a preset split or build a custom one with your own training days.
- **Phase 4, schedule and streaks:** preset layouts with a chosen start day, schedule history so changes never rewrite the past, the "I went today" log, and a streak where rest days count and a miss can be made up the next day (the grace rule).
- **Phase 5, exercises and PRs:** a built-in exercise list per muscle group, custom exercises, exercises per split day with planned sets, set logging, PR history with an Epley 1RM estimate, and manual PR edits.
- **Phase 6, profile:** sex, birth date, height, activity level, units (metric or imperial), weigh-ins, and a maintenance-calorie estimate.
- **Phase 7, calorie goals:** lose, maintain or gain with a suggested goal and safe limits, plus daily calorie logs with a target range.
- **Phase 8, points and levels:** a points ledger for workouts, complete weeks (+50, or +25 with a made-up day) and calorie targets, with levels computed from the total.
- **Phase 9, gyms:** a home city from Nominatim, gyms from Overpass cached per city, a list or map picker, custom gyms with address search, the gym logged per workout, and gyms visited per month.
- **Phase 10, friends:** friend requests and a friends-only leaderboard (this week or all time) with each friend's streak.
- **Phase 11, check-in rules:** undo today's check-in (and the points it earned), a check-in that needs most of the day's exercises filled in, and last session's sets as placeholders.
- **Phase 12, polish:**
  - Streak fix: a user with no streak no longer flips between 1 and 0.
  - Missed-day choice the day after a miss: shift the whole schedule (once a week), make it up before the next rest day (twice a week), or skip it.
  - Home is now a dashboard: today's workout, this week's row, recent PRs and the friends card.
  - Validation and error handling: stricter usernames and passwords, whole-number parsing, messages next to each field, Retry for online searches, an offline note on the map, and a global error handler that writes `errors.log`.
  - Light and dark themes with a theme setting, and the app icon. *(12e, still to do.)*
  - Release prep: one shared version, the version shown in Profile, the README and this changelog.
