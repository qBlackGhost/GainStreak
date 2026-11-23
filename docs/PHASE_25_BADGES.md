# Phase 25: Badges — spec for Claude Code

This is the spec for GymApp Phase 25 (version 0.25.0). It adds **badges**: 21 visible badges in four groups and 18
hidden ones, earned from what the phone already logs. Your badges get their own screen and a section on the profile,
friends can see the badges you earned, and you can pick one **featured badge** that shows next to your name on the
friends' map and the leaderboard. Phones only (the desktop is frozen). Work through the sub-phases **in order** and
stop after each one for review.

The design is the GainStreak Badges canvas (Design artifact, private to the developer):
https://claude.ai/artifact/QzR1EzfUXTAc2SzVDQF61v. Its boards: **Badge set** (every visible badge and its states),
**Tiers, materials and sizes**, **Phone · Achievements**, **Phone · Unlock moment**, **Heavy Lifter levels and hidden
badges**, and the row **Badges on the profile, map and leaderboard** (your profile, the featured-badge picker, a
friend's badges, map pins, leaderboard rows). The drawing rules are repeated in the appendix so this file stands on
its own. The canvas's "Option B · Medals" direction was dropped.

## Ground rules

- Do not commit or push.
- Phones only. Gate every badge screen and entry point through `IPlatform` (a new `HasBadges`, true on the iOS and
  Android heads, false on the desktop and default), the way `HasLiveWorkout` does. Don't build desktop layouts.
- Keep the server accepting 0.21.0: don't raise `MinClientVersion`. Every `Contracts` change is additive (new
  records, new endpoints, new optional trailing parameters), and old clients must keep working against the new server.
- Time comes from `AppClock` / an injected `TimeProvider`, never `DateTime.Now`.
- Checks follow CLAUDE.md: after each sub-phase `dotnet build` and `dotnet test tests/GymApp.Tests`; a sub-phase that
  touches `Contracts`, the server or a head also builds or tests that part right away; the full set at the end.
- User-facing text says "GainStreak", code keeps "GymApp".

## Decisions already made (don't change these)

- **Badges are computed on the phone** from local data (local-first). The server only stores what the phone uploads:
  which badges, their tier, the earn date and the featured badge. It never computes badges.
- **Earned badges are kept.** Once earned, a badge or a tier is never taken away, even if the log that earned it is
  later edited or deleted. Progress can still change.
- **Tiers:** Steel, Lime, Fire, Blaze (1 to 4). One-time badges have no tier (0) and show in lime.
  **No badge is earned more than once:** anything you can do again climbs tiers instead. (Each month's Monthly
  Challenge is its own one-time badge.)
- **Heavy Lifter shows its level's picture:** car, elephant, blue whale, space station.
- **Hidden badges** show as a dashed "?" badge with a one-line hint until earned, then show normally with a "Secret"
  tag. I'll Be Back (coming back after a break) is hidden, so a slow patch is never shown as a locked badge.
- **Friends see earned badges and tiers only,** never progress ("61 / 150 t"). A hidden badge a friend has
  and the viewer hasn't earned shows to the viewer as "?" with its hint (masked in the client). The same goes for a
  featured hidden badge: friends who haven't earned it see a "?" named "Secret badge" (the lighter `Secret` look),
  and the picker warns when you pick one.
- **One featured badge**, chosen in a picker, or none. Until the user picks, the featured badge is automatic: the
  highest tier earned, ties broken by the most recent. It shows on the profile picture's corner, after the name in leaderboard rows and
  in a gym's card on the map (not on the pins themselves; changed 2026-11-19).
- **Visible badges are the basic ones,** easy to start on from the first weeks. The meme and pop-culture badges are
  hidden, so they stay a surprise.
- **Fresh Start covers only January 1; First of the Month! covers the 1st of February to December.**

## Defaults to confirm (change here if wrong)

- **No points for badges.** Badges don't add to points or levels, so the leaderboard rules stay the same. (The unlock
  mockup shows "+250 pts"; drop that line unless this changes.)
- **Sharing is on by default:** "Friends can see my badges" starts on. Off hides your badges and your featured badge
  from friends (they see neither in your profile, on the map nor on the leaderboard).
- **Double Up:** a workout log can only exist once a day (`WorkoutLog` is unique on user and date), so "two workouts
  in one day" means a strength workout plus a separate cardio session (`CardioSession`) on the same date.
- **Night Owl, Eye of the Tiger and Marathon Session need a live workout:** only `LiveWorkout` has times. A workout logged
  after the fact can't earn them; the hint text doesn't promise otherwise.
- **Monthly Challenge** v1: one challenge per month built into Core ("N workouts this month"), so no server work.
  Each month is its own badge (id `monthly-2026-11`), earnable only in that month.
