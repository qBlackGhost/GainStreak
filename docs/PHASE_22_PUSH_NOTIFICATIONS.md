# Phase 22: Push notifications for friends — spec for Claude Code

This is the spec for GymApp Phase 22 (version 0.22.0). The phones get a notification when something happens with friends while the app is closed: "**alex** wants to be your friend", "**alex** accepted your friend request", and "**alex** added you with your QR code". Tapping it opens the request in the Inbox, or Friends. iPhones get them through Apple's push service (APNs), Android phones through Firebase Cloud Messaging (FCM). The desktop gets none. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase": `dotnet build` and the app tests after each sub-phase; the full `dotnet test`, both phone heads (Android emulator and iOS Simulator, **Release**) and the desktop app (builds and starts) at the end of the phase. 22a and 22b change `GymApp.Contracts` and the server, so they also run the server tests right away; 22e changes both heads' own code, so it builds both right away. After each sub-phase, report:
  - which files you created
  - which files you changed or moved
  - which migrations you added, and to which database (this phase has one on each: server Postgres and app SQLite)
  - what you checked by hand on each platform, and what you couldn't check
- After any change to the app's entities, regenerate the compiled model (`dotnet ef dbcontext optimize`, see CLAUDE.md) and test the iOS Simulator with a **Release** build. After changing the iOS head's `Entitlements.plist` or `Info.plist`, delete the stale `AppManifest.plist` and `nativelibraries/GymApp.iOS` (CLAUDE.md).
- **Desktop freeze:** no desktop work. Push is phone-only, gated through `IPlatform`; the desktop hides everything this phase adds. Installed 0.21.0 builds (the desktop's last release, and the phones' if any) must keep working against the 0.22.0 server: only new endpoints and new contract records, no change to existing ones, `MinClientVersion` stays at or below 0.21.0.
- **No secret in the repo.** The APNs key, the Firebase service account and `google-services.json` stay outside it (`.gitignore` them by name). The server reads them from settings, which Cloud Run fills from Secret Manager.
- Read `FriendEndpoints`, `FriendService` (`SendRequestAsync`, `AcceptAsync`, `AcceptInviteAsync` and when they set `BecameFriends`), `AccountEndpoints` (logout, password change and reset, delete), `Session` and `SessionTokens`, `ServerDbContext`, `DiscordFeedbackForwarder` and how `Program.cs` reads its setting, `InviteSettings`, `AccountCommands`, `RateLimits`, `Contracts/Friends.cs` and `Accounts.cs`, `SocialApiClient`, `RemoteSocialServices` and `LocalAccountService`, `AccountService` (sign-in, `LogOutAsync`, `DeleteAccountAsync`), `SocialSync`, `AppServices` (both wirings), `IPlatform`, `PhonePlatform`, `INotificationScheduler` with both phone schedulers, `RemindersViewModel` and Profile's Reminders section, `InboxViewModel` (the Requests filter), `MainViewModel` (`ShowFriends`, `ShowInbox`, `OpenLink`), `GymApplication` (`OpenFromReminder`, `OpenLink`, `_linkBeforeShell`), `AppDelegate`, `ReminderDelegate`, `MainActivity`, `AndroidManifest.xml`, `MobileHead.targets`, `FriendEndpointTests`, `RemoteSocialEndToEndTests`, `ClientContractTests`, `site/privacy.html` and `docs/STORE_LISTING.md` before changing anything.
- Every existing test must still pass.
- Run the `server-compat-reviewer` agent at the end of 22a and again before the release.

## Decisions already made (don't change these)

1. **Three events notify, all about friends:**
   - **A request arrives** (`SendRequestAsync` saved a pending request): to the receiver, "**alex** wants to be your friend". Tap → Inbox, Requests filter.
   - **A request is accepted** (`AcceptAsync`, or `SendRequestAsync` that accepted the other's pending request, i.e. `BecameFriends`): to the one who asked first, "**alex** accepted your friend request". Tap → Friends.
   - **An invite is accepted** (`AcceptInviteAsync`, Phase 21): to the inviter, "**alex** added you with your QR code. You're now friends." Tap → Friends.
   Declines, cancels, removals and blocks never notify: they're silent today (a blocked user isn't told), and stay so.
