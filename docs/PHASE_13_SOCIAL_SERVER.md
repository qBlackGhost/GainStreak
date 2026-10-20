# Phase 13: Social server — spec for Claude Code

This is the spec for GymApp Phase 13: a server that holds accounts, friendships, public stats and shared gym check-ins, so friends work between different phones. This phase builds **the server only**. Pointing the app at it (login screens, remote store, offline cache) is Phase 14. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added
- Project layout, existing and new:
  - **GymApp.Core**: pure logic, no EF and no HTTP. Unit-tested. Shared by the app and the server.
  - **GymApp.Social** (new, 13a): the friend and leaderboard rules plus the `ISocialStore` interface, with no EF. Shared by the app and the server.
  - **GymApp.Contracts** (new, 13c): request and response records for the HTTP API, nothing else. Shared by the server and (in Phase 14) the app.
  - **GymApp.Server** (new, 13c): ASP.NET Core minimal API, its own `ServerDbContext` on Postgres, its own migrations. References Core, Social and Contracts, **never GymApp.Data or GymApp.Services**.
  - **GymApp.Server.Tests** (new, 13c): xUnit. Endpoint tests through `WebApplicationFactory` against a real Postgres from Testcontainers (Docker is installed).
- Read `FriendService`, `SocialStore` (`ISocialStore`, `LocalSocialStore`), `LeaderboardService`, `FriendMapService`, `FriendMapRules`, `StreakCalculator`, `PointsCalculator`, `Credentials`, `PasswordHasher` and `FriendServiceTests` before changing anything.
- The app's behaviour doesn't change in this phase. Every existing test must still pass after every sub-phase.

## Decisions already made (don't change these)

1. **Mobile is the main target.** One phone per person, so the phone's local database stays the source of truth for workouts. **The server holds social data only**: accounts, friendships, a public stats snapshot and shared gym check-ins. No workout sync in this phase.
2. **Accounts live on the server.** The server account is the real login and the local `User` row becomes its cache (Phase 14). This keeps full sync possible later without migrating accounts.
3. **Server user ids are ints** issued by the server's own sequence. Users are only ever created on the server, so ints are globally unique and `FriendService` keeps working unchanged. (Guids would only be needed for rows created offline on several devices, i.e. workout sync.)
4. **The friend rules run on the server.** A client can call the API directly, so limits and "only the receiver can accept" can't be trusted to the app. The server reuses the same `FriendService` (moved to GymApp.Social in 13a), so there is one copy of the rules and one test suite.
5. **The phone computes points and streaks; the server only stores them.** Points can be forged by a modified client. For a friends-only leaderboard that's accepted for now.
6. **Privacy rules from Phase 10 and the gym map carry over:** only username, points, level and streak are public. Check-ins are gym and day, never a time, only when `ShareGymCheckIns == true`, last 30 days only, and a custom gym never comes with coordinates. **The server enforces these too**, not just the app.
7. **Accounts can be deleted from inside the app.** The App Store requires it for apps that create accounts.

## Defaults to confirm (change here if wrong)

