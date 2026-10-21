#!/usr/bin/env bash
# Uploads what build/release.sh left in Releases/ as one GitHub release (tag v<Version>) on the public releases
# repository in GymAppUpdateUrl. Uses the GitHub CLI's login (gh auth login) for the token. See the README.
set -euo pipefail
cd "$(dirname "$0")/.."

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
repo=$(sed -n 's:.*<GymAppUpdateUrl>\(.*\)</GymAppUpdateUrl>.*:\1:p' Directory.Build.props)
[[ "$repo" == https://github.com/* ]] || { echo "GymAppUpdateUrl in Directory.Build.props isn't a GitHub repository URL." >&2; exit 1; }

dotnet tool restore
# One release for all three channels: the first upload creates it, --merge adds the others to it
for channel in osx-arm64 osx-x64 win-x64; do
  dotnet vpk upload github --outputDir Releases --channel "$channel" --repoUrl "$repo" --token "$(gh auth token)" \
    --tag "v$version" --releaseName "GainStreak $version" --merge --publish
done

echo "Published GainStreak $version: $repo/releases/tag/v$version"
