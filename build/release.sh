#!/usr/bin/env bash
# Builds the tester releases with Velopack into Releases/: a signed-if-configured macOS .app + .pkg for Apple silicon
# and Intel, and a Windows Setup.exe. Run on a Mac from anywhere; see "Releases for testers" in the README.
#
#   build/release.sh                                   # uses GymAppServerUrl / GymAppUpdateUrl from Directory.Build.props
#   build/release.sh -p:GymAppServerUrl=https://...    # extra arguments go to dotnet publish
#
# macOS signing and notarizing, when set (vpk reads them itself):
#   VPK_SIGN_APP_IDENTITY="Developer ID Application: ..."  VPK_SIGN_INSTALL_IDENTITY="Developer ID Installer: ..."
#   VPK_NOTARY_PROFILE=<a notarytool keychain profile>
set -euo pipefail
cd "$(dirname "$0")/.."

project=src/GymApp.Desktop
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
out=Releases
common=(--packId GainStreak --packVersion "$version" --packTitle GainStreak --packAuthors GainStreak --outputDir "$out")

dotnet tool restore

for rid in osx-arm64 osx-x64; do
  dotnet publish "$project" -c Release -r "$rid" --self-contained true -o "publish/$rid" "$@"
  # One channel per architecture, so each copy only ever updates to its own build
  dotnet vpk pack "${common[@]}" --channel "$rid" --packDir "publish/$rid" --mainExe GainStreak \
    --icon "$project/Assets/gainstreak.icns" --bundleId com.gainstreak.desktop
done

dotnet publish "$project" -c Release -r win-x64 --self-contained true -o publish/win-x64 "$@"
dotnet vpk "[win]" pack "${common[@]}" --channel win-x64 --packDir publish/win-x64 --mainExe GainStreak.exe \
  --icon "$project/Assets/gainstreak.ico"

echo "GainStreak $version is in $out/. Upload everything in it to the update folder (GymAppUpdateUrl)."
