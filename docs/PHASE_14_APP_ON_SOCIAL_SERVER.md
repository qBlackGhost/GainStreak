# Phase 14: The app on the social server — spec for Claude Code

This is the spec for GymApp Phase 14: pointing the app at the social server built in Phase 13. Sign-in moves to the server, friends and the leaderboard come from it, and the phone uploads its projected stats and gym check-ins. Workouts stay on the phone. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added, and to which database (the app's SQLite or the server's Postgres)
- Project layout, existing and new:
  - **GymApp.Core**, **GymApp.Social**, **GymApp.Contracts**: as in Phase 13.
  - **GymApp.Client** (new, 14b): the typed HTTP client for the server API, the token store interface and the "offline" result type. References Contracts, Social and Core, **never Data, Services or Avalonia**, so a future mobile head can reuse it as-is.
  - **GymApp.Services**: gains the account, sync and remote-backed social services. References Client.
  - **GymApp.Desktop**: the only UI head for now. It's wired to the server in this phase, and a mobile head can reuse everything below it.
  - **GymApp.TestBench**: keeps working offline against the local store by default (14h).
- Read `AuthService`, `SessionService`, `RememberMeService`, `LoginViewModel`, `RegisterViewModel`, `ProfileViewModel` (email change, sharing), `HomeViewModel` (startup sync, logout, the share question), `FriendsViewModel`, `GymsViewModel`, `GymPickerViewModel`, `FriendService`, `LeaderboardService`, `FriendMapService`, `LocalSocialStore`, `PointsService`, `StatsProjectionService`, `WorkoutService` (log, undo, automatic gym), `GymService` (`SetGymForTodayAsync`, custom gyms), `ProfileService.SetShareGymCheckInsAsync` and `App.axaml.cs` before changing anything. On the server side, re-read the Phase 13 endpoints and `ServerTest`.
- Every existing test must still pass after every sub-phase. The local store and the local `FriendService` stay; they're what the TestBench and the shared `FriendServiceTests` run on.

## Decisions already made (don't change these)

