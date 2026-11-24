# Future: GainStreak Premium (planned, after the 0.28 update)

**Status: planned, not started.** The approved premium features, with what stays free. Not a phase spec yet: before
work starts, the plumbing in section 1 is designed, each feature's open design points are settled (options drafted for
the developer to pick from), and this file is split into phase specs (`PHASE_<N>_*.md`). Free features are in
`FUTURE_28_BACKLOG.md`.

Phones only (the desktop is frozen, CLAUDE.md "Desktop freeze"): premium is not built for the desktop, and the desktop
keeps building and starting. Keep `Contracts` backward compatible and never raise `MinClientVersion` above 0.21.0.

## Ruled out (don't add these)

Rest timer, plate calculator, RPE or reps in reserve, the deload prompt, and larger friend groups or private
leaderboards, and advanced templates (programs like 5/3/1 or PPL with generated weights). Supersets and import
from notes are deferred.

## What stays free (don't paywall)

Logging workouts, streaks, points, friends and the basic leaderboard, the beginner help (build a workout, tips and
form tips, the basic weight increase hint), the weekly and monthly recap, the PR card,
the basic PR list, and **file or iCloud backup and CSV export**: people must be able to take their data out. Premium
adds convenience and depth on top, never a lock on the user's own data.

## 1. Premium plumbing (build first)

None of this exists yet; check the code before assuming (a search found no subscription code).

- **Purchases.** App Store (StoreKit) and Google Play subscriptions behind an `IPlatform`-style interface, with the
  head doing the store work and `GymApp.App` seeing only "is premium" and "buy" and "restore purchases".
- **Entitlement on the server.** The server verifies the receipt and stores the entitlement per account (a new server
  table and migration); the app asks the server, with an offline cache like `SocialCacheStore`, so a phone offline
  keeps what it last knew. Features that need only the device (insights, themes) check the cached entitlement.
- **A lapsed subscriber keeps their data.** Open design: what happens to cloud backups and photos on lapse (a grace
  period, then read-only, then deletion with warning), and that nothing local is ever removed or locked. Existing
  custom splits above the new free limit stay usable (see section 6).
- **Beta and testers:** a way to grant premium without paying (a server flag per account) so the test account and
  TestFlight testers can try it.
- Ties to App Store rules (subscription terms, restore purchases, a privacy note on photos); settle in review.
- Old clients: the entitlement check must be an optional endpoint and optional field so 0.21.0 and earlier builds keep
  working; run the `server-compat-reviewer` agent.

## 2. Cloud backup and sync across phones

Free: the file or iCloud backup from the 0.28 doc. Premium adds automatic backup to the server and moving to a new
phone or using an iPad.

- **Backup:** the same backup content and format version as the free backup, uploaded automatically (when the phone is
  online, charging or on Wi-Fi is open), with the last backup time and a manual "Back up now".
- **Restore on a new phone:** sign in, choose "Restore from cloud", and the data comes down through the same migrations
  as a fresh start.
- **Sync across two devices is the hard part.** Open design, and decide before building: either a **single-active
  device** model (restore replaces, the last backup wins, simple) or **true sync** (merge per entity, conflicts,
  deletions). True sync is a large project in a local-first app; the default plan is single-active device first.
- Storage on the server (Postgres for metadata, object storage for the data), size and retention limits, and
  encryption at rest are open points. Treat backups as private health data.

## 3. Progress photos

- Photos taken or picked on the Body card, shown next to the weight chart and comparable (side by side, before and
  after). Stored on the device; premium adds cloud storage and backup of them.
- **Open design:** how many photos free users keep on the device (a free local limit is easy to justify), the premium
  limit, photo size and compression, and whether photos are included in the file backup (they are large).
- Privacy: photos never leave the device unless premium cloud storage is on, are never shown to friends, and are
  excluded from CSV. Deleting an account deletes its cloud photos.
- **Migration:** a new app SQLite entity for photo records (file path, date, optional note) plus a server table for
  the premium cloud copy; regenerate the compiled model.

## 4. Analytics

All of these are pure rules in `GymApp.Core` over `SetLog` and the body and food data already on the device, with no
server involvement. Warmups are ignored; drops never count toward PRs. Build the shared "best set per session" and
week-grouping code once.

- **Progressive overload suggestions, the deeper version.** The basic hint is **free** (see the 0.28 doc, beginner
  help): pick a rep range, and when every working set reaches the top of it, the app suggests adding weight. Premium
  builds on it: the suggested step per exercise from the user's history, the hint carried across sessions (not just the
  last one), and its link to plateau detection below. Hints only, never applied automatically.