- **Backfill without fireworks:** the first run after updating awards everything the history already earns, quietly,
  then shows one sheet: "You earned 9 badges from your history".

## The badges

Ids are stable kebab-case strings, sent over the wire: never rename one. The client ignores ids it doesn't know
(badges from newer clients). Thresholds are in kg; the display follows the user's units.

**Visible**

| Id | Name | Group | Rule | Tiers | Glyph |
|---|---|---|---|---|---|
| `streak` | Streak | Consistency | Longest streak reached (`StreakResult.Longest`) | 7 / 30 / 100 / 365 days | flame |
| `perfect-week` | Perfect Week | Consistency | A finished Monday–Sunday week (`Leaderboard.WeekRange`) with at least one training day, every training day `Done` (not Graced or Missed), no Frozen day | 1 / 4 / 12 / 52 weeks | week |
| `on-ice` | On Ice | Consistency | A day the streak was kept on ice (the 0.18.1 cold state: a rest day that kept the streak without adding). `StreakResult` only reports it for today, so add a per-day "cold" mark to the timeline | once | snow |
| `weekend-warrior` | Weekend Warrior | Consistency | Workout logs on both the Saturday and the Sunday of one weekend | 1 / 5 / 15 / 52 weekends | weekend |
| `split-finisher` | Split Finisher | Consistency | A cycle of your active split done: every `SplitDay` of that split logged (`WorkoutLog.SplitDayId`) at least once since the previous finished cycle, in any order. Switching splits starts a new cycle | 1 / 5 / 20 / 50 cycles | grid (`IcGrid`) |
| `pr-breaker` | PR Breaker | Strength | PRs set: sets that beat the previous best of their exercise (`PrCalculator`); an exercise's first set isn't a PR | 1 / 10 / 50 / 100 | bolt |
| `heavy-lifter` | Heavy Lifter | Strength | Lifetime volume: sum of reps × weight of working sets (`SetLog`, not warm-ups) | 1,500 / 6,000 / 150,000 / 420,000 kg (car, elephant, blue whale, space station) | car / elephant / whale / station |
| `century` | Century | Strength | Workouts logged (`WorkoutLog` rows) | 10 / 50 / 100 / 500 | text "100" |
| `bodyweight-club` | Bodyweight Club | Strength | A working set whose load is at least a multiple of your body weight (the latest `WeightLog` on or before that date; no weigh-in yet, no badge). Use the total load as stored in `SetLog.WeightKg`; check how bar weight is stored (`BarWeight`) first | 1× / 1.5× / 2× / 2.5× | bodyweight |
| `rep-machine` | Rep Machine | Strength | Lifetime reps of working sets | 1,000 / 10,000 / 50,000 / 100,000 | reps |
| `exercise-explorer` | Exercise Explorer | Strength | Different exercises with at least one logged set | 10 / 25 / 50 / 100 | compass |
| `pr-week` | PR Week | Strength | 3 or more PRs (as for PR Breaker) in one Monday–Sunday week | 1 / 3 / 10 / 25 weeks | text "×3" |
| `fuel-streak` | Fuel Streak | Body & health | Longest run of days in a row with a `CalorieLog` whose `GoalAtTime` is set and `CalorieGoals.HitTarget(GoalAtTime, CaloriesConsumed)` | 7 / 14 / 30 / 90 days | fuel |
| `weigh-in-habit` | Weigh-in Habit | Body & health | Longest run of Monday–Sunday weeks in a row, each with a `WeightLog` (Health imports count) | 4 / 12 / 26 / 52 weeks | scale |
| `cardio-minutes` | Cardio Minutes | Body & health | Lifetime cardio minutes: `WorkoutLog.CardioMinutes` and saved `CardioSession` minutes, each minute counted once (check `CardioRules` for whether a session's minutes are copied onto the day's log) | 60 / 600 / 3,000 / 10,000 | cardio |
| `regular` | Regular | Social & gyms | 25 workout logs at the same gym | once | pin |
| `explorer` | Explorer | Social & gyms | Workout logs at 5 different gyms (OSM gyms by type and id, custom gyms by id) | once | map |
| `we-go-jim` | We Go Jim | Social & gyms | A workout log at an OSM gym on a date when a friend checked in at the same gym (`IFriendCheckInSource`, last 31 days). Counted once per date, whichever friends; custom gyms can't match. The server keeps only 31 days of check-ins, so the backfill sees at most that much and the stored count grows from there | 1 / 5 / 25 / 100 days | friends |
| `recruiter` | Recruiter | Social & gyms | Someone becomes your friend through your QR code or invite link (count from the server, 25d) | once | recruiter |
| `top-of-the-board` | Top of the Board | Social & gyms | You finished a Monday–Sunday week first on the weekly friends' leaderboard, with at least one friend who scored that week. A shared first place counts. Checked once per finished week (25d) | 1 / 5 / 10 / 25 weeks | trophy (`IcTrophy`) |
| `monthly-YYYY-MM` | Monthly Challenge | Social & gyms | That month's challenge, done within the month | once per month, limited | text "NOV" etc. |

