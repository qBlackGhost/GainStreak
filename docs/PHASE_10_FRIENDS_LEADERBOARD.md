# Phase 10: Friends and leaderboard — spec for Claude Code

This is the spec for GymApp Phase 10: a friends system (requests, accept, remove) and a points leaderboard that shows only you and your friends, with each person's streak next to their name. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added
- Follow the existing project layout:
  - **GymApp.Core**: pure logic, no EF and no HTTP. Unit-tested.
  - **GymApp.Data**: entities, `AppDbContext`, migrations.
  - **GymApp.Services**: services, EF access, HTTP clients.
  - **GymApp.Desktop**: Avalonia views and view models only, using CommunityToolkit.Mvvm.
  - **GymApp.Tests**: xUnit. Service tests run against in-memory SQLite with the real migrations (`TestDb`).
- Read `User`, `PointsEntry`, `PointsService`, `StreakService`, `AuthService`, `AppDbContext` and `MainViewModel` before changing anything. Field names below are proposals, so match the existing naming style.
- Seeded ids are permanent. Don't touch existing seed data.
- Points and streak rules don't change in this phase. The leaderboard only reads `PointsEntry`, `User.Points` and `User.CurrentStreak`.

## Decisions already made (don't change these)

1. **The leaderboard is friends only.** It shows you and your accepted friends, never all users. There is no global leaderboard.
2. **The streak is shown on each row, next to the user.** It isn't a separate tab and isn't used to rank.
3. **Friendship is mutual.** A request has to be accepted before either user sees the other on a leaderboard. Either side can remove the friendship at any time.
4. **Only public stats are shared:** username, level, points and current streak. Body data, weight, calories, sets, PRs, gyms and schedules are never shown to friends.
5. **No points for social actions.** Adding friends or ranking high earns nothing.

6. **Local first, maybe a server later.** 10a–10e build everything against the existing local SQLite database through an `ISocialStore` interface, so friends work between accounts on the same install (and in tests). Friends on different computers need the optional sync server in 10f. It may be added later and is out of scope for now. Keep `ISocialStore` free of EF types so a server-backed store can replace it without changes to the services or UI.

## Defaults to confirm (change here if wrong)

- **Ranking:** points earned **this week** (Monday to Sunday), with a **This week / All time** toggle. All time ranks by `User.Points`.
- **Adding a friend:** search by username, send a request, the other user accepts or declines.
- **Limits:** at most 50 friends and 20 pending outgoing requests per user.

---

## 10a. Core: leaderboard ranking

**`Leaderboard`** in Core, pure and static like `GymSummary`.

**Input:** one row per person (user id, username, points for the chosen period, total points, current streak), plus the viewing user's id.

**Output:** the rows in rank order, each with:
- rank
- username
- points for the period
- level (from `LevelCalculator.For(totalPoints)`, so the level is always the all-time level, even on the weekly board)
- current streak
- `IsYou`

**Ranking rules:**
- Sort by period points, highest first.
- **Ties share a rank** ("1, 1, 3"). A tie is on points only. Streak doesn't break ties.
- Within a tie, order by username (case-insensitive) so the list doesn't jump around between refreshes.
- The viewing user is always included, even with 0 points and no friends.

**Week window:** add a helper (or reuse `PointsCalculator.MondayOf`) that gives Monday to Sunday for a date. Weekly points are the sum of `PointsEntry.Amount` with `Date` inside that range.
- A WeekComplete +50 is dated the day of the last training log, and a WeekCompleteGrace +25 is dated that week's Sunday. Both land in the week they belong to, so nothing special is needed.

**Tests (`LeaderboardTests`):**
- order by points
- shared ranks for ties, and the rank after a tie skips ("1, 1, 3")
- username order within a tie, ignoring case
- the viewer alone, with 0 points
- the level comes from total points, not from period points
- the week range for a Monday, a Sunday and a mid-week date (2026-10-05 is a Monday)

---

## 10b. Data: Friendship entity and migration

**New entity `Friendship`.** One row per pair of users, whatever its state.

| Field | Details |
| --- | --- |
| Id | |
| UserAId, UserBId | The two users, **always stored with UserAId < UserBId**, so a pair can only exist once. |
| RequestedById | Which of the two sent the request. |
| Status | `Pending` or `Accepted`, stored as text. |
| CreatedAt | When the request was sent (UTC). |
| AcceptedAt | Nullable. |