- **Plateau detection.** Per exercise, the best estimated 1RM per session (Epley, as `PrCalculator`). If there is no new
  best for about four weeks and the exercise was trained at least 5 or 6 times in that span, show a dismissible note
  on the exercise in the live workout and its history. Weeks on a `Vacation` don't count; a PR adjustment resets the
  clock. Advice only.
- **Per-muscle weekly volume, with history.** Working sets per `Exercise.MuscleGroup` (11 groups) as bars with a target
  band of roughly 10 to 20 sets a week, last week for comparison, a neglected marker below the band, and the weeks
  before it as a history. Drops count as sets, a per hand set counts once. Known limit: one muscle group per exercise,
  so secondary muscles aren't counted; say so in the screen's help text.
- **Trends over time.** Strength curves per lift (estimated 1RM over months) and volume per muscle compared month to
  month.
- **Year in review.** A shareable recap with the year's totals, biggest PRs and streak history; the premium version of
  the free weekly and monthly recap, using the same image-drawing control.
- **Body and nutrition correlations.** How body weight and calories line up with strength over the weeks, from
  `WeightLog`, `CalorieLog`/food entries and `SetLog`. Phrase results as "what the data shows", never as advice.
- **What the free app shows:** the basic PR list and the recap; free users see a locked preview of these screens with
  what they would get, not a blank space.

## 5. Personalisation

- **Custom themes and app icons**, built on the tokens in `GainStreakResources.axaml` (light and dark variants for
  each theme); alternate app icons per platform (iOS alternate icons, Android activity aliases), needing the heads.
- **Extra badge and card styles for shared PR cards**, once the PR card (free) exists.

## 6. Custom splits limit

- **The free limit drops from 5 to 2 or 3** custom splits (the developer's decision to settle: 2 or 3); premium
  raises it (open: 5, or higher, or unlimited). The limit lives in `SplitService.MaxCustomSplits` (now 5) and in the
  message in `SplitShareService.NoRoomMessage`; both must read the user's entitlement instead of a constant.
- **Existing users above the new limit keep every split they already have** (nothing is deleted or locked); they just
  can't add another until they are below the limit or premium. Receiving a shared split counts against the limit and
  needs the same message.
- Old clients enforce the old limit themselves (5), so no server change is needed for this.

## 7. Widgets and Live Activity styles

- Extra layouts for the Phase 24 widgets (`PHASE_24_IOS_WIDGETS.md`) and extra styles for the workout's Live Activity
  (`GymApp.iOS.LiveActivity`, Swift). The widget extension has to know the entitlement: pass it through the shared
  app group data the widgets already read. Free keeps the current default widget and Live Activity.
- Android widgets follow when Android has them. iOS only until then.

## 8. Exercise GIFs

Looping exercise animations, drawn by an original 3D figure with no third-party models or art, in the app's colors,
in a dark and a light version, 480 × 480 px. The generator already exists in `tools/gym-gifs/` (`rig.py`,
`exercises.py`, `render.py`, `build_page.py`; see its README) and is not committed yet. It makes three poses per muscle
group, named like the app's built-in exercises.

- **Where they show:** on the exercise in the live workout and in the exercise picker and exercise detail, matched to
  the built-in exercise by name; custom exercises have none.
- **Free or premium:** open for the developer to set. A natural split is a few exercises free and the rest premium, or
  the animation only in the live workout for premium.
- **Delivery (open):** the GIFs are large (the whole generated set in `out/` is about 22 MB on disk, dark and light
  versions). Bundling them makes the app download bigger; downloading them on demand from the server costs bandwidth
  and needs a cache on the phone and an offline fallback. Consider a smaller format (animated WebP or short video) and
  a lower resolution; check Avalonia's support for animated images on iOS and Android before choosing.
- **Coverage:** three exercises per muscle group is a small share of the built-in list; extend `exercises.py` for the
  rest before this is worth selling, and keep the "original art" note for the store review.
- Commit `tools/gym-gifs/` (without `.venv` and generated `out/` files, add a `.gitignore`) when this work starts.

## Order and versions

1. Premium plumbing (purchases, entitlement, grants for testers).
2. Cloud backup, then progress photos (the two with a running cost).
3. Analytics (overload, plateau, volume, trends, year in review, correlations).
4. Personalisation, custom splits limit, widgets and Live Activity styles, exercise GIFs.

The numbering is the developer's call; per CLAUDE.md a feature that needs a schema change opens a new phase (`0.N.0`).
Every user-visible change gets a `CHANGELOG.md` entry ending with a `**Migration:**` line when there is one.
