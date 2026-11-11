# Phase 21: Add friends with a QR code — spec for Claude Code

This is the spec for GymApp Phase 21 (version 0.21.0). A user can show a QR code in Friends, with **Copy link** under it. A friend who scans it with the phone's camera is taken into GainStreak, straight to Friends, where they confirm: "**alex** wants to be your friend". If they don't have the app yet, the link takes them to the App Store or Google Play. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- Checks follow CLAUDE.md's "Checks, for every phase": `dotnet build` and the app tests after each sub-phase; the full `dotnet test`, both mobile heads (Android emulator and iOS Simulator, Release) and the desktop app at the end of the phase. 21a changes `GymApp.Contracts` and the server, so it also runs the server tests right away. After each sub-phase, report:
  - which files you created
  - which files you changed or moved
  - which migrations you added, and to which database (this phase has one on each: server Postgres and app SQLite)
  - what you checked by hand on each platform, and what you couldn't check
- After any change to the app's entities, regenerate the compiled model (`dotnet ef dbcontext optimize`, see CLAUDE.md) and test the iOS Simulator with a **Release** build.
- Read `FriendService`, `ISocialStore` / `ISocialSession` and both stores (`LocalSocialStore`, `PostgresSocialStore`), `FriendEndpoints`, `RateLimits`, `Contracts/Friends.cs`, `SocialApiClient`, `RemoteFriendService` and its cache, `AppServices` (both wirings), `FriendsViewModel` and `FriendsView`, `InboxViewModel` (the incoming request rows), `MainViewModel` (`ShowFriends`, sign-in and `IsSignedInScreen`), `IPlatform` and both phone platforms (`OpenStorePage`), `AppDelegate`, `MainActivity`, `MobileHead.targets` (`BuiltSettings`), `tests/Shared/FriendServiceTests.cs`, `FriendEndpointTests`, `RemoteSocialEndToEndTests`, `ClientContractTests` and `site/privacy.html` before changing anything.
- Every existing test must still pass.
- Run the `server-compat-reviewer` agent at the end of 21a and again before the release.

## Decisions already made (don't change these)

