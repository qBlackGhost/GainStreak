#!/usr/bin/env bash
# Builds the live workout's Live Activity (0.20.0) for the iOS head, which runs this before it builds (GymApp.iOS.csproj,
# BuildLiveActivity). Two products, both from swiftc, no Xcode project:
#   GainStreakLiveActivityBridge.framework  the app's side: starts, updates and ends the activity (the C# head calls it)
#   GainStreakLiveActivity.appex            the widget extension that draws it (Dynamic Island, Lock Screen)
# The .NET build embeds the framework (NativeReference) and the extension (AdditionalAppExtensions), and signs both.
#
#   build.sh <iphonesimulator|iphoneos> <out dir> <version> <build number> <app bundle id> [extension .mobileprovision]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

sdk_name=$1 out=$2 version=$3 build=$4 app_id=$5 profile=${6:-}
case "$sdk_name" in
  iphonesimulator) suffix=-simulator platform=iPhoneSimulator ;;
  iphoneos) suffix= platform=iPhoneOS ;;
  *) echo "build.sh: unknown SDK $sdk_name" >&2; exit 1 ;;
esac
sdk=$(xcrun --sdk "$sdk_name" --show-sdk-path)
swiftc=(xcrun --sdk "$sdk_name" swiftc -sdk "$sdk" -swift-version 5 -O -parse-as-library -application-extension)
shared="$here/Shared/WorkoutActivityAttributes.swift"
tmp="$out/obj"
rm -rf "$out"
mkdir -p "$tmp"

plist() {   # plist <file> <key> <type> <value>
  /usr/libexec/PlistBuddy -c "Add :$2 $3 ${4:-}" "$1" >/dev/null
}

# The bridge: the app's minimum (iOS 16.0), with ActivityKit weak so 16.0 still starts; the code itself needs 16.2
bridge=GainStreakLiveActivityBridge
fw="$out/$bridge.framework"
mkdir -p "$fw"
"${swiftc[@]}" -target "arm64-apple-ios16.0$suffix" -module-name "$bridge" -emit-library \
  -Xlinker -install_name -Xlinker "@rpath/$bridge.framework/$bridge" \
  -Xlinker -weak_framework -Xlinker ActivityKit \
  -module-cache-path "$tmp/cache" -o "$fw/$bridge" \
  "$shared" "$here/Bridge/LiveActivityBridge.swift"
p="$fw/Info.plist"
plist "$p" CFBundleIdentifier string "$app_id.LiveActivityBridge"
plist "$p" CFBundleExecutable string "$bridge"
plist "$p" CFBundleName string "$bridge"
plist "$p" CFBundlePackageType string FMWK
plist "$p" CFBundleShortVersionString string "$version"
plist "$p" CFBundleVersion string "$build"
plist "$p" MinimumOSVersion string 16.0
plist "$p" CFBundleSupportedPlatforms array
plist "$p" CFBundleSupportedPlatforms:0 string "$platform"

# The widget extension: Live Activities with ActivityContent need iOS 16.2. It starts at NSExtensionMain, as Xcode's
# extensions do: started at its own main, ExtensionFoundation traps on launch (found in 0.20.0 on the Simulator)
ext=GainStreakLiveActivity
appex="$out/$ext.appex"
mkdir -p "$appex"
"${swiftc[@]}" -target "arm64-apple-ios16.2$suffix" -module-name "$ext" -emit-executable \
  -Xlinker -e -Xlinker _NSExtensionMain \
  -module-cache-path "$tmp/cache" -o "$appex/$ext" \
  "$shared" "$here/Widget/WorkoutLiveActivity.swift"
p="$appex/Info.plist"
plist "$p" CFBundleIdentifier string "$app_id.LiveActivity"
plist "$p" CFBundleExecutable string "$ext"
plist "$p" CFBundleName string "$ext"
plist "$p" CFBundleDisplayName string GainStreak
plist "$p" CFBundleDevelopmentRegion string en
plist "$p" CFBundlePackageType string XPC!
plist "$p" CFBundleInfoDictionaryVersion string 6.0
plist "$p" CFBundleShortVersionString string "$version"
plist "$p" CFBundleVersion string "$build"
plist "$p" MinimumOSVersion string 16.2
plist "$p" CFBundleSupportedPlatforms array
plist "$p" CFBundleSupportedPlatforms:0 string "$platform"
plist "$p" UIDeviceFamily array
plist "$p" UIDeviceFamily:0 integer 1
plist "$p" UIDeviceFamily:1 integer 2
plist "$p" NSExtension dict
plist "$p" NSExtension:NSExtensionPointIdentifier string com.apple.widgetkit-extension
# Device builds for TestFlight: the extension has its own App ID, so it needs its own profile, and its signature the
# profile's entitlements (the .NET build signs it with <name>.entitlements from this folder when there is one)
if [[ -n "$profile" ]]; then
  cp "$profile" "$appex/embedded.mobileprovision"
  security cms -D -i "$profile" > "$tmp/profile.plist"
  plutil -extract Entitlements xml1 -o "$out/$ext.entitlements" "$tmp/profile.plist"
fi

rm -rf "$tmp"
echo "Live Activity built for $sdk_name in $out"
