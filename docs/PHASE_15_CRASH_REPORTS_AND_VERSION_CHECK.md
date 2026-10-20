# Phase 15: Crash reports and the version check

Getting builds into testers' hands. Testers need a way to send what went wrong. Once old builds are out there, the server needs a way to turn away a build that's too old.

## Decisions

1. **Crash reports stay local: no Sentry (for now).** `errors.log` already catches everything the global handler sees (Phase 12). What was missing was a way to send it. A report the user copies keeps the "nothing leaves the phone unless you choose" rule. It needs no account or SDK, and it works offline. Sentry (free tier) can be added later behind the same `ErrorLog` if automatic reports become worth the privacy note.
2. **The version travels in a header**, `X-App-Version: 0.15.0` (`ClientVersions.Header` in Contracts), on every call from `SocialApiClient`. The value is `AppVersion.Current` from Directory.Build.props.
3. **The server's minimum is a setting**, `MinClientVersion` (an environment variable in production). When it's unset, every build is accepted, so nothing changes until it's set. A value that isn't a version stops startup, like a missing connection string.
4. **No header counts as too old.** Builds from before 0.15 don't send one, and they are exactly the builds this exists to turn away. Only `/v1` is checked; `/health` isn't.
5. **426 Upgrade Required**, as ProblemDetails with the message in `detail` and `minVersion` as an extension. The check runs before sign-in, so an old build gets "please update" at login and never a 401 it would misread.
6. **Too old isn't signed out.** Workouts live on the phone, so a 426 is handled like offline:
   - sign-in and restore fall back to the cached account and keep the token for after the update
   - pending uploads keep their flags
   - friend screens show cached data, and friend actions show the server's message

   The difference from offline is the banner: the app shows the server's message at the top of the window and doesn't say "you're offline".

## What was built

### Crash reports
- `ErrorLog` (Services) takes `appInfo` ("GainStreak 0.15.0 on Darwin … (Arm64)", `AppVersion.Description`) and stamps it on every entry. It's registered in DI as one instance.
- `ErrorLog.BuildReport()`: a header (app and time), then `errors.old.log` and `errors.log`, oldest first. It keeps only the newest 100 KB and never throws.
- **Feedback** (after the canvas's Feedback artboard): its own Support section in the sidebar, under the screens, opens a popup over the current screen (`FeedbackViewModel`, hosted in `MainWindow`). Two tabs on the sliding pill, "Report a problem" and "Suggest an idea", each with its own note. **Send** goes through `IFeedbackService` (see "Feedback to Discord" below). A problem carries the error report unless "Attach the error report" is off; "See what's sent" shows it, scrubbed. A failed send keeps the note and offers "Copy instead" (`Compose`, to the clipboard). "Show log file" opens the data folder. The view model is a singleton, so a half-written note survives closing the popup. The global "Something went wrong" banner points there.

### Feedback to Discord
Decision 1 above still holds for errors: nothing is sent on its own. Feedback is sent only when the user presses Send, and it says so ("Sent to the developer with your username").
- Contracts: `FeedbackRequest` (kind, text, report) and `FeedbackRules`: the limits (5,000 characters of text, 110,000 of report) and `Scrub`, which replaces the user name in file paths (`/Users/<user>/…`, `/home/<user>/…`, `C:\Users\<user>\…`).
- Server: `POST /v1/feedback`, signed in only, 5 an hour per user (`RateLimits.Feedback`). It checks the request, scrubs it again (an old or other client may not have), stores a `FeedbackEntry` with the `X-App-Version` and answers 204. The table is the record. Rows are deleted with the account (cascade).
- `DiscordFeedbackForwarder`, a background service fed by `FeedbackQueue`, posts each new row to `Feedback__DiscordWebhookUrl`: an embed (title, note, user, app version, id, time) and the error report as `feedback-<id>.txt`. A note past 4,000 characters is cut in the embed and sent whole in the file. `allowed_mentions` is empty, so "@everyone" in a note is only text. A refused or failed post is logged and leaves `ForwardedAt` null (not retried). With no webhook set, feedback is only stored. A webhook that isn't https stops startup.
- Client: `SendFeedbackAsync`. Services: `RemoteFeedbackService` (checks and scrubs, then sends; not queued, so offline says so and the note stays) and `LocalFeedbackService` for the TestBench's local mode, which has no server.
- **Migration:** `AddFeedback` in the server's Postgres.

### Version check
- Contracts: `ClientVersions` (the header name, `minVersion`, and `Parse`, which ignores `-beta` and `+commit` suffixes).
- Server: `ClientVersionCheck` middleware and `ReadMinimum`, wired in `Program.cs` after the forwarded headers and before authentication.
- Client: an `appVersion` constructor argument, `ApiResult.IsUpdateRequired`, and the `UpdateRequired` event (it carries the server's message).
- Services: `AccountService` sign-in and restore, and `SocialSync`, treat a 426 like offline (see decision 6).
- Desktop: `MainViewModel.UpdateNotice` and an amber banner in `MainWindow`. `App` wires `UpdateRequired` to it, and a 426 during startup restore is held until the window exists.

### Tests
- GymApp.Tests: the header is sent; a 426 raises `UpdateRequired` and not `SessionExpired`; the fallback message; `ClientVersions.Parse`; sign-in and restore on a 426; the sync keeps its flags on a 426; error log stamps and reports.
- Feedback: the view model (send, failed send and "Copy instead", the scrubbed preview), `FeedbackRules.Scrub`, `RemoteFeedbackService`; on the server (`FeedbackTests`) storing and scrubbing, the checks, sign-in, the hourly limit, deletion with the account, the client round trip, and the Discord message (embed, file, mentions off, a long note, a refused post, the webhook setting).
- GymApp.Server.Tests (`ClientVersionTests`): a missing, old or garbage version gets a 426 with the message and `minVersion`; current, newer and pre-release versions get through; `/health` isn't checked; the contract round trip through `SocialApiClient`; `ReadMinimum`.

## Rolling it out
1. Release 0.15.0: bump `Version` in Directory.Build.props.
2. Once testers have it, set `MinClientVersion=0.15.0` on the server. From then on, 0.14.x builds are locked out completely. They don't know the 426: their restore drops the token, and their sign-in shows the message as a login error with no offline fallback, so they can't log workouts until they update. Any uploads they try are dropped as refusals. 0.15 sends the full 31 days of check-ins and the stats projection again on the next change.
3. For later releases, raise `MinClientVersion` only when an API change breaks older builds.