1. **The camera app scans, not GainStreak.** The QR code holds a normal web link. There's no scanner inside the app in this phase.
2. **Showing the code is the request.** The person who shows the QR has already agreed, so when the scanner taps **Accept** the two become friends at once. The person who showed it doesn't confirm again.
3. **The link carries an invite code, never the user id.** A user id in a link would let anyone friend anyone by counting. The code is random, belongs to one user, and can be reset, which kills the old link and QR.
4. **Every existing friend rule still holds:** not yourself, blocks (a blocked inviter looks like a code that doesn't exist), `MaxFriends` on both sides. Already friends: the card says so and offers nothing. A pending request either way is accepted by the invite.
5. **Backward compatible.** Only new endpoints and new contract records. No change to existing endpoints, no `MinClientVersion` bump. Old builds simply don't show the QR button.
6. **Both wirings.** The invite rules live in `FriendService` over `ISocialSession`, so TestBench's Local mode and the server share them. That needs a migration on both databases.
7. **The desktop app looks and behaves as before**, apart from the new **Show my QR** button in Friends. It has no deep links. Copy link works there.

## Defaults to confirm (change here if wrong)

Confirmed by the user on 2026-11-11.

- **Domain: `gainstreak.app`**, mapped to the Cloud Run service. The link is `https://gainstreak.app/add/<code>`. It's baked into printed QR codes and into the iOS and Android link setup, so it must be the long-lived domain, not the `run.app` host. Add it to `AllowedHosts`. The link's host comes from a server setting (`Invites__BaseUrl`) and is returned by the API, so the app never builds it itself.
- **The code:** 10 characters from an alphabet without look-alikes (`23456789abcdefghjkmnpqrstuvwxyz`), from a cryptographic random source. Unique index.
- **One active code per user, no expiry.** It's made the first time the user opens the QR sheet. **Reset link** in the sheet makes a new one, after a confirm: "Your old QR code and link will stop working." (Expiring codes would break QR codes people saved or printed, and the reset covers a leak.)
- **Rate limits:** a new `Invite` policy for looking up and accepting codes, 30 per minute per user. Reset: 10 per hour per user.
- **Not signed in when the link opens:** the code is kept in memory (not on disk) until sign-in or sign-up finishes, then Friends opens with the card. If the app is closed before that, it's gone; opening the link again works.
- **Offline:** the sheet shows the last link from the cache with "Offline: this is your saved link." A first-time user who is offline sees "Connect to the internet to get your link." The confirm card offline: "Couldn't check this invite. Try again when you're online."
- **The landing page (`/add/{code}`)** is plain HTML from the server, in the style of `site/`. iPhone and iPad go to the App Store, Android to Google Play, and anything else sees the QR again with both store badges. Before the store releases (no `AppStoreId` yet), it links to the TestFlight public link and the Play testing link from server settings (`Invites__AppStoreUrl`, `Invites__PlayStoreUrl`). The page never says whose code it is (no username leaks to people without an account).
- **QR drawing:** the `QRCoder` package (managed code, works under AOT), error correction M, always black on white with a quiet zone, even in dark mode, about 240 px, with the username under it.
- **No referrer magic yet.** If the scanner has to install the app first, the code doesn't survive the install. The landing page says "Already installed? Open the link again." See "Later".

---

## Design first

The screens are on the **GainStreak Mobile** design canvas (claude.ai artifact "GainStreak Mobile"), row **Friends · QR (Phase 21)**:
- **Friends · Your QR code (sheet)**: the new **Show my QR code** row under the username search in Add friends, and the bottom sheet it opens. The sheet has the QR on white, `@username`, the link, **Copy link** ("Link copied" for 2 seconds), **Share** and **Reset link** with its confirm. The Scenario tweak shows open, copied, reset, offline with a saved link, offline with none, and closed.
- **Friends · Invite from a QR code**: the confirm card at the top of Friends. The Scenario tweak shows invite, checking, accepted, already friends, link doesn't work, you blocked them, and offline (with Try again).
- **Invite link on a computer** (removed from the canvas after 21c, at the user's request: `InviteLinkEndpoints.Page` on the server is now its source): the `/add/<code>` page in the app's colors. It has the QR code, the link, both store buttons (the App Store tweak shows "coming soon") and Privacy and Terms links. It never shows whose code it is.

The user approves them before 21c starts. Where the artboards and this spec disagree, ask.

## 21a. Invite rules, server and contracts

**Rules (`GymApp.Social`):**
- `FriendInvites` in `GymApp.Social`: `CodeLength = 10`, the alphabet, `NewCode()`, `IsWellFormed(code)` (checked before any lookup).
- `ISocialSession` gets `GetInviteCodeAsync(userId)`, `SetInviteCodeAsync(userId, code, at)` (replaces the old one) and `FindInviteAsync(code)` → the owner's `SocialUser` or null.
- `FriendService` (and `IFriendService`):
  - `GetInviteCodeAsync(userId)`: the code, made on first use.
  - `ResetInviteCodeAsync(userId)`: a new code.
  - `LookUpInviteAsync(userId, code)`: the inviter (id, username) and the relation, or `UnknownUserError` for a malformed, unknown or blocked code, or your own code ("That's your own code.").
  - `AcceptInviteAsync(userId, code)`: the checks in decision 4, then saves an Accepted friendship (or accepts the pending one), `RequestedById` = the inviter. Returns `FriendResult` with `BecameFriends`.

**Server (Postgres migration `FriendInvites`):** table `FriendInvites` (`UserId` primary key, `Code` unique, `CreatedAt`), cascade-deleted with the account. `PostgresSocialStore` and `LocalSocialStore` implement the new session methods.

**Endpoints (`FriendEndpoints`):**
- `GET /v1/friends/invite` → `FriendInviteResponse(Code, Url)`
- `POST /v1/friends/invite/reset` → `FriendInviteResponse`
- `GET /v1/invites/{code}` → `InviteLookupResponse(UserId, Username, Relation)`; 404 for unknown
- `POST /v1/invites/{code}/accept` → `SendRequestResponse`; 404, 400 or 409 as `Refused` maps them

**Web routes (no sign-in):**
- `GET /add/{code}`: the landing page from Defaults. It doesn't look the code up.
- `GET /.well-known/apple-app-site-association`: `applinks` for `<TeamId>.com.gainstreak.app`, path `/add/*`, served as `application/json`.
- `GET /.well-known/assetlinks.json`: `com.gainstreak.app` with the SHA-256 of the Play app signing key and of the upload key. Both from settings.

**Tests:**
- `tests/Shared/FriendServiceTests`: code made once and kept; reset replaces it and the old one is unknown; accept makes friends with no request step; own code refused; blocked either way looks unknown; already friends; pending request either way accepted; `MaxFriends` on each side; malformed codes never reach the store.
- `FriendEndpointTests`: each endpoint, the 404s, the rate limit, the account delete removing the invite.
- The landing page sends iPhone, Android and desktop user agents to the right place; both `.well-known` files have the right content type and body.
- `ClientContractTests` for the new records.

### 21a as built (2026-11-11)

Where it differs from the plan above:
- **The app SQLite migration came here, not in 21b.** `LocalSocialStore` implements `ISocialSession`, so the new session methods needed the app's `FriendInvites` table (and the compiled model) right away. 21b no longer has a migration.
- **The new `FriendService` members aren't on `IFriendService` yet.** Adding them there means `RemoteFriendService` must implement them, which is 21b. The server calls `FriendService` directly.
- **`ClientContractTests` moves to 21b** with the client calls it exercises.
- **The landing page redirects phones with a 302** (iPhone, iPod, and iPad when it says so, to `Invites__AppStoreUrl`; Android to `Invites__PlayStoreUrl`). So the "Already installed? Open the link again." line from Defaults can't be shown there. Computers, iPads that send a Mac user agent, and iPhones before an App Store link is set get the page: QR code (QRCoder, SVG), the link, and both store links ("App Store: coming soon" while unset).
- **Settings** (`InviteSettings`, all optional; a malformed one stops startup): `Invites__BaseUrl` (unset: the request's own host), `Invites__AppStoreUrl`, `Invites__PlayStoreUrl` (default: the app's Play page), `Invites__AppleTeamId` (turns on the AASA file), `Invites__AndroidCertFingerprints` (`;`-separated; turns on `assetlinks.json`). Unset, both `.well-known` files are 404.
- **Errors:** unknown, malformed, reset or blocked-by-them codes all give `FriendService.UnknownInviteError` (404). Own code (400), "You blocked them" (400), already friends (400), friend limits (400), a conflict (409).
- **Rate limits:** `invite` (lookup and accept, 30 a minute per user) and `invite-reset` (10 an hour per user).

## 21b. App services and links (no UI yet)

**App SQLite:** done in 21a (see "21a as built").

**Client and services:**
- `SocialApiClient`: the four calls, returning Ok / Refused / Offline like the rest.
- `RemoteFriendService`: implements the new `IFriendService` members. The own link is cached in `SocialCacheStore` (it rarely changes), so the sheet works offline.
- Add the invite members to `IFriendService` (`FriendService` already has them) and register both wirings in `AppServices`.

**Links:**
- `InviteLink.TryParse(Uri)` in `GymApp.App`: `https://<any host>/add/<code>` with a well-formed code → the code. The host isn't checked here; the OS only hands the app links for its verified domain.
- `MainViewModel.OpenLink(Uri)`: called by the heads on the UI thread. A friend link sets `PendingInviteCode`; if signed in, `ShowFriends()` and the card loads. If not, the code waits until sign-in or sign-up finishes and is then handled the same way. Any other link is ignored.

**Tests:** `ClientContractTests` for the four calls; `InviteLink.TryParse` (good, wrong path, bad code, trailing slash, query string); `OpenLink` signed in and before sign-in; `RemoteFriendService` against `FakeSocialServer` including offline with and without a cached link; Local mode accept via `FriendService` on in-memory SQLite; `CompiledModelTests`.

### 21b as built (2026-11-11)

- **`InviteLink`, not `FriendLink`, and in `GymApp.Social`**, next to `FriendInvites`: `FriendLink` was already the friendship record, and the parsing is pure. `InviteLink.TryParse(Uri)` takes http and https, any host, `/add/<code>` with an optional trailing slash or query, case-insensitive. `InviteLink.For(invite)` is the server's link, or `https://gainstreak.app/add/<code>` in Local mode, where there's no server to give one.
- **`InviteCodeResult` gained `Url` and `IsStale`.** `FriendService` (Local mode) leaves `Url` null. `RemoteFriendService` fills it in from the server and caches it under `SocialCacheKeys.Invite`. Offline, it returns the cached link marked stale. With nothing cached, it returns the offline message. Lookup and accept check the code's shape first, so a mangled link sends nothing.
- **`MainViewModel`:**
  - `OpenLink(Uri)` returns true for an invite link. If the user is signed in, it shows Friends. Otherwise the first screen after sign-in becomes Friends, once.
  - `HasPendingInvite` and `TakePendingInviteCode()` let Friends take the code for its card exactly once. Leaving Friends without acting drops the card.
- **Extra test:** an end-to-end test where two `Phone`s use the real app services against the real server: one shows its code, the other accepts.

## 21c. The UI

- **Friends header:** **Show my QR** (icon button next to search on phones, a button on desktop).
- **QR sheet** (`FriendQrViewModel` / `FriendQrView`, opened like the other sheets): the QR, `@username`, the link in small text, **Copy link** ("Link copied" for 2 seconds, through `TopLevel.Clipboard`), **Share** on phones (the head's share sheet through a new `IPlatform.Share(string text)`; desktop: hidden), **Reset link** with its confirm.
- **Confirm card** at the top of Friends when `PendingInviteCode` is set: looks the code up, then "**alex** wants to be your friend" with **Accept** and **Not now**. Accept → "You and alex are now friends." and the list refreshes. Already friends → "You're already friends with alex." Unknown → "This invite link doesn't work any more. Ask for a new one." Not now or a result clears the code.
- Segmented controls, if any, use `SegmentedPill`; colors come from `IronlogResources.axaml` in both themes.

**Tests:** the view models (load, copy, reset confirm, each card state, offline). The desktop app looks as in 0.20 apart from the button.

### 21c as built (2026-11-11)

- **The sheet:** `FriendQrViewModel` with `FriendQrSheet` hosted in `FriendsView`, like the report dialog. It's a bottom sheet on phones and a centred card on the desktop. Back, Escape and a tap on the dimmed page close it.
  - The view does the copying, through the window's clipboard, and calls `Copied(ok)`. "Link copied" shows for `CopiedFor` (2 s).
  - Share comes from the new `IPlatform.CanShare`/`Share(text)`: `UIActivityViewController` on iOS, an `ActionSend` chooser on Android. The desktop has none, so Copy link takes the whole row there.
- **The QR code:** the `QrCodeView` control draws QRCoder's module matrix black on white, in whole pixels, in both themes. QRCoder is now in `GymApp.App` too.
- **The card:** `FriendsViewModel.ShowInviteAsync(code)`. `MainViewModel.ShowFriends` hands it the waiting code as it creates Friends, so going back to Friends never shows the card again, and Friends never resolves the shell while the shell is being built.
  - The states follow the artboard: checking, ask, accepted, already friends, link doesn't work, you blocked them, offline with Try again.
  - "You blocked them" names nobody: the server's refusal has no username.
- **New icons:** `IcQr`, `IcWifiOff`, `IcLinkOff`, `IcBlock`.
- **The landing page** (`InviteLinkEndpoints.Page`) follows the artboard: dark unless the browser asks for light, system fonts, and no outside requests, like `site/`. Privacy and Terms link to the GitHub Pages copies (`InviteLinkEndpoints.PublicPages`).

## 21d. Heads

**iOS:**
- `Entitlements.plist`: `com.apple.developer.associated-domains` = `applinks:gainstreak.app`. Turn on Associated Domains for the App ID in the developer portal (the user does this; list the steps).
- `AppDelegate`: `ContinueUserActivity` (Universal Links, warm start) and the launch options' user activity (cold start) → `OpenLink`.
- Delete the stale `AppManifest.plist` and `nativelibraries/GymApp.iOS` after the entitlement change (CLAUDE.md).
- `Share` through `UIActivityViewController`.

**Android:**
- `MainActivity`: `[IntentFilter]` for `ActionView`, categories `Default` and `Browsable`, scheme `https`, host `gainstreak.app`, path prefix `/add/`, `AutoVerify = true`; `LaunchMode = SingleTask`. Handle the intent in `OnCreate` and `OnNewIntent` → `OpenLink`.
- `Share` through `Intent.ActionSend` with a chooser.

**Checks:** on the Simulator, `xcrun simctl openurl booted https://gainstreak.app/add/<code>` opens the app, signed in and not. On the emulator, `adb shell am start -a android.intent.action.VIEW -d https://gainstreak.app/add/<code>`, and `adb shell pm get-app-links com.gainstreak.app` shows the domain verified. Real-phone checks (scan with the camera, app not installed → store) go on the user's list for the beta.

### 21d as built (2026-11-11)

- **One path for links, in shared code.** Avalonia 12 already turns an opened link into an `OpenUri` activation (`ProtocolActivatedEventArgs`): a Universal Link (`NSUserActivityTypeBrowsingWeb`) through its scene delegate on iOS, the activity's `ActionView` intent on Android. `GymApplication` handles it in `WireForeground` and calls `GymApplication.OpenLink`. A link that comes before the shell exists (a cold start) waits in `_linkBeforeShell` and opens with it. The heads have no link code of their own.
- **iOS:** `Entitlements.plist` has `com.apple.developer.associated-domains` = `applinks:gainstreak.app`. Signed builds need Associated Domains on the App ID and a profile made after it was added.
- **Android:** `MainActivity` has `LaunchMode = SingleTask` and an `AutoVerify` intent filter for `https://gainstreak.app/add/`.
- **Checked on the Android emulator** against the local server, with an explicit `am start -a VIEW -d https://gainstreak.app/add/<code> com.gainstreak.app`:
  - A cold start with nobody signed in waited through login, then opened Friends with the card.
  - Accept made the two friends.
  - A warm start from Home showed "You're already friends".
  - The QR sheet and Share (the system chooser) work.
- **Checked on the iOS Simulator:** the app carries the entitlement and starts. A Universal Link itself can't be checked until `gainstreak.app` serves the association file: on the beta, open an invite link from Notes or Messages on a real iPhone.
- **Not checked:** App Link verification (`adb shell pm get-app-links com.gainstreak.app`) also needs the domain and `assetlinks.json` live with the release key's fingerprint. Both checks move to 21e, after the deploy.

## 21e. Privacy, store and release

- `site/privacy.html` and `docs/STORE_LISTING.md`: the invite code (random, tied to the account, deleted with it, resettable); the landing page logs nothing beyond the usual request logs.
- Server settings for the release: `Invites__BaseUrl`, `Invites__AppStoreUrl`, `Invites__PlayStoreUrl`, the Team ID and the signing key hashes. The domain mapped in Cloud Run and in `AllowedHosts`.
- Deploy order: migrate, deploy the server, check both `.well-known` files from outside, then release the apps.
- `Directory.Build.props` → 0.21.0. `CHANGELOG.md`: "**Add friends with a QR code.** …", ending with `**Migration:** server Postgres and app SQLite (FriendInvites).`

### 21e as built (2026-11-11)

- **Privacy and store notes:** `site/privacy.html` (dated 11 November 2026) has a row for the invite code and, under your choices, how to reset it. `docs/STORE_LISTING.md` has the same row. The README's server settings table has the five `Invites__*` settings.
- **0.21.0** in `Directory.Build.props`, and a `CHANGELOG.md` entry.

**Before deploying** (the user does these; each needs an account or DNS access):
1. **Domain:** map `gainstreak.app` to the Cloud Run service `gymapp-server` (Cloud Run → Manage custom domains, or `gcloud beta run domain-mappings create --service gymapp-server --domain gainstreak.app --region europe-west1`), and add the DNS records it lists at the registrar. The certificate can take up to a day.
2. **Apple:** turn on Associated Domains for the App ID `com.gainstreak.app`, then make new provisioning profiles. The Team ID is on the developer account's Membership page.
3. **Google Play:** copy both SHA-256 fingerprints (app signing key and upload key) from Play Console → Test and release → App integrity.

**Deploy order:**
1. Build the amd64 image and push it, as for earlier releases.
2. Migrate with the image's `migrate` arg (the one-off Cloud Run job, as on 2026-10-30). The server migration only adds the `FriendInvites` table.
3. Deploy with the new settings. `AllowedHosts` keeps the run.app host and adds `gainstreak.app` (separated by `;`). Set `Invites__BaseUrl=https://gainstreak.app`, `Invites__AppStoreUrl` (the TestFlight public link), `Invites__PlayStoreUrl` (the Play testing link), `Invites__AppleTeamId` and `Invites__AndroidCertFingerprints`.
4. Check from outside: `https://gainstreak.app/.well-known/apple-app-site-association` and `https://gainstreak.app/.well-known/assetlinks.json` both answer 200 with `application/json` and no redirect, and `https://gainstreak.app/add/abcdefghjk` shows the page on a computer.
5. Then release the apps (0.21.0).

**After the release, on real phones** (added to the beta checklist):
- **iPhone:** an invite link tapped in Notes or Messages opens GainStreak, both when the app is closed and when it's open. Scanning the QR code from another phone's screen with the Camera app does the same.
- **Android:**
  - `adb shell pm get-app-links com.gainstreak.app` shows `gainstreak.app: verified`.
  - Scanning the QR code with the camera opens the app.
  - Without the app installed, the link goes to the Play testing page.

## Later (not this phase)

- **Keeping the code across an install.** Android: put `referrer=invite%3D<code>` on the Play link and read it with the Play Install Referrer API on first start. iOS has no clean equivalent without a service like Branch.
- A scanner inside the app (for desktop users or phones whose camera app doesn't open links).
- Codes that expire, or one-time codes, if invites leak in practice.
