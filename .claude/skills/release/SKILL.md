---
name: release
description: Release a GainStreak version to testers - check the version and CHANGELOG, run the tests, deploy the social server if it changed, build the Velopack installers and publish them on GitHub.
disable-model-invocation: true
argument-hint: "[version, e.g. 0.15.7 - defaults to the one in Directory.Build.props]"
---

# Release GainStreak

Release the version in `$ARGUMENTS`, or the `<Version>` in `Directory.Build.props` if no argument is given. Work from the repository root. Each step below that publishes something is outward-facing: tester apps update themselves from it. Stop and ask before each of those steps (marked **confirm**), and stop at the first failure.

## 1. Pick the version and check the changelog

- Read `<Version>` from `Directory.Build.props`. Find the last released version with `git tag --sort=-v:refname | head -1` and the newest release on `https://github.com/qBlackGhost/GainStreak-releases` (`gh release list --repo qBlackGhost/GainStreak-releases --limit 3`).
- If that version is already released, the release needs a new version. Bump it following the rules in the README's "Versioning": `0.<phase>.<fix>`; a new phase (`0.N.0`) for new features or migrations, `0.N.x` for fixes without schema changes. Ask which kind of release this is if the commits don't make it obvious. Velopack only offers a higher version.
- `CHANGELOG.md` must have a `## <version>` section covering every user-visible change since the last release (`git log <last tag>..HEAD`). Write anything missing in the existing style: bold lead-ins, written for testers, and a closing `**Migration:**` line naming the database (the app's SQLite or the server's Postgres) when there is one.
- The working tree must be clean apart from the version bump and changelog. If other uncommitted changes exist, ask before going further.

## 2. Test

```bash
dotnet build
```

```bash
dotnet test
```

Docker must be running for `tests/GymApp.Server.Tests`. Don't release on a failing test.

## 3. Deploy the social server, if it changed (**confirm**)

The server deploy goes **before** the desktop release, because new clients may call new endpoints.

Check what changed since the version the server runs:

```bash
gcloud run services describe gymapp-server --project gainstreak-beta-1234 --region europe-west1 --format="value(spec.template.spec.containers[0].image)"
```

The image tag is the server's version. Run `git diff --stat v<that version>..HEAD -- src/GymApp.Server src/GymApp.Contracts src/GymApp.Social` (fall back to the commit that released that version if no tag exists). If nothing changed, skip this step and say so.

Otherwise:
- Run the `server-compat-reviewer` agent on the changes since that version, and show the user its findings and verdict.
  - **safe to deploy**: go on.
  - **safe with MinClientVersion = x.y.z**: ask whether to set it on the Cloud Run service (`--update-env-vars MinClientVersion=x.y.z` on the deploy below). Also note it in the CHANGELOG entry.
  - **not safe as is**: stop the release. Offer to fix the findings first.
- Then build the image for amd64 (Cloud Run), push it, migrate and deploy. Use literal values, because each command may run in a fresh terminal. Never print the contents of a secret.

```bash
docker buildx build --platform linux/amd64 -f src/GymApp.Server/Dockerfile -t europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> --push .
```

Only when there is a new server migration:

```bash
docker run --rm --platform linux/amd64 -e ConnectionStrings__Social="$(gcloud secrets versions access latest --secret social-db --project gainstreak-beta-1234)" europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> migrate
```

```bash
gcloud run deploy gymapp-server --image europe-west1-docker.pkg.dev/gainstreak-beta-1234/gymapp/gymapp-server:<version> --project gainstreak-beta-1234 --region europe-west1
```

Then check `curl https://gymapp-server-611713345242.europe-west1.run.app/health` answers `Healthy`.

## 4. Build the installers

```bash
build/release.sh
```

This takes several minutes, so run it in the background. It publishes osx-arm64, osx-x64 and win-x64 and packs them into `Releases/`. Never delete or clean `Releases/`: the next run builds its delta updates against the earlier packages. Afterwards, check that `Releases/` has `GainStreak-<channel>-Setup.*` files and a `releases.<channel>.json` for all three channels containing the new version.

## 5. Publish on GitHub (**confirm**)

Once published, testers' installed copies download the update on their next start.

```bash
build/upload-github.sh
```

It needs `gh auth login` and creates the release `v<version>` on the public releases repository.

## 6. Commit and tag (**confirm**)

- Commit the version bump and changelog, if they aren't already committed. Use a subject like the existing ones: `<version>: <short summary>`, a short body, and the attribution trailer.
- Tag the release with `git tag v<version>`.
- Ask before `git push` and `git push origin v<version>`.

## Report

Finish by listing:
- the version
- whether the server was deployed, and whether a migration ran
- the GitHub release link
- anything skipped or left for the user, e.g. unsigned macOS builds (when the `VPK_SIGN_*` variables weren't set) or an unpushed tag