2. **Sending lives in the server's endpoints, not in `FriendService`.** `FriendService` is shared with the TestBench's Local mode, which has no push. The endpoint calls the notifier after the rule succeeded. Local mode sends nothing; its push service is a no-op (CLAUDE.md: both wirings register an implementation).
3. **Sending happens inside the request**, as `DiscordFeedbackForwarder` does: Cloud Run scales to zero and only gives the container CPU while a request is open, so work left for after the response may never run. A short time limit bounds it, and a push that fails, times out or isn't configured **never** changes the friend endpoint's response.
4. **A device token belongs to a session.** One row per signed-in session, so logging out (the session row is deleted) stops the pushes at once, and so do a password change (other sessions), a password reset and account deletion, through the database's cascade. A token APNs or FCM reports as dead is deleted. The same token registered by a newer session (another account on the same phone) moves to it, so a phone never gets the previous account's notifications.
5. **APNs directly for iPhone, FCM for Android.** FCM could also reach iPhones, but only with the Firebase SDK in the iOS head (more native code under full AOT, a bigger app, another company's SDK on iOS). APNs over HTTP/2 needs nothing but `HttpClient` and an ES256 token, which .NET signs itself.
6. **Backward compatible.** New endpoints and new contract records only. Builds before 0.22.0 never register a token, so they simply get no pushes. No `MinClientVersion` bump.
7. **All push settings are optional.** Unset, the server runs as today: tokens are still accepted and stored (so they're ready when the keys arrive), and nothing is sent. A malformed setting stops startup, like the other settings.
8. **The desktop has no push.** `IPlatform` says so; Profile and Friends hide what this phase adds there.

## Defaults to confirm (change here if wrong)

- **The text names the sender.** "**alex** wants to be your friend" goes through Apple's or Google's servers with the username in it, and the user id of the sender in the data (for the tap). Nothing else: no email, no stats. Usernames are already visible to anyone searching for them, and a generic "You have a new friend request" makes the notification much less useful. The alternative, a generic text that the phone fills in itself, needs a Notification Service Extension on iOS (Swift, like the Live Activity) and is in "Later". **Decision for the user:** username or generic text.
- **The title says what happened** ("Friend request", "Request accepted", "New friend"; chosen on the canvas in 22d, first planned as "GainStreak"); the body is the sentence. English only, built on the server (the app has no other language yet).
- **One switch: Profile → Notifications → "Friend requests"** (covers all three events), next to Reminders. It's per phone: off unregisters this phone's token, on registers it. No per-event switches yet.
- **When to ask for permission.** Never at first launch. If the phone already allowed notifications (Reminders, 16g, uses the same permission), the switch starts **on** and the token is registered without asking. Otherwise a card at the top of Friends, shown once the user has sent or received a request, or opened the QR sheet: "**Know when friends add you.** Get a notification when someone sends you a request or accepts yours." with **Turn on** (then the system prompt) and **Not now** (hides it for good; Profile still has the switch). iOS asks only once, so the system prompt only follows a tap on Turn on. Refused: the switch stays off with Reminders' note ("Notifications are off for GainStreak. Allow them in your phone's Settings…").
- **No repeats:** at most one "wants to be your friend" push per sender and receiver in 12 hours, so request → cancel → request can't be used to buzz someone. Kept in memory: the server runs one instance, and after a cold start the worst case is one extra push. Accepted and invite pushes happen once per friendship and need no limit.
- **Time limit:** 3 seconds for all of one event's sends together (APNs and FCM in parallel, to each of the receiver's devices), then the endpoint answers anyway. The HTTP clients' own timeout is 5 seconds, as a backstop. At most 10 devices per user (the most recently used sessions).
- **Expiry:** APNs `apns-expiration` and FCM `ttl` of 1 day. A friend request is still there in the Inbox after that; the push is just stale.
- **Collapsing:** one notification per sender and kind on the phone (`apns-collapse-id` / Android `tag` = `<kind>-<senderId>`), grouped as "Friends" (`thread-id` / the "Friends" channel). A request that becomes a friendship replaces its own notification.
- **Foreground:** a push that arrives while the app is open isn't shown as a banner when the user is already on Inbox or Friends; it refreshes them (and the Inbox badge) instead. On any other screen it shows as a banner (iOS `WillPresentNotification`; Android posts it itself, since FCM doesn't show notification messages in the foreground) and refreshes the badge.
- **APNs environment per token.** The phone sends whether its build talks to APNs' sandbox (Debug builds signed for development, the Simulator) or production (TestFlight and the App Store). The server keeps it with the token and sends each to the right host. Both come from the same .p8 key.
- **Android without `google-services.json`:** the build still works; push is off on that build (`IPlatform` says unsupported, the switch is hidden). So a fresh checkout, CI and the TestBench build without Firebase files.

---

## Design first

The screens go on the **GainStreak Mobile** design canvas (claude.ai artifact "GainStreak Mobile"), new row **Notifications · Push (Phase 22)**:
- **Profile · Notifications**: Reminders (unchanged) and the new **Friend requests** switch, with the "off in Settings" note state.
- **Friends · Turn on notifications (card)**: the card above the leaderboard, with Turn on / Not now. Scenario tweak: shown, after refusal (the note), hidden.
- **Lock screen and banner**: each of the three notifications on iOS and on Android, for wording and the small icon.

The user approves them before 22d starts. Where the artboards and this spec disagree, ask.

## 22a. Device tokens: server, contracts and the notifier (no real sending)

**Server (Postgres migration `PushDevices`):** table `PushDevices`:
- `Id`; `SessionId` (unique, FK to `Sessions`, cascade); `UserId` (FK to `Users`, cascade; kept for the lookup by receiver).
- `Platform` (`apns` / `fcm`); `Token` (unique, max 4096); `Sandbox` (bool, APNs only); `CreatedAt`, `UpdatedAt`.

**Contracts (`Contracts/Push.cs`):**
- `PushPlatform { Apns, Fcm }` (camelCase strings, as `ApiFormat` writes enums).
- `PushDeviceRequest(PushPlatform Platform, string Token, bool Sandbox = false)`.

**Endpoints (`PushEndpoints`, signed in):**
- `PUT /v1/me/push-device` → 204. Registers or replaces the token of **the calling session**. Deletes the same token from any other session first (decision 4). 400 for an empty or too long token. Rate limit: a new `push-device` policy, 20 an hour per user.
- `DELETE /v1/me/push-device` → 204, also when there was none.

**Expired sessions:** the notifier skips devices whose session has expired (`ExpiresAt`), and the register endpoint deletes the user's devices on expired sessions while it's there (no new background job; scale to zero).

