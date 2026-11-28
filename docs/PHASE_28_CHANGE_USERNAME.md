# Phase 28: Change your username — spec for Claude Code

This is the spec for GymApp Phase 28 (version 0.28.0). The number is a placeholder: `docs/FUTURE_28_BACKLOG.md` also
plans its items as 0.28.x (and the gym list from our own server, once called Phase 28, shipped in 0.27.0). If another
phase lands first, renumber this file, the version and the sub-phase letters. It builds on Phase 21 (friend QR and
invite links) and Phase 13/14 (accounts on the server).

Today a username is chosen at registration and never changes (`RegisterRequest` is the only place it is set). This phase
lets a signed-in user **rename themselves**, with limits so a rename can't be used to dodge a block or a report or to
pass as someone else. Phones only (the desktop is frozen). Work through the sub-phases **in order** and stop after each
one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase". 28a changes the server's data model, so it runs the server tests
  (Docker) and the `server-compat-reviewer` agent. 28b changes only app code (no entity), so no compiled-model step
  unless that changes. After each sub-phase, report: files created, files changed, the migration (28a adds one), what
  you checked by hand on each platform and what you couldn't.
- **Builds for the phones: Debug while working, one iOS Release build at the end.** A Release build for the Simulator
  (full AOT) takes about 20 minutes, so don't make one after every sub-phase or fix:
  - **Hands-on checks during the phase** (28c's screen, a fix after review) use a **Debug** build for the Simulator
    (`-c Debug`, about 5 minutes) and the Android Debug build. Debug builds talk to a local server
    (`http://localhost:5080`, the emulator's `10.0.2.2`), which is also the only way to try the new endpoint before
    it's deployed; a Release build refuses a plain `http` address.
  - **An iOS Release build is needed only** to check something a Debug build hides: a change to the app's entities or
    `OnModelCreating` (EF Core model building, CLAUDE.md), or to the iOS head's own code (`Info.plist`,
    entitlements, the Live Activity). Phase 28 changes none of these (28a's migration is the server's, not the app's).
  - **When several updates need one, batch them:** finish them all, then make one Release build and check them
    together. Never rebuild Release for a change that doesn't need it.
  - **The phase's single Release build is in 28d**, against the beta server, for the end-of-phase checks and the
    scrolling pass.
- Read `AccountEndpoints.cs` (`Register`, `ChangeEmail`, `ChangePassword`, `GetMe`), `RateLimits.cs`, `Server/Data/User.cs`
  and `ServerDbContext` (the unique `IX_Users_UsernameNormalized` index), `Credentials.CheckUsername`, `UsernameFilter`,
  `Contracts/Accounts.cs` (`MeResponse`, `ProblemFields`), `AccountService` / `IAccountService` /
  `LocalAccountService` (and how `MeResponse` is cached into `User.Username`), `SocialApiClient`, `ProfileViewModel`,
  `FriendQrViewModel`, `SocialCacheStore` and `ClientContractTests` / `ClientVersionTests` before changing anything.
- Every existing test must still pass. Use `AppClock` / the injected `TimeProvider`, never `DateTime.Now`.
- **Both phones, every change:** this is shared code (`GymApp.App`, services, server), so it reaches iOS and Android.
  Check the new screen on a narrow phone. Nothing here is platform-specific; say so in the report.
- **The desktop** only has to keep building and starting (CLAUDE.md, "Desktop freeze"). The rename screen is phone-only
  and hidden on the desktop (gate it through `IPlatform`, as `HasLiveWorkout`); the TestBench's local mode gets the
  rename through `LocalAccountService`.
- **Old clients:** the server's `MinClientVersion` stays at or below 0.21.0. Only add an endpoint and optional fields to
  `Contracts`. Old builds never call the new endpoint and keep working.

## What was checked before writing this

- **Friend QR and invite links don't depend on the username.** `FriendInvites.NewCode()` is a random 10-character code
  ("never the user id"); the link is `https://<host>/add/<code>` and `gainstreak://add/<code>`; the server keeps it in
  `FriendInvite(UserId, Code, CreatedAt)`. A rename changes **nothing** about the code, the link, a printed QR code or one
  already shared. Only the text under the code (`FriendQrViewModel.Username`, read from `session.CurrentUser`) and the
  name the confirm card shows after a scan (`InviteLookupResponse.Username`, read live) change, and both must show the
  new name.
- **Everything the server relates by id** survives a rename: friendships, friend requests, shares (`FromUserId`), blocks,
  reports, check-ins, badges, stats, push devices. Names in `FriendResponse`, `ShareResponse.FromUsername`,
  leaderboard rows and check-in rows are joined from `Users.Username` on every request, so they update by themselves.
- **What keeps the old name:** the app's offline cache of friends, leaderboard, inbox and check-ins
  (`SocialCacheStore`) until its next successful refresh; a push notification already delivered (the text is built from
  the sender's name at send time); a message the user already copied or shared. These are accepted, not fixed.
- **Sign-in:** `Login` accepts the username or the email, so after a rename the user signs in with the **new** name
  (the old one stops working, or someone else may take it). The settings screen must say so.
- **The username is the only identity a friend sees**, so renaming is also the way to pass as someone else. That is
  what the limits below are for.

## Decisions already made (don't change these)

1. **Rules are the same as registration:** `Credentials.CheckUsername` (3 to 32 characters, letters, digits, `_` `.` `-`,
   at least one letter) and `UsernameFilter.IsAllowed`, checked **on the server**, the app only mirrors them for a
   quick message. Uniqueness is on `UsernameNormalized` (ignoring case), through the existing unique index.
2. **Changing only the case of your own name is allowed** ("gainstreak" to "GainStreak"): the normalized value stays,
   it doesn't count as taking a name, and it doesn't use the cooldown (decision 3). Anything else does.
3. **Cooldown: once every 30 days.** The server stores `UsernameChangedAt` on the user and refuses a second change
   sooner with 429 and a message that says when it works again. The 30 days is a constant in `Credentials` (or a server
   setting), counted from `TimeProvider`, and a user who never renamed can rename at once.
4. **Needs the current password**, like changing the email or the password (`RateLimits.Password` policy). A stolen
   phone can't rename the account.
5. **The old name isn't reserved.** It is free for anyone as soon as the rename succeeds. (A "Later": hold it for some
   days so nobody can take a name to imitate its previous owner.)
6. **Names that were already in use before this phase don't have to follow today's rules** (`Credentials` says so);
   only the new name is checked.
7. **Blocked and suspended users:** a suspended user (`SuspendedAt`) can't rename. A rename doesn't clear a block or a
   report (both are by id). A moderator sees the current name, and a **username history** is kept (see Data model) so
   a report against a name that was changed afterwards can still be understood.
8. **No notice to friends.** Friends just see the new name. (A "Later": a one-line "was @old" in the friend row for a
   week, or a push to friends.)
9. **Nothing else moves to the server.** No new stats, no change to invite links.

## Data model and server (28a)

- `User` (server): `UsernameChangedAt` (`DateTime?`, UTC).
- New table `UsernameChange`: `Id`, `UserId`, `OldUsername`, `NewUsername`, `ChangedAt`. Deleted with the user (account
  deletion already removes a user's rows: add it to that list and to its test). Never sent to clients.
- Migration `UsernameChange` in `GymApp.Server` (`dotnet ef migrations add UsernameChange --project src/GymApp.Server
  --output-dir Data/Migrations`). The production server must run `migrate` before deploy.
- **Contracts** (additive only): `ChangeUsernameRequest(string Username, string CurrentPassword)`; the success answer is
  `MeResponse` (the same record `GET /v1/me` returns, so the app updates its cache from one place). Add
  `ProblemFields.Username` use for the errors (it exists). Optional: `MeResponse.UsernameChangeAllowedAt` (`DateTime?`)
  so the screen can show when a rename works again without trying first.
- **Endpoint:** `PUT /v1/me/username`, in `AccountEndpoints`, with `RateLimits.Password`.
  - 400 for a name that fails `CheckUsername` / `UsernameFilter` (field `username`), 400 for a wrong password (field
    `password`, the existing message), 400 when the new name is the same as the current one (exact match).
  - 409 `UsernameTaken` when another user has the normalized name. Also catch the unique-index violation between the
    check and the save, as `ChangeEmail` does for emails.
  - 429 when inside the cooldown, with the date in the message and in `UsernameChangeAllowedAt`.
  - Success: set `Username`, `UsernameNormalized`, `UsernameChangedAt`, add the `UsernameChange` row, one
    `SaveChangesAsync`. Return `MeResponse`.
  - A case-only change (decision 2) skips the cooldown and doesn't set `UsernameChangedAt`.
- `GetMe` returns `UsernameChangeAllowedAt` when set and in the future.
- **Server tests** (real Postgres, Testcontainers): a rename shows in friends, shares, leaderboard and check-ins for
  the other user; the old name can no longer sign in, the new one can; taken name 409; a filtered name 400; wrong
  password 400; cooldown 429 then allowed after the clock moves 30 days (`FakeTimeProvider`); case-only change allowed
  twice in a row; two simultaneous renames to the same name end with one 409; a suspended user refused; account
  deletion removes the history rows; the invite code and link are the same before and after a rename.
- Run `ClientContractTests` and `ClientVersionTests`, and the `server-compat-reviewer` agent: the change must be
  invisible to a 0.21.0 client.

## App (28b)

- `SocialApiClient.ChangeUsernameAsync(request)` returning Ok / Refused / Offline like the other calls (a 10-second
  timeout counts as offline). The result carries the refusal's `field` and message.
- `IAccountService.ChangeUsernameAsync(newUsername, currentPassword)` with `AccountService` (server) and
  `LocalAccountService` (the TestBench's local mode: same rules, same cooldown, the check against the local table,
  `FreeLocalNamesAsync`'s naming rules unchanged). A success updates `User.Username` in the app's SQLite and the
  session's current user the same way a login does, then clears nothing else.
- **Offline:** a rename needs the server. Offline, the screen shows the existing offline text and the button is
  disabled; nothing is queued.
- After a success: refresh `SocialCacheStore` (friends, leaderboard, inbox, check-ins) once so the user doesn't see
  their own old name in a cached list, and let `FriendQrViewModel`, Home, Profile and the sidebar read the new name
  (`session.CurrentUser` raising its change). Check every place that shows `CurrentUser.Username` or builds
  `UserInitials` (Home card, Profile, Friends, `BadgeViewModels`, `DashboardCards`, `ReportUserDialogViewModel`,
  widget snapshot) updates **without a restart**. List what you found in the report.
- A friend's cached name changes on the next refresh; make sure the app refreshes friends when it comes to the
  foreground (it should already, `SocialSync`), and add a test that a renamed friend shows the new name after a refresh.
- **Tests (app side, `FakeSocialServer`):** success updates the session and the saved user; each refusal shows under the
  right field; the cooldown message; offline disables; the local mode follows the same rules.

## Screen (28c), phones

- Profile gets a **Change username** row under the account details (next to email and password). It opens a sheet
  (`Sheet.cs`, as the other account sheets) with: the current name, a field for the new one with live length and
  character feedback (`Credentials.CheckUsername`, not the server), the **current password**, and **Change** (disabled
  until valid, and while saving).
- Text on the sheet, written for testers: "Friends see your new name straight away. Your invite link and QR code keep
  working. You'll sign in with the new name. You can change it again after 30 days." Case-only changes say "No waiting
  time for capital letters".
- Errors under the right box (`FormField.Username` / password), the cooldown as a disabled state that says the date.
- Success closes the sheet with a short confirmation ("You're now @name").
- Use the existing form and sheet styles and tokens (light and dark). Look at the sheet on a narrow phone and with
  large text sizes; the keyboard must not cover the password field.
- Hidden on the desktop (`IPlatform` gate); available in the TestBench local mode.

## Release (28d)

- `CHANGELOG.md` 0.28.0 for testers in the existing style: **Change your username** (once every 30 days, needs your
  password, friends see it at once, your invite link stays the same). End with **Migration:** server database
  (`UsernameChange`). `Directory.Build.props` to 0.28.0.
- `site/privacy.html` and `docs/STORE_LISTING.md`: the server now keeps a short history of a user's previous
  usernames (for moderation). Update the privacy text and say what you changed. The history is deleted with the account.
- Deploy order: `migrate` on the beta server **before** the new server image, then the app. Say so in the report.
- The end-of-phase checks from CLAUDE.md, run once, after the other 28d items: full `dotnet test`, the Android build,
  the phase's one iOS Release build for the Simulator (see "Builds for the phones" in the ground rules) and running
  both phone heads on it, and the scrolling pass on Profile and Friends in that same build. If the report turns up a
  fix, check it in a Debug build; a new Release build only if the fix needs one.

**Stop and report**, with what must be checked on a real phone.

**Check on a real phone:**
- rename, then see the new name on a second account's Friends, leaderboard and inbox after a refresh
- scan a QR code and open an invite link made **before** the rename: both still work and show the new name
- sign out and in with the new name; the old name is refused
- the cooldown message on a second rename, and the case-only change
- wrong password, a taken name, a filtered name, offline
- the sheet with the keyboard open on a small phone, in light and dark, with large text

## Defaults to confirm (change here if wrong)

- **Cooldown:** 30 days (decision 3).
- **Password required** for the rename (decision 4).
- **Old names are released at once** (decision 5).
- **Case-only changes don't count** against the cooldown (decision 2).
- **No friend notification** (decision 8).

## Later (not this phase)

- Hold a released name for some days; "was @old" for a week in the friends' view; a push to friends.
- A moderator tool or report field that shows the username history.
- Showing the user's own username history in the app.
- A separate display name that isn't the sign-in name.