**Hidden**

| Id | Name | Hint | Rule | Tiers | Glyph |
|---|---|---|---|---|---|
| `ill-be-back` | I'll Be Back | Everyone has a slow patch. | A workout log 15 or more days after the previous one (14+ days off) | once | comeback |
| `night-owl` | Night Owl | Trained while the city slept. | A live workout started 23:00–03:59 local time | once | moon |
| `eye-of-the-tiger` | Eye of the Tiger | Some people train before sunrise. | A live workout started 04:00–05:59 local time | once | sunrise |
| `double-up` | Double Up | One workout wasn't enough today. | Strength workout and cardio session on the same date (see defaults) | once | double |
| `marathon-session` | Marathon Session | Lose track of time at the gym. | A live workout of 2 hours or more, ended by the user (`AutoStop` null) | once | clock |
| `triple-digits` | Triple Digits | A big round number on the bar. | A working set at 100 kg or more | once | plate |
| `fresh-start` | Fresh Start | Start the year the right way. | A workout log on January 1 | once | sparkle |
| `first-of-month` | First of the Month! | New month, same habit. | A workout log on the 1st of February to December; each such 1st counts | 1 / 3 / 6 / 11 firsts | month1 |
| `birthday-gains` | Birthday Gains | Make a wish, then lift. | A workout log on your birthday (`UserProfile.BirthDate`; a February 29 birthday counts on February 28 in other years). No birth date, no badge | once | cake |
| `leap-day` | Leap Day | Rarer than a PR. | A workout log on February 29 | once | text "29" |
| `christmas-gains` | Christmas Gains | Some gifts come in plates. | A workout log on December 25 | once | tree |
| `full-house` | Full House | Nothing left on the list. | 10 workout logs where every planned exercise was filled (`FilledExercises == TotalExercises`, `TotalExercises > 0`) | once | checklist |
| `rest-day-never` | Rest Day? Never Heard of Her | Rest days are for other people. | Workout logs on 14 days in a row (the "never heard of her" meme) | once | text "14" |
| `this-is-fine` | This Is Fine | Everything is under control. | A missed training day paid back by a later workout, so the streak survived (a `Graced` day in the timeline; the burning-room dog meme) | once | mug |
| `touch-grass` | Touch Grass | Even lifters need a break. | A vacation (vacation mode) that ended, then a workout log within 3 days of the return date | once | grass |
| `completion` | 100% Completion | Collect every one in sight. | Every other visible badge earned at least once (any tier). Monthly Challenge doesn't count. Newer badges added later raise the bar, but an earned 100% Completion is kept | once | text "100%" |
| `light-weight-baby` | Light Weight, Baby! | Yeah buddy! | A working set at 150 kg or more (Ronnie Coleman's line) | once | text "150" |
| `never-skip-leg-day` | Never Skip Leg Day | Chicken legs need not apply. | Longest run of Monday–Sunday weeks in a row, each with a working set of a legs exercise (`MuscleGroup` Quads, Hamstrings, Glutes or Calves) | 4 / 8 / 26 / 52 weeks | leg |

## 25a. The rules in Core

- `GymApp.Core/Badges/`: `BadgeCatalog` (every badge: id, name, group, hidden, hint, tier thresholds, glyph key,
  material), `MonthlyChallenges` (the built-in month table) and a pure `BadgeEvaluator`.
- `BadgeEvaluator.Evaluate(BadgeInputs inputs, DateOnly today)` returns, per badge, the tier earned and
  the progress to the next step (current value, target). `BadgeInputs` is a plain snapshot: workout dates and gyms,
  sets, PR history, streak timeline, calorie logs, weight logs, live workouts (start, end, auto-stop), cardio
  dates and minutes, friend check-ins, split days and the active split's history, the birth date, the
  accepted-invite count and the weekly leaderboard results seen so far. No EF types in Core.
- Extend `StreakCalculator`'s timeline with the per-day cold mark On Ice needs; keep `Calculate`'s
  results unchanged (its tests must still pass).
- Tests in `tests/GymApp.Tests`: one class per group, each threshold on both sides, longest runs (Fuel Streak, Weigh-in Habit), January 1 vs the
  1st of other months, 23:59 / 04:00 / 06:00 boundaries, custom gyms not matching We Go Jim, kg vs lb display.

### 25a as built (2026-11-19)

- `src/GymApp.Core/Badges/`: `BadgeCatalog` (38 badges plus the monthly ones; `Find`, `TierName`, `GlyphFor`,
  `CompletionSet`), `MonthlyChallenges` (`FirstMonth` 2026-12, a target per calendar month, `monthly-YYYY-MM` ids),
  `BadgeInputs` and `BadgeEvaluator`. Namespace `GymApp.Core`, like the rest of Core.
- `BadgeEvaluator.Evaluate` returns a `BadgeProgress(Id, Earned, Tier, EarnedOn, Value, Target)` per badge. `Tier` is
  1–4 for tiered badges and 0 for one-time ones. `EarnedOn` is the day the current tier was reached when the data
  shows it; it is null for Streak (only the longest is known), Recruiter and 100% Completion, so 25b stamps today.
- Inputs 25b has to build: gym keys are `osm:<type>:<id>` or `custom:<id>` (`BadgeInputs.OsmGymKey`/`CustomGymKey`);
  live workout times are local; `BadgeCardio` lists every saved cardio session, with only the minutes the day's log
  doesn't already hold (Double Up uses the session, Cardio Minutes the minutes); `PreviouslyEarned` feeds 100%
  Completion.
- `StreakCalculator.ColdDays` lists the days the streak was kept on ice (rest days under the 0.18.1 rule and vacation
  days, with a running streak); the other methods are unchanged.
- Tests: `tests/GymApp.Tests/BadgeTests.cs` (catalog, one class per group, cold days). The kg/lb display of
  thresholds belongs with the screens (25c).

## 25b. Storage and the badge service

- App migration `Badges` (`GymApp.Data`): `EarnedBadge` (`UserId`, `BadgeId` max 40, `Tier`, `EarnedOn`,
  `SeenAt` null until shown; unique on user and badge) and on `UserProfile`: `FeaturedBadge` (string, null = automatic),
  `ShareBadges` (bool, default true). Regenerate the compiled model
  (`dotnet ef dbcontext optimize …`); `CompiledModelTests` must pass.
- `BadgeService` (`GymApp.Services`): builds `BadgeInputs` from the database, runs the evaluator and **only raises**
  stored tiers (decisions: never taken away). Runs after a workout, set, calorie, weight or cardio log
  changes, after Health sync, on foreground and when friend check-ins refresh. Raises `BadgesChanged` with the new
  unlocks (badge, new tier) for the UI.
- Top of the Board: on the first sync of a new week, read last week's board (25d) and store the result per week
  (week start, first or not) so a week is counted once, even across reinstalls of the cache.
- Backfill: when there are no `EarnedBadge` rows yet, store everything earned with `SeenAt` set, and flag the one
  "from your history" sheet.
- Tests on in-memory SQLite with the real migrations: raise-only, backfill, tier upgrades, deleting a log keeps the badge.

### 25b as built (2026-11-19)

- App migration `Badges`: `EarnedBadges` (unique on user and badge), `BadgeEvents` (unique on user, kind and date),
  `UserProfile.FeaturedBadge` / `ShareBadges` (default true), and on `User` `BadgesBackfilled` and
  `BadgeHistorySheetPending`. Compiled model regenerated.
- Backfill uses `User.BadgesBackfilled`, not "no rows yet": with the row rule, a user with no history would be
  backfilled again on every check, and their first real badge would come in silently.
- `BadgeEvents` keeps what can't be worked out again later: We Go Jim days (`we-go-jim`; friends' check-ins only live
  31 days on the server) and finished leaderboard weeks (`top-of-the-board`, stored once per week; 25d fills them
  through `RecordWeekResultAsync` / `HasWeekResultAsync`).