**The notifier (`GymApp.Server/Push`):**
- `IPushSender` per platform: `SendAsync(PushDevice, PushMessage, ct)` → `Sent` / `DeadToken` / `Failed`. 22a has only a fake in the tests and a no-op (`NoPushSender`) when nothing is configured.
- `PushMessage(Kind, SenderId, SenderUsername)` with `Kind` = `FriendRequest`, `FriendAccepted`, `InviteAccepted`; the text and payload come from one place (`PushTexts`), so the tests and both senders agree.
- `FriendNotifier.NotifyAsync(receiverId, message)`: loads the receiver's live devices (at most 10), sends to all in parallel under the 3-second limit, deletes `DeadToken` rows, logs failures, never throws. Applies the 12-hour repeat rule for `FriendRequest`.
- `FriendEndpoints`: `SendRequest` (pending → `FriendRequest` to the other; `BecameFriends` → `FriendAccepted` to the other), `accept` (→ `FriendAccepted` to the requester), `AcceptInvite` (→ `InviteAccepted` to the inviter, whose id `LookUpInviteAsync` gives). Only after success; the response is the same as today.

**Tests (server):**
- Register, replace, unregister; the token moving to a newer session of another user; 400s; the rate limit.
- Logout, password change (other sessions), password reset and account deletion remove the rows.
- Each friend endpoint notifies the right user with the right kind, once; decline, cancel, remove and block notify nobody; a failed rule notifies nobody.
- A sender that hangs: the endpoint still answers within the limit with its usual response. A sender that throws: same. `DeadToken` removes the row. Expired sessions get nothing.
- The 12-hour repeat rule (with the injected `TimeProvider`).
- `ClientContractTests` for the new record and enum (in 22c, with the client calls, as in Phase 21).

### 22a as built (2026-11-13)

Where it differs from the plan above:
- **The token is at most 512 characters, not 4096** (`PushRules.MaxTokenLength`): APNs tokens are 64 hex characters and FCM's about 160, and a long value would push the unique index past Postgres' btree row limit. A valid token is printable ASCII without spaces.
- **`PushKind` and the payload keys are in `GymApp.Contracts`** (`Push.cs`: `PushKind`, `PushPayload.Kind`/`From`/`Name`/`Parse`), since the phones read them from the push's data. 22c's routing uses them.
- **No `NoPushSender`:** a platform with no registered `IPushSender` is simply not configured. `FriendNotifier.IsConfigured` is false with none, and then it doesn't touch the database.
- **The inviter's id:** `FriendResult` gained an optional `OtherUserId`, which `AcceptInviteAsync` fills on success (also when it accepted a waiting request), so the endpoint knows whom to tell without a second lookup.
- **`FriendNotifier`** (singleton, `GymApp.Server/Push`) also has `SendToUserAsync(userId, message)` with no repeat rule, for 22b's `push-test`. It returns a `PushDelivery` per device (platform, sandbox, result; never the token). A send still running at the time limit counts as `Failed`; the endpoint answers anyway.
- **Register** runs in one transaction: removes the token from other sessions, removes the user's tokens on expired sessions, then updates or adds this session's row. Two registrations racing on the same token give 409 ("That push token just changed. Try again.").
- **Tests:** `PushTests` (15, with fake senders, and a 0.8 s limit so the hanging-sender test is quick) and `PushUnconfiguredTests` (1). The 10-device cap isn't tested: 11 sign-ins would hit the login rate limit.
- **Migration:** server Postgres `PushDevices` (table, unique `Token` and `SessionId`, index on `UserId`, cascades from `Sessions` and `Users`).

## 22b. APNs and FCM senders

**APNs (`ApnsSender`):**
- HTTP/2 only (`HttpRequestMessage.Version = 2.0`, `VersionPolicy = RequestVersionExact`) to `api.push.apple.com` or `api.sandbox.push.apple.com` by the device's `Sandbox`.
- Token auth: an ES256 JWT (`kid` = key id, `iss` = team id, `iat`), signed with the .p8 key through `ECDsa.ImportFromPem`. Reused for 40 minutes (Apple refuses one older than an hour and dislikes new ones more often than every 20 minutes); a 403 `ExpiredProviderToken` makes a new one and retries once.
- Headers: `apns-topic` = the bundle id, `apns-push-type: alert`, `apns-priority: 10`, `apns-expiration`, `apns-collapse-id`. Body: `aps.alert` (title, body), `aps.sound = default`, `aps.thread-id = friends`, and `kind` / `sender` for the tap (first planned as `from`, which FCM refuses: see 22e as built).
- 410, and 400 `BadDeviceToken` / `DeviceTokenNotForTopic` → `DeadToken`. Anything else → `Failed`, logged with the reason.

**FCM (`FcmSender`):**
- HTTP v1: `POST https://fcm.googleapis.com/v1/projects/<project>/messages:send`.
- OAuth access token from the service account (scope `https://www.googleapis.com/auth/firebase.messaging`), through `Google.Apis.Auth` (`GoogleCredential.FromJson(...).CreateScoped(...)`), which caches and refreshes it. The project id comes from the service account JSON.
- Message: `notification` (title, body), `data` (`kind`, `sender`), `android.ttl`, `android.collapse_key`, `android.notification.channel_id = friends`, `tag`, `icon = ic_notification`.
- 404 `UNREGISTERED`, and 400 `INVALID_ARGUMENT` about the token → `DeadToken`. Anything else → `Failed`.

