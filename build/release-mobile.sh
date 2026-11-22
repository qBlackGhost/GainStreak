#!/usr/bin/env bash
# Builds the phone betas into Releases/mobile/<version>/: a signed Android App Bundle (Google Play internal testing)
# plus an APK to send to testers directly, and a signed iOS .ipa for TestFlight. Run on a Mac from anywhere; see
# "Releases for testers" in the README.
#
#   build/release-mobile.sh                       # both phones
#   build/release-mobile.sh --android             # one of them
#   build/release-mobile.sh --ios -p:Foo=bar      # extra arguments go to dotnet publish
#
# Signing comes from the environment and the keychain, never from the repo:
#   Android: GAINSTREAK_ANDROID_KEYSTORE (path to the upload keystore), GAINSTREAK_ANDROID_KEY_ALIAS,
#            GAINSTREAK_ANDROID_KEYSTORE_PASS, and GAINSTREAK_ANDROID_KEY_PASS when the key's password differs
#   iOS:     GAINSTREAK_IOS_SIGN_IDENTITY (the "Apple Distribution: Name (TEAMID)" certificate in the keychain) and
#            GAINSTREAK_IOS_PROVISIONING_PROFILE (the App Store profile's name, installed in Xcode; from 0.20.0 its App ID
#            needs the HealthKit capability, so a profile made before that must be made again), and
#            GAINSTREAK_IOS_LIVE_ACTIVITY_PROFILE (path to the App Store .mobileprovision for the Live Activity
#            extension, App ID com.gainstreak.app.LiveActivity, 0.20.0)
#
# The version is Directory.Build.props' Version; the build number, which the stores need to only go up, is the
# commit count. So commit before building, and build each commit once per store.
set -euo pipefail
cd "$(dirname "$0")/.."

android=1 ios=1
case "${1:-}" in
  --android) ios=0; shift ;;
  --ios) android=0; shift ;;
esac

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
build=$(git rev-list --count HEAD)
out="Releases/mobile/$version"
started=$(date +%s)

need() {
  local missing=()
  for name in "$@"; do [[ -n "${!name:-}" ]] || missing+=("$name"); done
  if (( ${#missing[@]} )); then
    echo "Set ${missing[*]} first (see \"Releases for testers\" in the README)." >&2
    exit 1
  fi
}

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Note: uncommitted changes. Build $build is the commit count, so commit before a build you upload." >&2
fi

if (( android )); then
  need GAINSTREAK_ANDROID_KEYSTORE GAINSTREAK_ANDROID_KEY_ALIAS GAINSTREAK_ANDROID_KEYSTORE_PASS
  export GAINSTREAK_ANDROID_KEY_PASS="${GAINSTREAK_ANDROID_KEY_PASS:-$GAINSTREAK_ANDROID_KEYSTORE_PASS}"
  # The passwords go in as env: references, so they never show in the command line or the build log
  dotnet publish src/GymApp.Android -c Release -o publish/android \
    -p:ApplicationVersion="$build" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$GAINSTREAK_ANDROID_KEYSTORE" \
    -p:AndroidSigningKeyAlias="$GAINSTREAK_ANDROID_KEY_ALIAS" \
    -p:AndroidSigningStorePass=env:GAINSTREAK_ANDROID_KEYSTORE_PASS \
    -p:AndroidSigningKeyPass=env:GAINSTREAK_ANDROID_KEY_PASS \
    "$@"
  mkdir -p "$out"
  cp publish/android/com.gainstreak.app-Signed.aab "$out/GainStreak-$version-$build.aab"
  cp publish/android/com.gainstreak.app-Signed.apk "$out/GainStreak-$version-$build.apk"
fi

if (( ios )); then
  need GAINSTREAK_IOS_SIGN_IDENTITY GAINSTREAK_IOS_PROVISIONING_PROFILE GAINSTREAK_IOS_LIVE_ACTIVITY_PROFILE
  # ArchiveOnBuild also leaves an .xcarchive in Xcode's Organizer
  dotnet publish src/GymApp.iOS -c Release -r ios-arm64 -o publish/ios \
    -p:ApplicationVersion="$build" \
    -p:ArchiveOnBuild=true \
    -p:CodesignKey="$GAINSTREAK_IOS_SIGN_IDENTITY" \
    -p:CodesignProvision="$GAINSTREAK_IOS_PROVISIONING_PROFILE" \
    -p:LiveActivityProvisioningProfile="$GAINSTREAK_IOS_LIVE_ACTIVITY_PROFILE" \
    "$@"
  mkdir -p "$out"
  cp publish/ios/*.ipa "$out/GainStreak-$version-$build.ipa"
  # The Live Activity extension and its bridge are built by src/GymApp.iOS.LiveActivity/build.sh, so .NET leaves their
  # symbols out of the .xcarchive; App Store Connect then warns "Upload Symbols Failed" and can't symbolicate their
  # crashes. Put them in the archive that this publish just made (Organizer uploads the symbols with the build).
  archive=$(ls -dt "$HOME"/Library/Developer/Xcode/Archives/*/*.xcarchive 2>/dev/null | head -1 || true)
  extension_dsyms=src/GymApp.iOS/obj/liveactivity/Release/iphoneos/dSYMs
  if [[ -n "$archive" && -d "$archive/dSYMs" && -d "$extension_dsyms" && "$(stat -f %m "$archive")" -ge "$started" ]]; then
    cp -R "$extension_dsyms"/*.dSYM "$archive/dSYMs/"
    echo "Added the Live Activity symbols to $archive."
  else
    echo "Note: no fresh .xcarchive or no extension symbols found, so the archive has no symbols for the Live Activity." >&2
  fi
fi

echo "GainStreak $version (build $build) is in $out/."
(( android )) && echo "  Google Play: upload the .aab to internal testing. Testers without Play: send them the .apk."
(( ios )) && echo "  TestFlight: upload the .ipa with Apple's Transporter app."
exit 0