- **Login tokens:** opaque random session tokens (32 bytes, base64url), stored **hashed** (SHA-256) in a `Sessions` table, sent as `Authorization: Bearer <token>`. Sliding expiry: 90 days since last use. Simpler than JWT plus refresh tokens, and a session can be revoked at once (logout, password change, account deletion). Same idea as the existing `RememberMeService`.
- **Login with username or email**, case-insensitive. Usernames are unique ignoring case.
- **Passwords:** `PasswordHasher` and `Credentials` from Core, so the rules match the app.
- **Rate limits** (ASP.NET Core's built-in limiter, per IP): register 5/hour, login 10/minute, search 30/minute. Everything else 120/minute per user.
- **Errors:** `ProblemDetails` with the user-facing message from the service in `detail` (e.g. "You already sent a request."). 400 for a broken rule, 401 for no or bad token, 404 for an unknown user, 409 for a conflict (`SocialStoreConflictException`).
- **API prefix `/v1`.** JSON with camelCase names; dates as `yyyy-MM-dd`.
- **Stats horizon:** 14 days of projected stats per upload (see 13b).
- **Out of scope:** password reset by email, email verification, push notifications, admin tools, real hosting. These are listed under "Later" at the end.

---

## 13a. Move the friend rules into GymApp.Social

A refactor with no behaviour change, so the server can reuse the rules.

- New class library **GymApp.Social** (net10.0), referencing **only** GymApp.Core.
- Move these files into it, keeping their namespaces' style (`GymApp.Social`):
  - `FriendService` and its records (`FriendResult`, `FriendRelation`, `FoundUser`, `FriendSearchResult`, `Friend`, `FriendRequest`, `FriendRequests`)
  - from `SocialStore.cs`: `SocialUser`, `FriendshipRecord`, `FriendLink`, `PublicStats`, `SocialStoreConflictException`, `ISocialStore`, `ISocialSession`
  - `LeaderboardService` and `LeaderboardView`
- `LocalSocialStore` stays in GymApp.Services (it uses EF), now implementing the interface from GymApp.Social. `FriendMapService` stays in Services as well.
- GymApp.Services references GymApp.Social. Fix usings in Services, Desktop, TestBench and Tests.
- **One interface change:** `GetPublicStatsAsync` also takes `DateOnly today`. `LocalSocialStore` ignores it (it already refreshed). The server needs it to pick the right projected day (13f).

**Tests:** all existing tests pass unchanged apart from usings and the extra argument.

---

## 13b. Core: projected public stats

The server can't replay a friend's logs, but a streak can break, and grow on rest days, without the friend opening the app. So the phone uploads what its stats **will be** on each of the next days if nothing new is logged. The server shows the entry for today.

**`StatsProjection`** in Core, pure and static:

```csharp
public sealed record ProjectedStats(DateOnly Date, int TotalPoints, int WeekPoints, int CurrentStreak);

public static IReadOnlyList<ProjectedStats> Project(
    IEnumerable<DateOnly> workoutDates,
    IReadOnlyList<ScheduleChange> schedule,
    IReadOnlyDictionary<DateOnly, MissChoice> choices,
    DateOnly? scheduleStart,
    IEnumerable<CalorieRecord> calories,
    IReadOnlyList<PointsAward> currentAwards,   // the ledger as it stands today
    DateOnly today,
    int days = 14);
```

- One entry per day from `today` to `today + days - 1`.
- Each day runs `StreakCalculator.Calculate` and the `PointsCalculator` pipeline **with that day as "today"** and the same inputs, so no workouts are added. That covers rest days growing the streak, a missed day breaking it, and week bonuses paid at a week's end.
- `WeekPoints` is for the Monday-to-Sunday week containing that entry's date (`PointsCalculator.MondayOf`).
- Pure: it never writes anything, and future-dated awards are only used inside the projection.

**Tests** (in GymApp.Tests, like the other calculator tests):
- Day 0 equals the current streak and points from `StreakService` and `PointsService` for the same data.
- A rest day adds 1 to the streak; a missed training day with no log the next day drops it to 0 on the following entry.
- A missed day with a stored `ShiftWeek` or `Continue` choice projects like the calculator says.
- `WeekPoints` resets to 0 on the first Monday after a week with points, while `TotalPoints` doesn't drop.
- With no schedule, every day is a training day, so the streak breaks after the grace day.

---

## 13c. Server skeleton

- **GymApp.Contracts** class library (net10.0), no dependencies. Empty apart from a `Dates` note; records arrive in 13d onward.
- **GymApp.Server** (`Microsoft.NET.Sdk.Web`, net10.0), with packages `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design`.
  - `ServerDbContext` with no tables yet, plus the first (empty) migration.
  - Configuration: `ConnectionStrings:Social` from `appsettings.Development.json` locally and an environment variable in production. No secrets in the repo.
  - Applies migrations at startup in Development only.
  - `GET /health` returns 200 and checks the database connection.
  - Built-in rate limiter and ProblemDetails registered, with the policies from "Defaults".
- **`docker-compose.yml`** at the repo root with one Postgres 17 service for local development (volume, port 5432, dev-only password).
- **GymApp.Server.Tests** (xUnit), with packages `Microsoft.AspNetCore.Mvc.Testing` and `Testcontainers.PostgreSql`.
  - A shared fixture starts one Postgres container per test run, and each test class gets a fresh database (create, migrate, drop).
  - A helper to register a user and get an authenticated `HttpClient` (used from 13d on).
- Add all four projects to `GymApp.slnx`.

**Tests:** `/health` returns 200.

---

## 13d. Accounts and sessions

**Tables:**
- `Users`: `Id` (int identity), `Username`, `UsernameNormalized` (lowercase, unique), `Email` (lowercase, unique), `PasswordHash`, `ShareGymCheckIns` (bool?, null = not asked), `CreatedAt`.
- `Sessions`: `Id`, `UserId` (cascade delete), `TokenHash` (unique), `DeviceName` (max 64), `CreatedAt`, `LastUsedAt`, `ExpiresAt`.

**Auth handler:** a custom `AuthenticationHandler` reading the bearer token, hashing it, looking up a live session, sliding `LastUsedAt`/`ExpiresAt` (at most once an hour, to limit writes) and setting the user id claim. Every endpoint outside `/v1/auth` and `/health` requires it.

**Endpoints:**

| Method | Path | Body | Result |
|---|---|---|---|
| POST | `/v1/auth/register` | `username, email, password, deviceName` | `201 { token, user }` |
| POST | `/v1/auth/login` | `login` (username or email), `password, deviceName` | `200 { token, user }` |
| POST | `/v1/auth/logout` | none | `204`, revokes this session |
| GET | `/v1/me` | none | `{ id, username, email, shareGymCheckIns }` |
| PUT | `/v1/me/password` | `currentPassword, newPassword` | `204`, revokes every other session |
| DELETE | `/v1/me` | `password` | `204`, deletes the user and, by cascade, sessions, friendships, stats and check-ins |

- Register validates with `Credentials.CheckUsername`, `CheckPassword` and `CheckEmail` (after `NormalizeEmail`) and returns their messages. "That username is taken." and "That email is already used." are 409.
- Login gives the same message for an unknown user and a wrong password ("Wrong username or password.") and always runs a hash, so timing doesn't reveal which usernames exist.

**Tests:** register then `/v1/me`; duplicate username in different case; bad email; login by username and by email; wrong password; expired and revoked tokens give 401; a password change keeps this session and kills the others; deleting the account removes everything and the token stops working.

---

## 13e. Friends endpoints

**Table:** `Friendships` with the same shape and rules as the app's `Friendship`: `UserAId < UserBId`, unique on the pair, `RequestedById`, `Status`, `CreatedAt`, `AcceptedAt`, cascade delete from both users.

**`PostgresSocialStore : ISocialStore`** in GymApp.Server:
- `OpenAsync` starts a transaction at **Serializable** isolation. A Postgres serialization failure (`40001`) or a unique violation (`23505`) on commit becomes `SocialStoreConflictException`, so `FriendService` returns its usual "That request just changed" message.
- `SearchUsersAsync` is a prefix match on `UsernameNormalized` (`LIKE 'abc%'`, with `%` and `_` escaped), ordered by username.
- `RefreshStatsAsync` does nothing; stats are uploaded (13f).

**Endpoints**, all calling the shared `FriendService` with the signed-in user's id:

| Method | Path | Calls |
|---|---|---|
| GET | `/v1/users/search?q=` | `SearchAsync` |
| GET | `/v1/friends` | `GetFriendsAsync` |
| GET | `/v1/friends/requests` | `GetRequestsAsync` |
| GET | `/v1/friends/requests/count` | `IncomingCountAsync` |
| POST | `/v1/friends/{userId}/request` | `SendRequestAsync` (returns `becameFriends`) |
| POST | `/v1/friends/{userId}/accept` | `AcceptAsync` |
| POST | `/v1/friends/{userId}/decline` | `DeclineAsync` |
| DELETE | `/v1/friends/{userId}/request` | `CancelAsync` |
| DELETE | `/v1/friends/{userId}` | `RemoveAsync` |

**Tests:**
- Run the existing `FriendServiceTests` cases against `PostgresSocialStore` as well. Make the test class generic over the store, or share the cases through a base class.
- Endpoint tests for the full flow between two users, a third user who can't accept someone else's request, and both users sending a request at the same moment (one wins, the other becomes friends or gets a conflict, never two rows).

---

## 13f. Stats upload and leaderboard

**Table:** `ProjectedStats`: `UserId`, `Date`, `TotalPoints`, `WeekPoints`, `CurrentStreak`, primary key `(UserId, Date)`, cascade delete.

**Endpoints:**

| Method | Path | Body | Result |
|---|---|---|---|
| PUT | `/v1/me/stats` | `days: [{ date, totalPoints, weekPoints, currentStreak }]` | `204` |
| GET | `/v1/leaderboard?period=thisWeek\|allTime&today=yyyy-MM-dd` | none | the `LeaderboardView` rows |

- **Upload** replaces all of the user's rows in one transaction. Rejected (400) if there are more than 31 days, dates repeat, the first date is more than 2 days from the server's UTC date, or a number is negative.
- **`GetPublicStatsAsync(ids, weekStart, weekEnd, today)`** takes each user's **latest row dated on or before `today`**:
  - If its date is in a different week than `today`, `WeekPoints` is 0.
  - No row at all means 0 points and 0 streak (they never uploaded).
  - A row older than the horizon is still used. Its streak is what the projection said for its last day, which is 0 for anyone with a training day in the schedule.
- `today` comes from the caller, because days are local to the viewer. It must be within 2 days of the server's UTC date, else 400.
- The leaderboard endpoint calls the shared `LeaderboardService`, so ranking, ties and `IsYou` behave as in the app.

**Tests:** upload then leaderboard for three friends; a non-friend never shows; a pending request never shows; the right projected day is picked as `today` moves forward; week rollover; a user with no upload shows 0; bad uploads are rejected.

---

## 13g. Gym check-ins

**Tables:**
- `CheckIns`: `UserId`, `Date`, and the gym:
  - `GymOsmType` and `GymOsmId` (null for a custom gym)
  - `GymName`, `CityOsmType`, `CityOsmId`, `CityName`
  - `Latitude`, `Longitude` (null for a custom gym)
- Primary key `(UserId, Date)`, matching the one-workout-log-per-day rule.

The app's local `Gym.Id` and `City.Id` mean nothing on another phone, so **gyms and cities are identified by their OSM type and id**. In Phase 14 the app maps them back to its own rows (or shows the gym under "other cities").

**Endpoints:**

| Method | Path | Body | Result |
|---|---|---|---|
| PUT | `/v1/me/sharing` | `shareGymCheckIns` | `204`. Turning it off deletes all of the user's check-ins. |
| PUT | `/v1/me/checkins/{date}` | the gym fields | `204`, upsert |
| DELETE | `/v1/me/checkins/{date}` | none | `204` (log undone) |
| GET | `/v1/friends/checkins?since=yyyy-MM-dd` | none | accepted friends' check-ins, with username |

- **Enforced on the server:**
  - A PUT while `ShareGymCheckIns` isn't `true` is refused (403).
  - Coordinates sent with a custom gym (no OSM id) are dropped.
  - A date in the future is refused, beyond the same 2-day allowance.
  - `GET` only returns check-ins of accepted friends whose `ShareGymCheckIns` is `true`, from `since` but never older than 30 days.
- **Cleanup:** a hosted background service deletes check-ins older than 31 days once a day.
- Distances stay on the phone. The server never receives the viewer's position (same as `FriendMapService` now).

**Tests:** sharing off means nothing is stored and nothing is returned; turning sharing off deletes existing rows; custom gym coordinates are dropped; non-friends and pending friends see nothing; the 30-day window holds; undo deletes the row.

---

## 13h. Ready to deploy (no hosting yet)

- A `Dockerfile` for GymApp.Server (multi-stage, runs as non-root).
- Production config only from environment variables: the connection string, plus allowed hosts.
- HTTPS is terminated by the host. Add forwarded-headers handling so rate limiting sees the real client IP.
- Migrations run with a separate command (`dotnet GymApp.Server.dll migrate`), not at startup.
- A short "Running the server" section in README.md: `docker compose up -d`, `dotnet run --project src/GymApp.Server`, and how to run the server tests.

---

## Later (not this phase)

- **Phase 14, the app side:**
  - login and register screens against the server, with local accounts linked by a `ServerUserId` on the local `User`
  - a `RemoteSocialStore` or HTTP-backed `FriendService` for the UI
  - uploading stats after every log, undo, missed-day choice or schedule change, and on app start
  - an offline cache of friends and the leaderboard
  - mapping check-ins to local gyms and cities
- Password reset by email (needs an email provider, e.g. Resend or Postmark).
- Push notifications for friend requests.
- Choosing a host and deploying (a small VPS or an app host plus managed Postgres).
- Server-side checking of points, if cheating becomes a problem.
- Replacing the public OSM tile, Nominatim and Overpass servers before a public release.