**Settings (`PushSettings`, all optional, read like `InviteSettings`):**
- `Push__Apns__KeyId`, `Push__Apns__TeamId`, `Push__Apns__PrivateKey` (the .p8 file's text) and `Push__Apns__BundleId` (default `com.gainstreak.app`). All three of the first, or none.
- `Push__Fcm__ServiceAccountJson` (the service account key file's text).
- A key that doesn't parse, or half the APNs settings, stops startup with a message naming the setting (never its value).

**A test command:** `dotnet GymApp.Server.dll push-test <username>` (and the image's `push-test` arg, like `suspend` in `AccountCommands`) sends "Test notification from GainStreak" to that user's devices and prints the result per device (platform, sandbox, sent / dead / failed and why), never the token. It's how a deploy checks the keys.

**Tests:** the JWT (header, claims, verifies with the test key's public half, reused, renewed after 40 minutes); APNs requests against a fake handler (host by sandbox, headers, body); the 410 / 400 / 403 handling; FCM requests against a fake handler and a fake credential (URL with the project id, body); `DeadToken` mapping; settings parsing (none, all, half, malformed); `push-test` against fakes.

### 22b as built (2026-11-13)

- **`ApnsSender`** (`GymApp.Server/Push`): HTTP/2 only, the JWT signed with `ECDsa` in the IEEE P1363 form JWS wants, reused for 40 minutes (and remade if the clock goes backwards). Dead: 410, and `BadDeviceToken`, `DeviceTokenNotForTopic` or `Unregistered`. One retry with a new JWT after `ExpiredProviderToken`. The body puts `kind` and `sender` at the top level, next to `aps`.
- **`FcmSender`**: Google.Apis.Auth 1.78.0, through `CredentialFactory.FromJson<ServiceAccountCredential>` (the older `GoogleCredential.FromJson` is obsolete). Getting the token sits behind `IFcmAccessTokens`, so the tests need no Google. Dead: `UNREGISTERED`, `SENDER_ID_MISMATCH` (another project's token), or a 400 whose message is about the registration token. Messages go with `priority: high`, so a backgrounded phone shows them at once.
- **Both HTTP clients** time out after 5 s and have no request logging (the APNs URL holds the device token). Neither sender logs a token.
- **`PushSettings`**: Google's library also parses the service account's private key at startup, so a damaged key stops startup with the setting's name, not on the first push. Error messages never quote the value.
- **`push-test <username>`** (`PushCommands`, next to `suspend` in `Program.cs`): sends the friend-request kind with the text "Test notification from GainStreak.", so the phone handles it like a real one. `PushMessage` gained an optional `Text` for it. It prints each device's id, platform, sandbox and result. Exit code 0 only if at least one device got it.
- **README:** the `Push__*` settings in the server table, and "Checking push notifications" with `push-test`. The phone-signing steps (Firebase file, Push capability) come with 22e.
- **`ServerTest.Settings`**: test classes can now give the server more settings (through `UseSetting`, since `Program` reads them before the app is built).
- **Tests:** `PushSenderTests` (32: the JWT, APNs and FCM requests and refusals, settings) and, in `PushTests.cs`, `push-test` (2 more) and `PushConfiguredTests` (the server starts with both keys and registers both senders). Not checked against the real services: that needs the Apple key and the Firebase project (22e, by hand).

## 22c. App services (no UI yet)

**App SQLite (migration `AddFriendNotifications`):** `User.FriendNotifications` (`bool?`: null until asked, then the switch) and `User.FriendNotificationsCardDismissed` (`bool`). Regenerate the compiled model.

**Platform:** `IPushNotifications` in `GymApp.App`, from `IPlatform.CreatePushNotifications()` (desktop and tests: `NoPushNotifications`):
- `IsSupported` (false on the desktop and on an Android build without Firebase).
- `PermissionState` (allowed / denied / not asked) and `RequestPermissionAsync()`: the same permission as Reminders; share the head's code with `INotificationScheduler` rather than asking twice.
- `GetTokenAsync()` → `PushToken(PushPlatform, Token, Sandbox)` or null, and a `TokenChanged` event.
- `ShowInForeground(title, body, kind, from)` for Android's foreground case.

**Client and services:**
- `SocialApiClient.RegisterPushDeviceAsync(request)` and `UnregisterPushDeviceAsync()`, Ok / Refused / Offline like the rest.
- `IPushRegistration` in `GymApp.Services`, registered in both wirings:
  - `RemotePushRegistration`: registers when signed in, the switch is on and there's a token; again when the token changes, after every sign-in (a new session has no device) and once a day on resume. The last token sent, with the session it was sent for, is kept in `SocialCacheStore`, so an unchanged token isn't sent again. Off → `DELETE`. Offline → tries again on the next resume or sync, like `SocialSync`. Logout and account deletion need no call (the server's cascade), but clear the cached token.
  - `LocalPushRegistration`: no-op (Local mode has no server to push from).
- `PushRouting.Open(kind)` in `GymApp.App`: `FriendRequest` → `MainViewModel.ShowInbox()` with the Requests filter; `FriendAccepted` / `InviteAccepted` → `ShowFriends()`. `GymApplication.OpenFromPush(kind)` waits for the shell like `OpenLink` (`_linkBeforeShell`), and after sign-in if no one is signed in (a push for a signed-out account is dropped: a different user may sign in).
- `GymApplication.PushArrived(kind)` (foreground): refreshes the Inbox badge and, when the current screen is Inbox or Friends, reloads it; otherwise asks the head to show it.

**Tests:** `RemotePushRegistration` against `FakeSocialServer`: registers after sign-in, not when off or unsupported, again on a token change and after a new sign-in, not again when unchanged, `DELETE` when switched off, offline then online, cleared on logout; Local wiring is a no-op; `PushRouting` for each kind, before the shell and before sign-in; `PushArrived` on Inbox, Friends and elsewhere; `ClientContractTests` for both calls; `CompiledModelTests`.

### 22c as built (2026-11-15)

Where it differs from the plan above:
- **`IPushNotifications` is in `GymApp.Services`, not `GymApp.App`** (`PushNotifications.cs`, with `PushPermission`, `PushToken` and `NoPushNotifications`), because the registration service there needs it. It has no `ShowInForeground`: the heads ask `GymApplication.PushArrivedAsync(kind)` whether to show a push themselves (22e). `IPlatform.CreatePushNotifications()` defaults to none; `PhonePlatform` keeps that until the heads add theirs in 22e.
- **One registration service per wiring, sharing a base** (`PushRegistration.cs`): `PushRegistrationBase` holds the switch, the card and the "already allowed → on" start. `RemotePushRegistration` adds the server; `LocalPushRegistration` sends nothing. Both are singletons. `IPushRegistration` has `GetStateAsync` (supported, switch, card dismissed, permission), `SetEnabledAsync` (`On` / `Off` / `Refused`), `DismissCardAsync`, `RequestSync` / `SyncAsync` and `WhenIdleAsync`. Errors go to errors.log ("Push").
- **What's cached:** `SocialCacheKeys.PushDevice` keeps the last token sent and a 16-character SHA-256 of the session token it was sent with, so a new sign-in registers again. The session token itself isn't kept twice. A removed registration keeps the row with a null token.
- **Turning off** removes the token only if this session registered it: an older session's went with that session on the server. **Permission taken away in Settings** removes it too, and the switch stays on, so allowing again brings them back.
- **A refusal (a 404 from a server before 0.22.0) isn't cached**, so the next resume tries again, as the compatibility review asked. Offline, the same.
- **Triggers** (`GymApplication.WirePush`, phones only): `SessionService.Changed` (sign-in), `IPushNotifications.TokenChanged`, and every return to the foreground. The daily re-send rides on the foreground one.
- **Routing:** `MainViewModel.OpenFromPush(kind)` (signed out: dropped) and `PushArrived(kind)`. `ShowInbox(requests: true)` opens on the Requests filter. `FriendsViewModel.RefreshAsync()` reloads Friends in place. `GymApplication.OpenFromPush(string? kind)` takes the push's raw `kind` and waits for the shell at a cold start (`_pushBeforeShell`). `PushArrivedAsync` runs on the UI thread and returns whether to show.
- **App SQLite migration `AddFriendNotifications`:** `User.FriendNotifications` (`bool?`) and `User.FriendNotificationsCardDismissed`. Compiled model regenerated. The iOS Release build for the Simulator waits for the end of the phase (CLAUDE.md's checks).
- **Tests:** `PushRegistrationTests` (16), `PushRoutingTests` (5), `AppServicesTests` (the Remote wiring) and `ClientContractTests.PushDevice_RegisterAndUnregister` (the real client against the real server).

## 22d. The UI

- **Profile → Notifications** (the section that holds Reminders, renamed if the artboard says so): the **Friend requests** switch, hidden when `IsSupported` is false. Turning it on asks for permission if not asked yet; refused → back off with Reminders' note. Every change saves and registers or unregisters at once.
- **The card in Friends** (`FriendsViewModel`): shown while `FriendNotifications` is null, push is supported, permission isn't allowed yet, the card wasn't dismissed, and the user has sent or received a request or opened the QR sheet. Turn on → permission → switch on (or the refusal note). Not now → dismissed for good.
- **Permission already allowed** (from Reminders): no card; `FriendNotifications` becomes true at startup and the token is registered.
- Colors from `IronlogResources.axaml` in both themes; any segmented control uses `SegmentedPill`.

**Tests:** the switch (on asks, refusal, off unregisters, hidden on the desktop); the card's show rules and both buttons; the "already allowed" start; the desktop shows neither.

### 22d as built (2026-11-15)

- **Design approved** (2026-11-15) on the canvas's new row **Notifications · Push (Phase 22)**: Profile · Notifications, Friends · Turn on notifications (card), and Notifications · lock screen and banner.
- **Per-event titles** (the user's choice on the canvas): "Friend request", "Request accepted" and "New friend" instead of "GainStreak", since Android already shows the app's name in the header. `PushMessage.Title` on the server; `push-test` keeps "GainStreak". This is a server change (both senders' tests updated).
- **Profile:** the Reminders card is now a **Notifications** card. It holds Reminders (unchanged apart from a smaller heading) and, under a divider, **Friend requests** (`FriendNotificationsViewModel`), each shown only where it works. The card shows when either does (`ProfileViewModel.HasNotifications`), so the desktop shows none, as before. A note under the switch says the notifications go through Apple or Google with the friend's username, on this phone only. Turned on but the permission taken away in Settings: the switch shows off with the note.
- **Friends' card:** `FriendPushCardViewModel` (`FriendsViewModel.Push`), above the leaderboard after the invite card. Friends asks it to consider showing after loading friends or requests (any friend, incoming or outgoing request), and when the QR sheet opens. Sending a request reloads Friends, which covers that case. "Turn on" asks the phone, then says "Friend notifications are on. You can turn them off in Profile." or points to Settings. "Not now" is for good. After an answer the switch is decided, so the card doesn't come back either.
- **New icon** `IcBellOff`.
- **Not checked on a phone yet:** push isn't supported on either head until 22e, so neither the row nor the card shows there yet. They're checked by hand in 22e.
- **Tests:** `PushUiTests` (9: the switch, refusal, permission taken away, the card's show rules, Turn on, refusal, Not now, already allowed, no push). `FakePlatform.Push` lets a test give the app a phone that can receive pushes.

## 22e. Heads

**iOS:**
- `Entitlements.plist`: `aps-environment` (`development` for Debug, `production` for Release; set it from the build configuration in the csproj, as the Simulator needs it for signing). Turn on **Push Notifications** for the App ID and make new profiles (the user; list the steps).
- `AppDelegate`: `UIApplication.SharedApplication.RegisterForRemoteNotifications()` at start when the permission is allowed (and after it's granted); `RegisteredForRemoteNotifications(NSData)` → hex token → `TokenChanged`; `FailedToRegisterForRemoteNotifications` → errors.log ("Push").
- `Sandbox` = true for Debug builds, false for Release (`BuiltSettings`).
- `ReminderDelegate` becomes `NotificationDelegate`: a push (it has `kind` in its user info) → `PushArrived` / `OpenFromPush`; a reminder or the workout nudge → as before.
- Check payload handling with `xcrun simctl push booted com.gainstreak.app <file>.apns` (no Apple key needed), then real sandbox pushes on the Simulator with the key (Apple silicon Macs get real tokens there).

**Android:**
- `Xamarin.Firebase.Messaging` (the .NET for Android binding), with `google-services.json` from `GAINSTREAK_GOOGLE_SERVICES_JSON` (a path, like the keystore variables) as a `GoogleServicesJson` item. Without the variable, the build leaves Firebase out of the app's start-up and the platform says unsupported.
- `FirebaseMessagingService` subclass (`[Service]`, `com.google.firebase.MESSAGING_EVENT` intent filter): `OnNewToken` → `TokenChanged`; `OnMessageReceived` (foreground) → `PushArrived`, and posts the notification itself unless on Inbox or Friends.
- A **Friends** notification channel (next to Reminders), and the manifest's `default_notification_icon` (`ic_notification`) and `default_notification_channel_id` (`friends`) for pushes shown while the app is in the background.
- A tapped push opens `MainActivity` with the data as intent extras: `OnCreate` / `OnNewIntent` read `kind` → `OpenFromPush`, next to the reminder extra.
- Check the size added by Firebase (Phase 16a measured ~24 MB download) and that a Release build with trimming still receives.
- Test on an emulator with a **Google Play** system image (FCM needs Play services).

**Checked by hand on each platform, against a local server with the keys in user secrets:** the permission paths; a request, an accept and an invite each arriving with the app closed, in the background and open (on Friends, and elsewhere); each tap opening the right screen, cold and warm; logout stopping them; the switch off stopping them; a second account on the same phone not getting the first's.

### 22e as built (2026-11-15)

**iOS:**
- **`aps-environment`** comes from `CustomEntitlements` items in `GymApp.iOS.csproj`: `development` for Debug, `production` for Release. One `Entitlements.plist` stays for the rest. The Simulator's signature carries it (checked in `Entitlements-Simulated.xcent`).
- **`IosPushNotifications`:** registers with APNs at every start, whatever the permission (iOS gives a token anyway; alerts show only once allowed), so the token is ready when the switch goes on. The token is hex and **sandbox** in Debug builds and any build on the Simulator. `GetTokenAsync` waits up to 10 s for APNs' answer. `AppDelegate` exports `application:didRegisterForRemoteNotificationsWithDeviceToken:` and `…didFailToRegister…`, since Avalonia's delegate has neither.
- **`NotificationDelegate`** (was `ReminderDelegate`): a notification with a `kind` is a push. In the foreground it asks `GymApplication.PushArrivedAsync` whether to show (banner, list and sound, or nothing). A tap calls `OpenFromPush`. Reminders and the workout nudge behave as before.
- **Checked on the iPhone 18 Pro Simulator (iOS 27.1, Debug) against the local server:**
  - Profile shows the Notifications card. Turning on Friend requests shows iOS's prompt. After Allow, the server has the session's token (APNs, 160 hex characters, sandbox).
  - A simulated "Friend request" push (`xcrun simctl push`):
    - shows as a banner over Profile, with the per-event title;
    - shows nothing on the Inbox;
    - a tap on the banner opens the Inbox on Requests.
  - A "Request accepted" push to the app in the background, tapped from Notification Center, opens Friends.

**Android:**
- **`Xamarin.Firebase.Messaging` 125.1.3.** `google-services.json` comes from `GAINSTREAK_GOOGLE_SERVICES_JSON` or a gitignored file next to the project. The package is always in; without the file Firebase fails to start (it logs "FirebaseApp initialization unsuccessful") and `AndroidPushNotifications.IsSupported` is false, so the app hides the feature. No conditional compilation.
- **`AndroidPushNotifications`:**
  - The permission is the reminders' `POST_NOTIFICATIONS`. "Not asked" vs "denied" on Android 13+ comes from a stored flag, since Android can't tell them apart.
  - The token comes from `FirebaseMessaging.GetToken()`.
  - The **Friends** channel has high importance.
- **`PushMessagingService`** (`FirebaseMessagingService`): `OnNewToken` → `TokenChanged`. `OnMessageReceived` (foreground only) asks `PushArrivedAsync`, then posts the notification itself with the same channel, tag and `kind` extra. The manifest names `ic_notification` and the `friends` channel for pushes Firebase shows in the background.
- **Taps:** `MainActivity.Opened` reads the intent's `kind` extra (Firebase puts the data there) next to the reminder extra.
- **Binding quirks:** `GetToken()` and `OnNewToken` are marked obsolete by the binding only; the warnings are silenced with a comment.
- **Checked on the Pixel emulator (google_apis image, Debug):** without `google-services.json` the app installs, starts and reaches the login screen. Firebase logs that it isn't configured, and nothing crashes. Signing in and looking at Profile was cut short by a slow emulator after a cold boot; `PushUiTests.WithoutPush_NeitherShows` covers that case.
- **Not checked yet:** a real APNs delivery (needs the paid Apple account), the size Firebase adds to the APK, and a Release (trimmed) Android build receiving. Real FCM was checked on 2026-11-17 (below). These go to the end-of-phase checks and the beta checklist.

**Real FCM, checked 2026-11-17** (Firebase added to `gainstreak-beta-1234`, the Android app registered, `google-services.json` and the service account key in `~/keys`, the `push-fcm-account` secret created):
- **Bug found and fixed:** the data key `from` is reserved by FCM. Every Android push was refused with 400 "Invalid data payload key: from", which the fake-server tests couldn't see.
  - The key is now `sender` (`PushPayload.Sender`) on both platforms.
  - `PushPayload.FcmReservedKeys` and `PushSenderTests.TheDataKeys_AvoidFcmsReservedOnes` keep it from coming back.
  - `FcmSender` now logs FCM's error message (it never contains the device token).
- **The earlier emulator check had run stale code:** a Debug "fast deployment" folder (`files/.__override__`) from an earlier phase was still on the emulator, and `adb install` doesn't replace it. The first start with Firebase configured crashed with `TypeLoadException: GymApp.Android.PushMessagingService`.
  - Uninstalling, and building with `-p:EmbedAssembliesIntoApk=true`, fixed it.
  - For hand checks, install with `dotnet build -t:Install`, or embed the assemblies.
- **Then, on the Pixel emulator (Debug, `google-services.json`, local server with `Push__Fcm__ServiceAccountJson`):**
  - Firebase starts, and with notifications already allowed the switch turned on by itself. The server got the session's FCM token.
  - A friend request through the API reached the closed app as "Friend request · pushalice wants to be your friend." on the Friends channel, with tag `friendRequest-1`.
  - `push-test pushbob` printed "Fcm, sent", and the test notification arrived.
  - Tapping the request opened the Inbox on Requests, with the request and Accept.
- **Still not checked:** APNs for real. It needs the paid Apple Developer Program, which isn't set up yet.

**`.gitignore`:** `google-services.json` and Firebase service account key files (the `.p8` was already ignored). **README:** the Firebase file under phone signing, and Push Notifications (with Associated Domains, which was missing) in the iOS App ID step.

## 22f. Privacy, store and release

- **`site/privacy.html`** (new "Last updated" date) and **`docs/STORE_LISTING.md`:**
  - A row in "Sent to the GainStreak server": the push token, per signed-in phone, to deliver friend notifications; deleted on logout, account deletion, switching off, or when Apple or Google say it's dead.
  - "Other services": Apple's push service and Firebase Cloud Messaging (Google) carry the notification: the sender's username and the event, plus the token.
  - "Your choices": Profile → Notifications → Friend requests, and the phone's own notification settings.
- **Play Data safety:** Device or other IDs (the push token): collected, required for the feature (optional overall), app functionality. FCM is a service provider, so not "shared". **App Store privacy:** Identifiers → Device ID (App Functionality), linked to the user. **Decision for the user:** confirm these two answers before entering them.
- **README:** the `Push__*` settings in the server settings table, `GAINSTREAK_GOOGLE_SERVICES_JSON` under phone signing, Push Notifications in the iOS App ID steps, and `push-test` next to the account commands.
- `Directory.Build.props` → 0.22.0. `CHANGELOG.md`: "**Friend notifications.** …", ending with `**Migration:** server Postgres (PushDevices) and app SQLite (AddFriendNotifications).`

**Before deploying** (the user does these; each needs an account):
1. **Apple** (developer.apple.com): Certificates, Identifiers & Profiles → Keys → **+**, tick **Apple Push Notifications service (APNs)**, download the `.p8` (only once) and note the **Key ID**. The Team ID is on the Membership page (the same one Phase 21's `Invites__AppleTeamId` needs). Turn on **Push Notifications** for the App ID `com.gainstreak.app`, then make the development and App Store profiles again.
2. **Firebase** (console.firebase.google.com): a project (it can be the existing Google Cloud project `gainstreak-beta-1234`), an Android app with package `com.gainstreak.app` and both SHA-256 fingerprints, download `google-services.json` and keep it outside the repo. Project settings → Service accounts → **Generate new private key** (a JSON file); or create a key for a service account with the **Firebase Cloud Messaging API Admin** role. Make sure the **Firebase Cloud Messaging API (V1)** is enabled.
3. **Secret Manager** (no value in a shell history or the repo; `--data-file` reads the file):
   ```bash
   gcloud secrets create push-apns-key --data-file=AuthKey_KEYID.p8 --project gainstreak-beta-1234
   ```
   ```bash
   gcloud secrets create push-fcm-account --data-file=firebase-service-account.json --project gainstreak-beta-1234
   ```
   Give `gymapp-server@…` the Secret Accessor role on both, as for `social-db`.

**Deploy order:**
1. Build the amd64 image and push it, as for earlier releases.
2. Migrate (the one-off Cloud Run job). The server migration only adds the `PushDevices` table.
3. Deploy with `--update-secrets Push__Apns__PrivateKey=push-apns-key:latest,Push__Fcm__ServiceAccountJson=push-fcm-account:latest` and `--update-env-vars Push__Apns__KeyId=…,Push__Apns__TeamId=…`. `MinClientVersion` unchanged.
4. Check: the server starts (a bad key stops it, so a failed revision means a settings problem), then `push-test` on a tester's account (as a one-off job with the image's `push-test` arg) once a 0.22.0 phone is registered.
5. Release the phones (0.22.0): TestFlight builds use APNs production, so check with a TestFlight build, not only a Debug one.

**After the release, on real phones** (added to the beta checklist): the three notifications on a real iPhone and a real Android phone, closed and open; taps cold and warm; logout and the switch stopping them.

### 22f as built (2026-11-15)

- **`site/privacy.html`** (dated 15 November 2026):
  - A row for the push token (only while friend notifications are on; deleted on turning them off, logout, account deletion, or when Apple or Google say it's dead).
  - "How long it's kept": the token goes with its session.
  - "Other services": what Apple's push service and FCM carry (the title, and the sentence with the friend's username).
  - "What isn't collected": the push token is now the only phone-specific value.
  - "Your choices": the Friend requests switch, and that it starts on if Reminders already allowed notifications.
  - Also fixed (outside push, at the user's request): emails now say they go through **Resend** from `noreply@gainstreak.app` (since 11 November), not Google. `docs/STORE_LISTING.md` says the same.
  - After changing `site/`, copy it to the releases repository's `docs/` and push (see `docs/STORE_LISTING.md`, Public pages).
- **`docs/STORE_LISTING.md`:**
  - A row for the push token, and a paragraph on what APNs and FCM carry.
  - **Play:** Device or other IDs (collected, not shared, optional, app functionality).
  - **App Store:** Identifiers → Device ID (App Functionality).
  - Both are marked "to confirm before entering" (decision 5 in the report).
  - App Review notes for notifications, a store description line, and two checklist items (the Push capability and Firebase).
- **0.22.0** in `Directory.Build.props`. The `CHANGELOG.md` entry ends with `**Migration:** server Postgres (PushDevices) and app SQLite (AddFriendNotifications).`

**End-of-phase checks (2026-11-15):**
- **Tests:** the full `dotnet test` passes (1638 app, 287 server on Postgres in Docker).
- **iOS:** the Release build for the Simulator (full AOT) builds and starts to the login screen.
- **Android:** the Release build makes the `.aab` and `.apk` (about 52 MB each, signed with the local debug key, since no upload key was set). The size Firebase adds isn't measured: there's no 0.21 Release APK here to compare with.
- **Desktop:** builds and starts on a fresh data folder, applying every migration through `AddFriendNotifications`, with nothing in errors.log.
- **`server-compat-reviewer`:** no breaks. `MinClientVersion` stays at 0.18.0.
- Both Release builds took about an hour each on this Mac, far longer than usual (other builds were running).

**Before deploying** (the user, as in "Before deploying" above): the APNs key and Key ID, Push Notifications on the App ID and new profiles, the Firebase project with the Android app, `google-services.json`, the service account key, and the two secrets in Secret Manager with `gymapp-server@…` given access.

**Deploy order:**
1. Build the amd64 image and push it, as for earlier releases.
2. Migrate with the one-off Cloud Run job (the image's `migrate` arg). It only adds the `PushDevices` table, so 0.21.x keeps running on it.
3. Deploy with `--update-secrets Push__Apns__PrivateKey=push-apns-key:latest,Push__Fcm__ServiceAccountJson=push-fcm-account:latest` and `--update-env-vars Push__Apns__KeyId=<key id>,Push__Apns__TeamId=<team id>`. `MinClientVersion` stays at 0.18.0. A revision that doesn't start means a malformed `Push__*` value: the log names the setting.
4. Build the phones with `GAINSTREAK_GOOGLE_SERVICES_JSON` set (`build/release-mobile.sh`), and upload: TestFlight and Play internal testing.
5. On a tester's account with a 0.22.0 phone registered, run `push-test <username>` as a one-off job with the same secrets. Each device should say "sent".

**After the release, on real phones** (for the beta checklist):
- **iPhone (TestFlight, so APNs production) and Android (Play internal):** each of the three notifications arrives with the app closed, in the background and open (on Friends, and elsewhere).
- Taps open the right screen, cold and warm.
- Logging out stops them; so does the switch.
- A second account on the same phone doesn't get the first's.
- **Android:** the Friends channel shows in the app's notification settings.

## Later (not this phase)

- **Other events:** a share received (split or workout, opens the Inbox), friends' check-ins ("maya trained at Iron Gym"), a friend passing you on the weekly leaderboard, a streak at risk sent from the server (the phone's local reminders cover this today). Each needs its own switch and a rule against noise.
- **Generic text filled in on the phone:** a Notification Service Extension on iOS (and a data-only FCM message on Android) that fetches the details itself, so Apple and Google see no username.
- **Per-event switches** and quiet hours.
- **App icon badge** with the Inbox count (`aps.badge`, Android's notification dot), kept in step as requests are read.
- **Withdrawing a delivered notification** when the request is cancelled or declined elsewhere (a silent push that removes it).
- **A delivery queue with retries** (Cloud Tasks or a small table plus a scheduled job) if in-request sending loses too many pushes when APNs or FCM are slow.
- Huawei phones without Google services (HMS Push Kit).
