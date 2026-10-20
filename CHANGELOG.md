# Changelog

Versions follow the rules in the README: **1.0.x** for bug fixes, **1.x.0** for new features (migrations allowed), **2.0.0** for breaking changes.

## 1.1.0 (unreleased)

The app on the social server (Phase 13's server, Phase 14 of the plan). Workouts stay on the device; accounts, friends, the leaderboard and shared check-ins move to the server.

- **Phase 13, the social server:** an ASP.NET Core API on Postgres for accounts, friends, the leaderboard and shared gym check-ins, with rate limits, a Docker image and migrations run on their own.
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

## 1.0.0 (unreleased)

The first release. It covers Phases 0 to 12 of the implementation plan.

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