- A unique index on (UserAId, UserBId).
- Indexes on UserAId and UserBId for the "my friends" lookups.
- Cascade delete from both users, so deleting an account removes its friendships.
- A check constraint `UserAId < UserBId`, if the existing migrations already use check constraints. Otherwise enforce it in the service.
- **Declining, cancelling and removing delete the row.** No "declined" state is stored, so the users can send a new request later.

Add one migration, `Friends`.

**Done when:**
- the migration applies cleanly to an existing database
- the existing tests still pass

---

## 10c. Services: FriendService

**`ISocialStore`:** a small interface in Services holding the reads and writes 10c and 10d need (search users, friendship rows, period points, streaks). `LocalSocialStore` implements it with `AppDbContext`. The services depend on the interface, so 10f can swap in a server-backed store without touching the UI.

**`FriendService`** (all methods take the current user's id):

| Method | Behaviour |
| --- | --- |
| `SearchAsync(userId, query)` | Users whose username starts with the query, ignoring case. At least 3 characters, at most 20 results, the current user left out. Each result says its relationship to you: none, friend, request sent, or request received. |
| `SendRequestAsync(userId, otherId)` | Creates a Pending row. **If the other user already sent you a request, it accepts that one instead.** Fails if you're already friends or a request is pending, if it's yourself, or if a limit would be exceeded. |
| `AcceptAsync(userId, otherId)` | Only the user who **received** the request can accept it. Sets Accepted and AcceptedAt. Checks the 50-friend limit for both users. |
| `DeclineAsync(userId, otherId)` | The receiver deletes the pending row. The sender isn't notified. Their request simply disappears. |
| `CancelAsync(userId, otherId)` | The sender deletes their own pending request. |
| `RemoveAsync(userId, otherId)` | Either friend deletes an Accepted row. |
| `GetFriendsAsync(userId)` | Accepted friends, ordered by username. |
| `GetRequestsAsync(userId)` | Incoming and outgoing pending requests, newest first. |
| `IncomingCountAsync(userId)` | For the badge on Home. |

- Results use the same `Result(bool Success, string? Error)` style as `AuthResult`, with errors the UI can show as they are ("You're already friends.", "You can have at most 50 friends.").
- Write methods run in a transaction so two quick clicks can't create two rows. The unique index is the safety net. Catch `DbUpdateException` the same way `AuthService.RegisterAsync` does.

**Tests (`FriendServiceTests`):**
- send, accept, then both sides see each other as friends
- the pair stored once with A < B, whichever user sent the request
- sending to someone who already sent you a request accepts it
- the sender can't accept their own request
- decline and cancel delete the row, and a new request works afterwards
- remove by either side
- no requests to yourself, no duplicates, and the friend and pending limits
- search: the prefix match ignoring case, yourself left out, the minimum length, and the relationship states
- deleting a user removes their friendships

---

## 10d. Services: LeaderboardService

**`LeaderboardService.GetAsync(userId, period, today)`**, where `period` is `ThisWeek` or `AllTime`:
1. Load the current user plus their **accepted** friends. Pending requests never appear.
2. For each person, load:
   - total points (`User.Points`)
   - period points: the sum of `PointsEntry` in the week range for ThisWeek, or `User.Points` for AllTime
   - the current streak
3. Pass the rows to `Leaderboard` in Core and return the result.

**Keeping friends' numbers current.**
- `User.Points` and `CurrentStreak` are caches that only update when that user's hooks run, and a streak can break at midnight without anything being logged.
- In local mode, run `StreakService.RecalculateAsync` and `PointsService.SyncAsync` for each friend before reading, so a friend who hasn't opened the app since yesterday shows the right streak.
- Both calls are idempotent, so this is safe. With at most 50 friends it's fast enough. Measure it, and report back if it isn't.

**Tests (`LeaderboardServiceTests`):**
- only you and your accepted friends appear, never pending requests or strangers
- weekly points count only this week's entries (entries from last Sunday and next Monday are left out)
- all time uses `User.Points`
- a friend's broken streak shows 0 even though their cached value was stale
- removing a friend takes them off the board

---

## 10e. Desktop UI

**Friends screen**, opened from Home. Add it to the `MainViewModel` navigation the same way the other screens are added. It has three sections:
- **Friends:** the list, each with a **Remove** button and a confirmation.
- **Requests:** incoming requests with **Accept** and **Decline**, and outgoing ones with **Cancel**.
- **Add friend:** a username search box (search on button press or Enter, like the city search). Each result shows **Add**, **Requested**, **Accept** or **Friends** depending on its state.

**Leaderboard screen**, opened from Home and from the Friends screen:
- a **This week / All time** toggle
- one row per person: rank, username, level name, points for the period, and the **streak with a flame icon** next to it (for example "🔥 12")
- your own row is highlighted
- **empty state** (you're the only person on it): "Add friends to compete", with a link to the Friends screen
- a "Week of 12–18 Oct" line on the weekly view

**Home:**
- a small **Friends** card: your weekly rank ("#2 of 5 this week"), and a badge with the number of incoming requests
- with no friends yet, the card just says "Add friends"

The view models only read results from `FriendService` and `LeaderboardService`. No ranking logic lives in view models.

**Done when:**
- two local accounts can send, accept and remove a friend request
- the board shows both of them with points and streaks
- the toggle switches the ranking between this week and all time
- the empty state shows for a user with no friends

---

## 10f. Optional, later: sync server

**Not part of the current work. Don't start it until it's asked for.** The goal is friends on different computers. When it's picked up, treat it as its own design step: write the proposal and stop for review before writing code.

**Outline to work from:**
- A new **GymApp.Server** project: an ASP.NET Core minimal API with its own database (SQLite to start). It stores only server accounts, friendships, and each user's **public snapshot**: username, total points, this week's points, week start, current streak, level, and an "as of" timestamp.
- **Accounts:** a local user links to a server account, either by registering on the server or by signing in with an existing one. Use the same rules as `AuthService` and the same PBKDF2 hashing. The server issues a token, which the app stores hashed, like the remember-me token.
- **Publishing:** after each `PointsService.SyncAsync` and `StreakService.RecalculateAsync`, the app uploads its snapshot. Workouts, calorie logs and body data stay local, so decision 4 still holds.
- **Stale streaks:** the server can't replay a friend's logs, so a snapshot older than a day shows its streak with a "last synced" hint instead of recomputing it.
- **`RemoteSocialStore`** implements `ISocialStore` over HTTP, with timeouts and errors returned as results (like `GeocodingService`). The leaderboard shows the last cached result when offline.
- **Points can't be verified** server-side, because they're computed on the client. That's acceptable between friends, but write it down as a known limitation.
- Hosting, HTTPS and rate limits need deciding before 10f starts. The leading option is the Azure App Service free tier (F1) with SQLite. If SQLite's data limits get in the way, move to a free hosted Postgres.

---

## Done when

- [x] `Leaderboard` ranking in GymApp.Core, with tests
- [x] `Friendship` entity and the `Friends` migration applied
- [x] `FriendService` and `LeaderboardService` behind `ISocialStore`, with tests
- [x] Friends screen, Leaderboard screen and the Home friends card
- [x] The streak shown on every leaderboard row; the board shows friends only
- [x] `dotnet build` clean and all tests pass
- [x] List of created and modified files reported, nothing committed or pushed
- 10f (sync server) is optional and later, not part of this checklist

---

## Known issues (to look at later)

### Inactive users' streaks flip between 1 and 0

Found while testing 10d. It comes from the existing `StreakCalculator` rules, not from Phase 10, and this phase didn't change them.

**What happens:** once a streak breaks, the next unlogged training day is counted provisionally (+1, leaving a debt), and the day after that breaks it again. So a user who stopped logging shows a current streak of 1 and 0 on alternating days instead of staying at 0. On the leaderboard, a friend who hasn't trained in a week can show 🔥 1.

**Example (no schedule, so every day is a training day):** the last workout is on 1 Oct, so the streak breaks on 3 Oct. Reading it on 11 Oct gives a current streak of 1 with status `AtRisk`. Reading it on 10 Oct gives 0.

**Options when it's picked up:**
- Change `StreakCalculator` so a provisional day doesn't count while no workout has been logged since the last break. This changes Home too, and needs its own tests in `StreakCalculatorTests`.
- Or fix it only for display: show 0 when the status is `AtRisk` and nothing has been logged since the break. This means `PublicStats` would have to carry the status or the last workout date.

`LeaderboardServiceTests.FriendsBrokenStreak_ShowsZero_EvenWithAStaleCache` uses a date where the result is 0 either way, so it will still pass after either fix.