1. **Workouts stay on the phone.** The server holds social data only. No workout sync in this phase (Phase 13, decision 1).
2. **The server account is the real login; the local `User` row is its cache** (Phase 13, decision 2). It's linked by a new `ServerUserId`. Every local table keeps pointing at the local `User.Id`, so no workout data moves.
3. **Server user ids are ints** (Phase 13, decision 3). In remote mode, the user ids in friend, request, search and leaderboard rows are **server** ids. A view model must never compare them with the local `User.Id`; it uses `IsYou` and the ids it got from the server.
4. **The friend rules run on the server only.** The app doesn't run `FriendService` against a remote store: `ISocialSession`'s check-then-write needs one transaction, and only the server has it. The UI talks to small interfaces that a local implementation (today's services) and a remote one (HTTP) both implement.
5. **The phone computes points and streaks and uploads the 14-day projection** (Phase 13, decisions 5 and 13b). The server never sees workout logs.
6. **Privacy rules carry over unchanged.** Only username, points, level and streak are public. Check-ins are gym and day, only while sharing is on, never a custom gym's position. The viewer's position never leaves the phone: distances are still computed on the phone.
7. **Accounts can be deleted from inside the app** (App Store requirement).

## Defaults to confirm (change here if wrong)

- **Offline sign-in for this phone's own account:** after a successful server sign-in, the local `User.PasswordHash` is updated with a hash of the same password. If the server can't be reached, sign-in falls back to that hash, so the user can still log a workout with no signal. Friends then show cached data. A **wrong** password reported by the server never falls back.
- **Staying signed in:** always, until the user logs out (the usual phone behaviour). The "Remember me" checkbox goes away. The session token is the stored credential, and the server's 90-day sliding expiry applies.
- **Existing local accounts** (installs from before Phase 14): at the first sign-in, an unlinked local account whose username or email **and** password match is offered "Move this account online". That registers it on the server (it asks for an email if the local one is null, and for another username if it's taken) and links it. Its workouts, points and streak stay as they are. Local friendships from Phase 10 don't carry over; friends re-add each other on the server.
- **Registering needs a connection.** There are no offline-only accounts any more (except in the TestBench, see 14h).
- **Deleting the account** deletes it on the server **and** deletes the local `User` with all their local data, after a confirmation that says exactly that. It needs a connection; offline, the button explains why it can't run.
- **Logging out** revokes the session on the server (best effort, offline is fine), forgets the token and clears this user's cached friend data. Local workout data stays.
- **Writes to friends need a connection:** sending, accepting, declining, cancelling and removing aren't queued. Offline, they fail with "You're offline. Try again when you're connected." Uploads (stats, check-ins, the sharing setting) **are** queued and retried.
- **Cache:** the last successful friends list, requests, both leaderboards and friends' check-ins are kept per user in the local database and shown offline with "Last updated <time>".
- **Server address:** `http://localhost:5080` in Debug builds, overridable with the `GYMAPP_SERVER_URL` environment variable. Release builds require `https://` and get their address from the same setting until hosting is chosen (out of scope).
- **Request timeout:** 10 seconds. A timeout counts as offline.
- **Token storage:** behind an `ITokenStore` interface. On desktop it's a file in the data folder, readable only by the user (`chmod 600` on macOS). A mobile head will use the Keychain or Keystore.

---

## 14a. Server additions

Two endpoints the app needs that Phase 13 didn't build. They go in GymApp.Server, with tests in GymApp.Server.Tests.

| Method | Path | Body | Result |
|---|---|---|---|
| PUT | `/v1/me/email` | `currentPassword, email` | `204`. Same checks and messages as register (`NormalizeEmail`, `CheckEmail`, 409 "That email is already used."), plus 400 "That password is not right." and 400 "That is already your email." |
| PUT | `/v1/me/checkins` | `checkIns: [{ date, ...the gym fields }]` | `204`. Replaces **all** of the user's check-ins in one transaction |

- The bulk replace uses the same rules as the single PUT: 403 unless sharing is `true`, custom gym coordinates dropped, no future dates beyond the 2-day allowance, and every field checked. It also rejects a repeated date and more than 31 entries. Entries older than 31 days are dropped, not rejected, so a phone can always send "the last 31 days" as it sees them.
- The single-date PUT and DELETE stay; the app uses the bulk call for resyncs.

**Tests:** change email (success, wrong password, taken in another case, same email, bad email, the old email no longer logs in and the new one does); bulk replace (replaces older rows, refused while not sharing, custom gym coordinates dropped, a repeated date is 400, old entries dropped, future dates refused, an empty list clears everything).

---

## 14b. GymApp.Client: the API client

- New class library **GymApp.Client** (net10.0), referencing Contracts, Social and Core.
- **`SocialApiClient`**, one method per endpoint (Phase 13 and 14a), on an injected `HttpClient`, with `ApiFormat.Json` for every read and write.
  - Sends `Authorization: Bearer <token>` from `ITokenStore` when there is one.
  - Every call returns an `ApiResult<T>` (or `ApiResult` with no value) with one of three outcomes:
    - **Ok** with the value.
    - **Refused**: the HTTP status, the ProblemDetails `detail` and its `field` extension.
    - **Offline**: no connection, DNS failure, timeout or a 5xx. Its message is "You're offline. Try again when you're connected."
  - A **401** on any call except login raises `SessionExpired` (an event), so the app can sign out to the login screen.
- **`ITokenStore`**: `Get()`, `Set(token)`, `Clear()`. Implementations: `FileTokenStore` (desktop, in the data folder, readable only by the user) and `MemoryTokenStore` (tests).
- **`ServerAddress`**: the base URL rule from "Defaults" in one place.

**Tests:**
- In GymApp.Tests, with the existing `FakeHttpHandler`: problem details become `Refused` with detail and field; a thrown `HttpRequestException`, a timeout and a 503 become `Offline`; a 401 raises `SessionExpired`; the bearer header is sent only when there is a token.
- In GymApp.Server.Tests, **contract tests**: `SocialApiClient` on `WebApplicationFactory`'s `HttpClient` against the real server. Register, login, search, the full friend flow, a stats upload and the leaderboard, a check-in and the friends' check-ins. This keeps Contracts, the server and the client in step.

---

## 14c. Local schema

One migration in **GymApp.Data** (SQLite):

- `User.ServerUserId` (int?, unique when set): the linked server account.
- **`SocialCache`**: `UserId` (cascade), `Key` (e.g. `friends`, `requests`, `leaderboard:thisWeek`, `leaderboard:allTime`, `checkins`), `Json`, `FetchedAt`. Primary key `(UserId, Key)`.
- **`SyncState`**: `UserId` (primary key, cascade), `StatsDirtySince`, `CheckInsDirtySince`, `SharingDirtySince` (all `DateTime?`, null = in sync), `LastError` (string?, for the log only).

No existing column changes meaning. `Email` and `PasswordHash` stay: they're the offline-sign-in cache from "Defaults".

**Tests:** the migration applies on a database with existing users and leaves their data alone; `ServerUserId` is unique; deleting a user removes their cache and sync rows.

---

## 14d. Accounts in the app

**`AccountService`** in Services, using `SocialApiClient`, the local database, `ITokenStore` and `SessionService`:

- **`RegisterAsync(username, email, password)`**: registers on the server, then creates the local `User` (same username, email and password hash) with `ServerUserId` set, stores the token and signs in. Field errors come back as `FormField`s, as `AuthService` does today.
- **`SignInAsync(login, password)`**:
  - **Server says ok:** finds the local user by `ServerUserId`. If there isn't one, it creates one, unless an unlinked local account matches (see "Existing local accounts" in Defaults); then it returns `MoveOnlineOffered` instead. It refreshes the cached username, email, password hash and `ShareGymCheckIns`, stores the token and signs in.
  - **Server says wrong:** "Wrong username or password.", with no offline fallback.
  - **Offline:** checks the linked local user's cached hash. On a match, it signs in with a banner: "You're offline. Friends will update when you're back online." With no linked local user, it fails with the offline message.
- **`MoveOnlineAsync(localUserId, password, email?, username?)`**: registers the existing local account on the server, then links it. Field errors as in register.
- **`RestoreAsync()`**: at startup, with a stored token, calls `GET /v1/me`. On success, it refreshes the cache and signs in. On 401, it clears the token and shows the login screen. Offline, it signs in as the linked user with the cached data. Called where `RememberMeService.TryRestore()` is today.
- **`LogOutAsync()`**, **`ChangeEmailAsync`** (`/v1/me/email`, then the local cache), **`ChangePasswordAsync`** (`/v1/me/password`, then the local hash) and **`DeleteAccountAsync(password)`** (`DELETE /v1/me`, then the local user and everything that cascades from it, then sign out).
- `SessionExpired` from the client signs out to the login screen with "Your session ended. Sign in again."

**UI** (Desktop):
- **Login:** the field label becomes "Username or email" and the "Remember me" checkbox goes away. The "Move this account online" step shows when offered.
- **Register:** goes through `AccountService`.
- **Profile:** the email change goes through `AccountService`; a password change section is added (current, new, confirm); "Delete account" goes behind a confirmation that asks for the password and states what's deleted.
- **Logout** (Home and Sidebar) goes through `AccountService`.
- `AuthService` and `RememberMeService` stay for the TestBench's local mode only; the Desktop head no longer uses them.

**Tests** (GymApp.Tests, with a fake `SocialApiClient` handler):
- Register creates the linked local user.
- Sign-in with an existing link reuses the local user and their data; a first sign-in on a new phone creates a local user.
- An offline sign-in with the right cached password works; an offline sign-in with the wrong one fails; a server-wrong password never falls back.
- Move online links the existing local account and keeps its workouts and points; a taken username asks for another.
- Restore: ok, 401 and offline.
- Delete account removes the local user and their data only after the server said 204.
- Log out clears the token and the cache but keeps workouts.

---

## 14e. Friends and the leaderboard from the server

- **Interfaces** in GymApp.Social, matching what the view models call today:
  - `IFriendService`: `SearchAsync`, `SendRequestAsync`, `AcceptAsync`, `DeclineAsync`, `CancelAsync`, `RemoveAsync`, `GetFriendsAsync`, `GetRequestsAsync`, `IncomingCountAsync`, all with today's signatures and result records.
  - `ILeaderboardService`: `GetAsync`.

  `FriendService` and `LeaderboardService` implement them unchanged.
- **`RemoteFriendService`** and **`RemoteLeaderboardService`** in Services, over `SocialApiClient`:
  - They act as the signed-in user. The `userId` argument must be the signed-in local user's id; anything else is a programming error, not a request for someone else's data.
  - Refusals return the server's message in the existing result records (`FriendResult.Error`, `FriendSearchResult.Error`); offline returns the offline message.
  - Reads save to `SocialCache` on success and come from it when offline. The results gain an `IsStale` flag and a `FetchedAt` time so the UI can show "Last updated …". Add them to the records with defaults, so the local services need no change.
  - The leaderboard sends `AppClock.Today` as `today`. Rows map back to `LeaderboardRow`, with `LevelInfo` rebuilt by `LevelCalculator` from the level number (the server sends number and name).
- **View models** (`FriendsViewModel`, `HomeViewModel`) depend on the interfaces. `App.axaml.cs` registers the remote implementations. They show the stale banner when offline, and turn a `Refused` 409 into the existing "That request just changed" message.

**Tests:**
- The remote services against the real server (in GymApp.Server.Tests, like the 14b contract tests): the full friend flow, a refused accept, the leaderboard with three friends, and `IsYou` on the right row.
- Unit tests with the fake handler: an offline read returns the cached data marked stale; an offline write returns the offline message and changes no cache.

---

## 14f. Uploading stats, check-ins and the sharing setting

**`SocialSync`** in Services. It's the only code that uploads:

- `MarkStatsDirty(userId)`, `MarkCheckInsDirty(userId)` and `MarkSharingDirty(userId)` set the `SyncState` columns and start a sync in the background. They never block or fail the caller.
- **`SyncAsync(userId)`**: for the signed-in, linked user only. Runs whatever is dirty, in this order:
  1. **Sharing:** `PUT /v1/me/sharing` with the local `ShareGymCheckIns` (null counts as false on the server).
  2. **Check-ins:** if sharing is on, `PUT /v1/me/checkins` with the last 31 days of workout logs that have a gym. Each one gets the gym's and city's OSM type and id, names and position; a custom gym never gets a position, the server drops it anyway.
  3. **Stats:** `PUT /v1/me/stats` with `StatsProjectionService.ProjectAsync(userId, AppClock.Today)`.

  Each step clears its own flag on success. Offline leaves it set; `Refused` logs `LastError` and clears it, so a bad payload isn't retried forever.
- **When it runs:** after each mark, at sign-in and restore, when the app comes back to the foreground (on desktop, when the window is activated), every 15 minutes while open, and when a request succeeds after being offline. Only one sync runs at a time per user.

**Where the marks go** (one place each, not in the view models):
- **Stats:** at the end of `PointsService.SyncAsync` and `RebuildAsync`. Every stats change already ends there: log, undo, missed-day choice, split select or edit, calorie log, the startup catch-up and the TestBench. `PointsService` takes an optional sync hook, which is a no-op in tests and the TestBench's local mode.
- **Check-ins:** `WorkoutService.LogTodayAsync` (when the automatic gym was set), `WorkoutService.UndoTodayAsync`, `GymService.SetGymForTodayAsync`, and renaming a custom gym used in the last 31 days.
- **Sharing:** `ProfileService.SetShareGymCheckInsAsync` marks sharing, and check-ins too when it's turned on.

**Tests:**
- Each hook marks the right flag.
- A sync uploads the projection and the right check-ins (only the last 31 days, only logs with a gym, no position for a custom gym, OSM keys and not local ids).
- Sharing off sends no check-ins.
- Offline keeps the flags and a later sync clears them; a refusal clears the flag and records the error.
- End to end against the real server (GymApp.Server.Tests): log a workout with a gym on phone A; friend B's leaderboard shows the new points and B's friends' check-ins show the gym.

---

## 14g. The friends map from the server

- **`IFriendCheckInSource`** in Social, with today's `GetFriendGymCheckInsAsync(userId, since)`. `LocalSocialStore` implements it; `FriendMapService` and the gym picker's "friends here" counts depend on it instead of `ISocialStore`.
- **`RemoteFriendCheckInSource`** in Services calls `GET /v1/friends/checkins` (cached like 14e). It maps each row back to the phone's own data:
  - **City:** by `(OsmType, OsmId)` to a local `City`. If there's no match, `CityId` is null, so the row lands under "other cities", grouped by the city name the server sent.
  - **Gym:** by `(OsmType, OsmId)` to a local active `Gym` in that city. A custom gym, or an OSM gym the phone hasn't fetched, gets a **negative** `GymId`, unique within the result, so it can't collide with a local id. It keeps the server's name and position. `IsCustomGym` is true when the gym has no OSM id.
  - `DistanceKm` stays null here; `FriendMapService` fills it in from the phone's own origin, as now.
- No change to `FriendMapRules`: home-city pins still need a local city match, custom gyms still never become pins.

**Tests:** mapping to local gym and city ids; an unknown city goes under other cities; an unknown OSM gym in the home city still gets a pin from the server's position; a friend's custom gym gets a negative id, no position and no pin; the gym picker counts only gyms that matched.

---

## 14h. TestBench and cleanup

- The TestBench keeps the **local** social mode by default: `LocalSocialStore`, the local `FriendService`, `AuthService` and "sign in as any user", and a no-op sync. Its clock moves freely, which the server would refuse (the 2-day allowance). Its admin window gets a "Social: Local / Server" switch that restarts it like the existing data switches. In Server mode it uses the Desktop wiring, and "sign in as any user" is disabled.
- Remove the Desktop registrations that are no longer used (`ISocialStore` → `LocalSocialStore`, `AuthService`, `RememberMeService`). Keep the classes for the TestBench and the tests.
- **README:**
  - running the app against a local server (`docker compose up -d`, `dotnet run --project src/GymApp.Server`, then the app)
  - `GYMAPP_SERVER_URL`
  - the TestBench's social switch
  - the new projects in the layout table
- **CHANGELOG:** a 1.1.0 entry.

**Tests:** the TestBench in local mode still runs the existing flows with no server, and the full test suite passes with Docker running.

---

## Later (not this phase)

- **A mobile head** (iOS and Android), reusing Client and Services; Keychain or Keystore token storage; foreground and connectivity events from the platform.
- **Rate limits behind shared IPs:** mobile carriers put many phones behind one IP, so "register 5/hour per IP" may block real users. Revisit before release (per-device or CAPTCHA-free alternatives).
- Password reset by email, email verification, push notifications for friend requests.
- Choosing a host and deploying; the production server address.
- Workout sync between devices (would need Guid ids for rows created offline).
- Server-side checking of points, if cheating becomes a problem.
- Replacing the public OSM tile, Nominatim and Overpass servers before a public release.