- `BadgeService` (Services): `CheckAsync` (stores, raise-only; returns the unlocks, or `Backfilled`), `GetProgressAsync`
  (the evaluator with the stored tiers on top, for the screens), `GetEarnedAsync`, `GetUnseenAsync`, `MarkSeenAsync`,
  `TakeHistorySheetAsync`. Friends' check-ins match by this phone's gym id (OSM gyms only).
- `BadgeSync` (App, singleton): one check at a time; raises `Unlocked` and `HistoryFound` for 25c. Triggers: sign-in
  and coming to the front (with friends' check-ins), every screen change, the sync hooks (`ReminderHooks`), and a new
  `HealthSync.Ran` event. It isn't gated to the phones yet; 25c adds `IPlatform.HasBadges`.
- Recruiter's invite count stays 0 until 25d.
- Tests: `tests/GymApp.Tests/BadgeServiceTests.cs`.

## 25c. Your badges on the phone

- `BadgesViewModel` / `BadgesView` (**Phone · Achievements**): earned count and bar, "Closest" next badge, an
  All / Earned / Locked toggle (`SegmentedPill`), the grid of badges with their tier. Tap a badge for a detail
  sheet: name, rule, tier ladder or Heavy Lifter levels, progress, earn date.
- `BadgeView` control: draws one badge at any size from the catalog (appendix): states earned (material by tier),
  locked, hidden. Colors from `GainStreakResources` tokens, light and dark.
- **Unlock sheet** (**Phone · Unlock moment**): new badge or tier, the tier pips, Share with friends (`IPlatform.Share`
  text) and Done. Several unlocks at once: one sheet, newest first, swipe through. Mark them `SeenAt`.
- **Profile** (**Profile · Your badges**): a Badges card under the header: count, "See all", the featured badge with
  Change, the last 4 earned, and the "Friends can see my badges" switch. The featured badge on the avatar's corner.
- **Picker** (**Profile · Pick a featured badge**): sheet with a live preview (map pin and leaderboard row), the earned
  badges plus None, "Feature <name>" / "Show no badge".
- TestBench admin window: "Award badge…" and "Reset badges", for testing the sheets.

### 25c as built (2026-11-19)

- `IPlatform.HasBadges` (default false; true in `PhonePlatform`; `DesktopPlatform.HasBadges` is false for the
  desktop and follows the TestBench's phone workout setting there).
- Core: `BadgeDisplay` (units, "61 t / 150 t", US tons in imperial, "Fire · 100 days").
- App: `BadgeView` (draws a badge from `Glyph` / `Text` / `Look`; the leg's shine twinkles on a light timer from
  40 px up; the glow is a soft radial halo), `BadgeViewModels.cs` (`BadgeItemViewModel`, `BadgesViewModel`,
  `BadgeDetailViewModel`, `BadgeUnlockViewModel`, `FeaturedBadgePickerViewModel`, `ProfileBadgesViewModel`) and the
  views `BadgesView`, `BadgeDetailView`, `BadgeUnlockView`, `FeaturedBadgePickerView`.
- `BadgeService` gained `GetSettingsAsync` (featured: the pick while still earned, else automatic; `"none"` = none),
  `SetFeaturedAsync`, `SetShareAsync`, and the TestBench's `AwardAsync` / `ResetAsync`.
- `MainViewModel`: `ShowBadges`, `OpenBadgeDetail`, `OpenFeaturedPicker`, and `PresentBadgeNewsAsync` (on badge events
  and every signed-in screen, when no popup is open: the history sheet first, then the unseen unlocks).
- Profile (phones, Main): the Badges card under the header and the featured badge on the avatar's corner.
- TestBench admin window: Badges section (Award, Check now, Reset).
- The reduced-motion setting isn't read yet (Avalonia doesn't expose it); the shine always twinkles.
- Tests: `BadgeDisplayTests`, `BadgeScreenTests`; `NavigationTests` lists the badges screen.

## 25d. Server and contracts

- `GymApp.Contracts/Badges.cs` (new):
  - `BadgeDto(string Id, int Tier, DateOnly EarnedOn)` (`EarnedOn` is when the current tier was reached)
  - `BadgesUploadRequest(IReadOnlyList<BadgeDto> Badges, string? Featured, bool Share)`: replaces everything the
    server holds for you. At most 200 badges, no id twice, ids max 40 chars, `Featured` must be one of them or null.
  - `UserBadgesResponse(int UserId, IReadOnlyList<BadgeDto> Badges, string? Featured)`
- Optional trailing `string? FeaturedBadge = null` on `LeaderboardRowResponse` and `FriendCheckInResponse`, as
  `"<id>:<tier>"` (`"streak:3"`). Old clients ignore it; follow the `Avatar` precedent.
- Endpoints: `PUT /v1/me/badges`, `GET /v1/friends/{userId}/badges` (accepted friends only; not a friend → 404;
  sharing off → empty list and null featured). Leaderboard and friend check-ins fill `FeaturedBadge` only when the
  owner shares.
- **Recruiter:** when a friendship comes from an invite (QR or link), record who invited whom (`InvitedByUserId` on
  the friendship, or on the request the invite created). `GET /v1/me/invites/accepted` returns a `CountResponse`.
- **Top of the Board:** add `LeaderboardPeriod.LastWeek` (the finished week before the viewer's current one). Old
  clients never send it, so the enum change is safe; add a contract test that they still parse every response.
- Server migration `Badges`: `UserBadges` (user, badge id, tier, earned on; key user + badge) and on users
  `FeaturedBadge`, `ShareBadges` (default true). Production runs `migrate` before the deploy.
- `GymApp.Social`: `IBadgeShareService` (upload yours, read a friend's). `Remote*` over `SocialApiClient` with the
  offline cache, `Local*` for the TestBench's local mode; register both branches in `AppServices.Create`. `SocialSync`
  uploads badges with the stats; uploads wait while offline.
- Tests: endpoint tests (validation, friends only, sharing off, featured on leaderboard and check-ins),
  `ClientContractTests` / `ClientVersionTests` still pass, an end-to-end test with two `Phone`s.
- Run the `server-compat-reviewer` agent on the changes before the next server deploy.

### 25d as built (2026-11-19)

- Contracts: `Badges.cs`; `LeaderboardPeriod.LastWeek`; `FeaturedBadge` (`"<id>:<tier>"`) as an optional last
  parameter on `LeaderboardRowResponse` and `FriendCheckInResponse`. Core `FeaturedBadge.Format` / `Parse`.
- Server: `UserBadges`, `InviteAccepts` (one row per inviter and invitee, written by `POST /v1/invites/{code}/accept`;
  kept if they unfriend), `Users.FeaturedBadge` / `ShareBadges`; migration `Badges`. `BadgeEndpoints` serves the three
  routes. The featured badge reaches leaderboard rows through `PublicStats` (both stores) and friend check-ins
  directly; both are null while the owner doesn't share.
- **Last week's board:** a stats upload now replaces the days from its first day on and keeps the days before it, for
  21 days (`StatsEndpoints.HistoryDays`). `LeaderboardService` reads `LastWeek` as each user's latest row up to the
  Sunday before this week; the stats refresh (the TestBench's local store) still runs for the real today. Old clients
  upload the same way and see the same boards.
- Phone: `IBadgeShareService` (`RemoteBadgeShareService` with a per-friend cache, `LocalBadgeShareService` for the
  TestBench), registered in both branches. `SocialSync` uploads the badges right after the stats (no new sync flag);
  `BadgeSync.MarkChanged` (new badges, featured, sharing) marks the stats. On sign-in and coming to the front,
  `BadgeSync` also stores Recruiter's invite count (`BadgeService.RecordAcceptedInvitesAsync`, highest seen) and, once
  per finished week, last week's result for Top of the Board.
- Tests: server `BadgeEndpointTests`, `BadgeEndToEndTests` (two `Phone`s); `StatsEndpointTests` updated for the kept
  days; app `BadgeServerDataTests`; `FakeSocialServer` answers `PUT v1/me/badges`.
- Deploy: run `migrate` before deploying the server.

## 25e. Friends' badges, map and leaderboard

- **A friend's profile** (**Profile · A friend's badges**): open from the friends list, a leaderboard row or a map pin.
  Header with their featured badge, "<name>'s badges 12 / 34", "N in common", an All / Both have / Only <name> toggle,
  the grid with notes ("You: Lime", "Not yet for you"); hidden ones you haven't earned masked.
  Offline: the cached copy with the usual offline note.
- **Map pins** (**Map · Featured badge on pins**): the featured badge on the pin's avatar corner; the friend card shows
  "Featured badge · <name, level>" and a Badges link.
- **Leaderboard** (**Leaderboard · Featured badge**): the featured badge after the name; rows open the friend's profile.

### 25e as built (2026-11-20)

- `FriendBadgesViewModel` / `FriendBadgesView` (`MainViewModel.ShowFriendBadges`, under the Friends tab): the friend's
  avatar with their featured badge, "<name>'s badges N / 39", "N in common", All / Both have / Only <name>, and the grid
  with notes: your tier for a tiered badge you have ("You: Lime", also when it's the same as theirs), "You:" with a
  green check for a one-time badge you have, "You:" with a red cross for one you don't (tiered or not), and "Earn it
  to see it" for a hidden one you haven't earned, drawn as the "?". The third option reads "Only them" for names
  longer than 8 characters. Opened from the map (a check-in has no level or avatar), the screen takes the friend's
  level ring and avatar from their all-time leaderboard row. A tile opens the usual detail sheet. Offline: the cached copy and "Offline: showing what was last
  fetched."; nothing cached: "Couldn't load <name>'s badges…".
- Leaderboard (phones): the featured badge after the name; a row opens the friend's badges (your own row: your
  badges). The friends list: the name opens their badges, with the featured badge beside it.
- Map: pins stay plain (an earlier badge on the pin was dropped on 2026-11-19, along with `BadgeSvg`); tapping a pin
  opens the gym's card with each friend's featured badge and a Badges link.
- All of it only with `IPlatform.HasBadges`; on the desktop the rows look and behave as before (their buttons do
  nothing there rather than being disabled, which would grey them out).
- `FeaturedLook.From("<id>:<tier>")` turns the wire value into what to draw; `BadgeArt` holds the colours and glyph
  paths that `BadgeView` and `BadgeSvg` share.
- Tests: `FriendBadgesTests`; `NavigationTests` lists the new screen under Friends.

## 25f. Release

- `CHANGELOG.md` entry for 0.25.0 in the existing style, ending with `**Migration:**` naming both databases.
- Version `0.25.0` in `Directory.Build.props`.
- End-of-phase checks from CLAUDE.md: full `dotnet test` (Docker), the Android build, the iOS Release build for the
  Simulator, and running both phone heads through: backfill sheet, a live unlock, the picker, a friend's profile, map
  and leaderboard with a featured badge. The desktop only needs to build and start, with no badge UI.
- Add the real-phone checks for badges to `docs/REAL_PHONE_CHECKS.md` (local file, never committed).

### 25f as built (2026-11-19)

- Version 0.25.0; `CHANGELOG.md` entry; real-phone checks for badges added (local file).
- Checks: full `dotnet test` (309 server, 1866 app), Android Debug build and install, iOS Release build for the
  Simulator (starts and creates its database with the Badges tables under full AOT; a Release build only takes an
  https server, so the badge walkthrough ran on the Debug build against the local server), desktop builds and starts.
- Walked through on the iPhone Simulator: the history sheet, two unlocks (one secret) swiped through, the picker
  (the featured badge reaches the server), a friend's badges from the leaderboard and the map, featured badges on
  leaderboard rows and the map pin. On the Android emulator: the leaderboard with a featured badge and a friend's
  badges (the map pin wasn't checked there: the test account has no home city).

## Later (not this phase)

- High five on a friend's new tier (the card on **Profile · A friend's badges**) and a push when a friend reaches one.
- Monthly challenges set from the server, with more kinds than a workout count.
- A badge feed in the inbox.
- Points for badges, if the default above changes.

## Appendix: drawing a badge

On a 120 × 120 grid. Outer shape filled and stroked (width 8, round joins) with the material's gradient (top-left to
bottom-right, three stops); inner shape filled with the core color; the glyph is a 24-grid stroke icon scaled 1.6 at
(40.8, 40.8), stroke 2, round caps and joins, in the same gradient; text badges use mono bold 25 centered. Earned
lime and up get a soft glow (drop shadow, blur ≈ 10% of the size, the material's glow color). Sizes: 160 unlock,
120 list, 72 phone grid, 40 feed and profile, 24 inline (pins and rows use 22–34).

- Hexagon outer `M60 6L106.8 33V87L60 114L13.2 87V33Z`, inner `M60 18L96.4 39V81L60 102L23.6 81V39Z`.
- Round (Monthly only) outer `M6 60a54 54 0 1 0 108 0a54 54 0 1 0-108 0Z`, inner `M18 60a42 42 0 1 0 84 0a42 42 0 1 0-84 0Z`.

| Material | Gradient | Core | Glow |
|---|---|---|---|
| Steel | #4A4A4A, #D6D6D6, #6E6E6E | #0B0B0B | none |
| Lime | #8DB51E, #C6F432, #E2FF8A | #0B0B0B | rgba(198,244,50,.22) |
| Fire | #C81D00, #FF4A00, #FFB000 | #0B0B0B | rgba(255,74,0,.35) |
| Blaze | #FF7A00, #FFD400, #FFF6B0 | #120C02 | rgba(255,212,0,.45) |
| Ice | #1F6FD1, #5CB8FF, #D4F0FF | #06101A | rgba(92,184,255,.3) |
| Limited | #C6F432, #FFD400, #FF4A00 | #0B0B0B | rgba(255,154,0,.3) |
| Locked | #2A2A2A, #3A3A3A, #2A2A2A; glyph #5A5A5A | #0B0B0B | none |
| Hidden | #161616, #222222, #161616; inner line #4A4A4A dashed 4 4; "?" #9A9A9A | #080808 | none |

Glyphs not already in `GainStreakResources` (`IcFlame`, `IcPin`, `IcMap`, `IcFriends`, `IcMadeUp` = I'll Be Back,
`IcClock`, `IcGrid`, `IcTrophy` are there):

```
week      M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M9 15l2 2 4-4
snow      M12 2v20M3.4 7l17.2 10M3.4 17L20.6 7M9.5 3.6L12 5.6l2.5-2M9.5 20.4L12 18.4l2.5 2M3.6 10.4l3.1-.4-.9-3M20.4 13.6l-3.1.4.9 3M3.6 13.6l3.1.4-.9 3M20.4 10.4l-3.1-.4.9-3
bolt      M13 2L4 14h7l-1 8 9-12h-7z
fuel      M12 7.5c-1-1.8-3-2.8-5-2-3 1.2-4 5-3 9 1 4 4 6.5 6 5.5 1-.4 1.3-.5 2-.5s1 .1 2 .5c2 1 5-1.5 6-5.5 1-4 0-7.8-3-9-2-.8-4 .2-5 2zM12 7.5c0-2 1-4 3-5
scale     M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM8 11a4 4 0 0 1 8 0M12 11l1.6-2.2
star      M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z
car       M3 17v-5l3-1 3-4h6l4 4h2v6M5 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0M15 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0M9 17h6
elephant  M21 15v-5a4 4 0 0 0-4-4h-7a5.5 5.5 0 0 0-5.5 5.5V19H6M9 14v5h2.5v-3h5v3H19v-4M21 11l1.5 2.5M9 7.5c-2 1-2 4 1 5M7 10.5h.01
whale     M2 12c0 4 4 7 10 7 5 0 8-2 9-5l1-4-3 1c-1-3-4-5-8-5-5 0-9 2-9 6zM7 11h.01M11 6c0-1.5 1-2.5 2-3M11 6c0-1.5-1-2.5-2-3
station   M2 6h5v12H2zM17 6h5v12h-5zM7 12h10M10 9h4v6h-4zM2 10h5M2 14h5M17 10h5M17 14h5
moon      M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z
sunrise   M4 18h16M7 18a5 5 0 0 1 10 0M12 6v3M5.6 10.6l1.6 1.6M18.4 10.6l-1.6 1.6M2 21h20
double    M4 6v6M10 6v6M4 9h6M14 12v6M20 12v6M14 15h6
plate     M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0
sparkle   M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M5.6 18.4l2.8-2.8M15.6 8.4l2.8-2.8
month1    M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M10.5 13.5l2-1.5v6
weekend   M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M14 13h4v4h-4z
bodyweight M3 4h18M6 2v4M18 2v4M10 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0M8 4l3 4M16 4l-3 4M12 11v5M9 22l3-6 3 6
reps      M17 2l3 3-3 3M4 11V9a4 4 0 0 1 4-4h12M7 22l-3-3 3-3M20 13v2a4 4 0 0 1-4 4H4
compass   M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M15.5 8.5l-2 5-5 2 2-5z
cardio    M20.8 5.6a5 5 0 0 0-7.1 0L12 7.3l-1.7-1.7a5 5 0 0 0-7.1 7.1L12 21.5l8.8-8.8a5 5 0 0 0 0-7.1zM3 12h4l2-3 3 6 2-3h7
recruiter M4 21c0-4 3-7 7-7M7 8a4 4 0 1 0 8 0a4 4 0 1 0-8 0M18 14v6M15 17h6
cake      M4 21h16M5 21v-8h14v8M5 16c2 1.5 4.5 1.5 7 0s5-1.5 7 0M12 13V9M12 6.5c-1-1-1-2 0-3.5 1 1.5 1 2.5 0 3.5z
tree      M12 2l5 7h-3l5 7H5l5-7H7zM12 16v5
checklist M9 6h11M9 12h11M9 18h11M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2
leg       M7.5 3.5C11 3.6 14.5 5.5 16.5 9C17.3 10.3 17 11.3 16.2 12.2C15.6 14.5 15.2 16.5 15.2 18.6C16.8 19.4 18.6 19.6 19.6 20.4C19.8 21 19.4 21.4 18.8 21.4H12.6C12 21.4 11.8 20.8 12 20.2C12.2 19 11.6 17.8 10.9 16C10.2 14.2 10.6 12.6 11.8 11.6C10 10.6 8 9.6 6.6 8.2C5.6 7.2 5.8 3.6 7.5 3.5Z
leg-detail M9 5.6C11.5 6.5 13.6 8 14.8 10M14.8 10.2C15.4 11 15.4 12 14.8 12.8M12.6 13.4C13.4 14.8 13.4 16.6 12.8 18.2M17 21.4v-.9M18.2 21.4v-.7
leg-shine M10 1.2l.4 1.2M13.6 1.9l-.3 1.2M17.4 4.4l-1 .8M19.8 9.4l-1.2.3M19.9 12.6l-1.2-.4M8.4 15l1 .3M8.3 12.4l1.1.5
mug       M4 9h12v6a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5zM16 11h2a2 2 0 0 1 0 4h-2M8 2c-1 1.5 1 2.5 0 4M12 2c-1 1.5 1 2.5 0 4
grass     M2 21h20M5 21c0-4-1-7-3-9M9 21c0-5 1-9 4-12M14 21c0-3 1-6 4-8M19 21c0-2 0-4 2-6
```

Never Skip Leg Day is the one glyph with extra layers: `leg` is the outline of a bent leg (thigh up to the left, knee,
calf, foot with toes; stroke 2, like every glyph), `leg-detail` draws the quad, knee and calf lines and the toes in
the same gradient at stroke 1.1, and `leg-shine` is the shine: short white lines just outside the thigh, knee and calf,
stroke 1.2, twinkling (opacity 0.25 to 1, 2.4 s loop). The shine
doesn't move when the system asks for reduced motion, and is left out below 40 px.

These new glyphs are drafts made for this spec and aren't on the canvas yet: check them at 72 and 24 px and adjust.

Tier text colors: Steel #D6D6D6, Lime #C6F432 (light theme `AccentInk`), Fire #FFB066 (`FlameText`), Blaze #FFD400,
Ice #8FD0FF (`IceText`), Limited #FFD400, Locked and Hidden #9A9A9A (`Muted`).
