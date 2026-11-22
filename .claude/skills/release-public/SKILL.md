---
name: release-public
description: Release a GainStreak version to phone testers - check the version and CHANGELOG, run the tests, deploy the social server if it changed, build the signed Android and iOS betas with build/release-mobile.sh and hand them over for Google Play internal testing and TestFlight. The desktop is frozen and is not released.
disable-model-invocation: true
argument-hint: "[version, e.g. 0.23.1 - defaults to the one in Directory.Build.props] [--android | --ios]"
---

# Release GainStreak to phone testers

Release the version in `$ARGUMENTS`, or the `<Version>` in `Directory.Build.props` if none is given. `--android` or `--ios` in `$ARGUMENTS` releases only that phone; otherwise both. Work from the repository root. Steps that publish something are outward-facing: stop and ask before each of them (marked **confirm**), and stop at the first failure.

**The desktop is frozen at 0.21.0** (CLAUDE.md, "Desktop freeze"). Never run `build/release.sh` or `build/upload-github.sh`, and never publish a desktop release on GitHub, even when asked to "release everything": say the desktop is frozen and release the phones only.

## 1. Pick the version and check the changelog

- Read `<Version>` from `Directory.Build.props`. The last released version is the newest tag: `git tag --sort=-v:refname | head -1`.
- If that version is already tagged, the release needs a new version. Bump it following CLAUDE.md's "Versioning and changelog": `0.<phase>.<fix>`; a new phase (`0.N.0`) for new features or migrations, `0.N.x` for fixes without schema changes. Ask which kind of release this is if the commits don't make it obvious. Both stores only take a higher version.
- `CHANGELOG.md` must have a `## <version>` section covering every user-visible change since the last tag (`git log <last tag>..HEAD`). Write anything missing in the existing style: bold lead-ins, written for testers, and a closing `**Migration:**` line naming the database (the app's SQLite or the server's Postgres) when there is one.
- **Commit before building.** The build number the stores see is the commit count (`git rev-list --count HEAD`), so every uploaded build must come from a committed tree, and each commit is uploaded once per store. If the version bump or changelog is uncommitted, commit them (**confirm**) with a subject like the existing ones (`<version>: <short summary>`) and the attribution trailer. Other uncommitted changes: ask before going further. Untracked folders nothing builds from (like `tools/gym-gifs/`) don't matter; the script only warns.

## 2. Test

```bash
dotnet build
```

```bash
dotnet test
```

Docker must be running for `tests/GymApp.Server.Tests`. Don't release on a failing test.

## 3. Deploy the social server, if it changed (**confirm**)

The server goes **before** the phone builds, because new builds may call new endpoints. Old installed builds (the desktop 0.21.0 among them) keep talking to it, so it must stay compatible.

Check the version the server runs:

```bash
gcloud run services describe gymapp-server --project gainstreak-beta-1234 --region europe-west1 --format="value(spec.template.spec.containers[0].image)"
```

The image tag is the server's version. Run `git diff --stat v<that version>..HEAD -- src/GymApp.Server src/GymApp.Contracts src/GymApp.Social src/GymApp.Core` (fall back to the commit that released that version if no tag exists; Core is in the list because the server shares its rules, e.g. `Credentials`). If the tag already matches this version, or nothing changed, skip this step and say so.

Otherwise:
- Run the `server-compat-reviewer` agent on the changes since that version, and show the user its findings and verdict.
  - **safe to deploy**: go on.
  - **safe with MinClientVersion = x.y.z**: never above 0.21.0 while the desktop freeze holds (CLAUDE.md). If it needs more, stop and ask. Otherwise ask whether to set it (`--update-env-vars MinClientVersion=x.y.z` on the deploy below) and note it in the CHANGELOG entry.
  - **not safe as is**: stop the release. Offer to fix the findings first.
- Build the image for amd64 (Cloud Run) and push it. Use literal values, because each command may run in a fresh terminal. Never print the contents of a secret.

```bash
docker buildx build --platform linux/amd64 -f src/GymApp.Server/Dockerfile -t europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> --push .
```

Only when there is a new server migration (`git diff --stat v<that version>..HEAD -- src/GymApp.Server/Data/Migrations`). The production server never migrates on start:

```bash
docker run --rm --platform linux/amd64 -e ConnectionStrings__Social="$(gcloud secrets versions access latest --secret social-db --project gainstreak-beta-1234)" europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> migrate
```

```bash
gcloud run deploy gymapp-server --image europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> --project gainstreak-beta-1234 --region europe-west1
```

Then check `curl https://gymapp-server-611713345242.europe-west1.run.app/health` answers `Healthy`. Any call under `/v1` needs an `X-App-Version: <version>` header, or the server answers 426.

## 4. Build the phone betas

Check the signing settings first and name any that are missing; never print a password. Each Bash call is a fresh shell, so the iOS values go on the same command line as the script. The developer's values: identity `Apple Distribution: CRISTIAN DELAMARIAN (T836JC72YJ)`, profile `GainStreak App Store`, Live Activity profile `~/keys/GainStreak_LiveActivity_App_Store.mobileprovision`. The first signing may ask for the login keychain password: the user must answer it (Always Allow).
- **Android:** `GAINSTREAK_ANDROID_KEYSTORE`, `GAINSTREAK_ANDROID_KEY_ALIAS`, `GAINSTREAK_ANDROID_KEYSTORE_PASS` (and `GAINSTREAK_ANDROID_KEY_PASS` if the key's password differs). For friend notifications, `GAINSTREAK_GOOGLE_SERVICES_JSON` or `google-services.json` next to `GymApp.Android.csproj`; without it the build works but friend notifications are hidden, so say so.
- **iOS:** `GAINSTREAK_IOS_SIGN_IDENTITY`, `GAINSTREAK_IOS_PROVISIONING_PROFILE` and `GAINSTREAK_IOS_LIVE_ACTIVITY_PROFILE`. These need the paid Apple Developer Program. Without it there is no TestFlight build: release Android only, and point to `build/run-on-phone.sh` for the developer's own iPhone (free account, reinstalled every 7 days).

```bash
build/release-mobile.sh
```

Add `--android` or `--ios` for one phone. It takes several minutes, so run it in the background. Afterwards check `Releases/mobile/<version>/` has `GainStreak-<version>-<build>.aab` and `.apk` (Android) and `.ipa` (iOS), with `<build>` equal to `git rev-list --count HEAD`. Don't delete earlier folders in `Releases/`.

## 5. Hand the builds to the stores (the user's step)

Uploading happens in the store consoles, signed in as the developer, so give the user the file paths and steps instead of doing it:
- **Google Play:** Play Console → the app → Testing → Internal testing → Create release, upload the `.aab`, and roll it out. Testers get it through the Play Store. A tester without Play access can get the `.apk` directly (they allow installs from unknown sources once).
- **TestFlight:** open the newest `.xcarchive` (`open` it, or Xcode → Window → Organizer → Archives; several builds of one version look alike, so check the creation time), then **Distribute App → App Store Connect → Upload**. Use Organizer, not Transporter (Transporter gave a misleading ITMS-90034). Once App Store Connect has processed it (10 to 30 minutes), internal testers get it right away; external testers need a short beta review first.

## 6. Tag (**confirm**)

- Tag the released commit: `git tag v<version>`.
- Ask before `git push` and `git push origin v<version>`.

## Report

Finish by listing:
- the version and build number
- whether the server was deployed, whether a migration ran, and any MinClientVersion change
- the files in `Releases/mobile/<version>/`, and which phone was left out and why (missing signing, no Apple Developer Program)
- what's left for the user: the Play and TestFlight uploads, an unpushed tag
