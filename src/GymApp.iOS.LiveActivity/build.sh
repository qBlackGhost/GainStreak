#!/usr/bin/env bash
# Builds the live workout's Live Activity (0.20.0) and the Home Screen widgets (Phase 24) for the iOS head, which runs
# this before it builds (GymApp.iOS.csproj, BuildLiveActivity). Two products, both from swiftc, no Xcode project:
#   GainStreakLiveActivityBridge.framework  the app's side: starts, updates and ends the activity, and reloads the
#                                           widgets (the C# head calls it)
#   GainStreakLiveActivity.appex            the widget extension: the Live Activity (Dynamic Island, Lock Screen), the
#                                           Home Screen widgets and the Control Center controls
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
# extensions do: started at its own main, ExtensionFoundation traps on launch (found in 0.20.0 on the Simulator).
# Since Phase 24 it also has the Home Screen widgets (iOS 17) and Control Center controls (iOS 18), whose Edit Widget
# options are App Intents: the system finds them through Metadata.appintents, which Xcode makes after compiling. Here
# swiftc saves the constant values the tool needs (-emit-const-values-path, whole-module) and the tool runs below
ext=GainStreakLiveActivity
appex="$out/$ext.appex"
mkdir -p "$appex"
sources=("$shared" "$here/Widget/WorkoutLiveActivity.swift" "$here/Widget/HomeWidgets.swift")
echo '["AppIntent","EntityQuery","AppEntity","TransientEntity","AppEnum","AppShortcutProviding","AppShortcutsProvider","AnyResolverProviding","AppIntentsPackage","DynamicOptionsProvider","_IntentValueRepresentable","_AssistantIntentsProvider","_GenerativeFunctionExtractable","IntentValueQuery","Resolver"]' \
  > "$tmp/protocols.json"
"${swiftc[@]}" -wmo -target "arm64-apple-ios16.2$suffix" -module-name "$ext" -emit-executable \
  -Xlinker -e -Xlinker _NSExtensionMain \
  -emit-const-values-path "$tmp/$ext.swiftconstvalues" \
  -Xfrontend -const-gather-protocols-file -Xfrontend "$tmp/protocols.json" \
  -module-cache-path "$tmp/cache" -o "$appex/$ext" \
  "${sources[@]}"
printf '%s\n' "${sources[@]}" > "$tmp/sources.txt"
echo "$tmp/$ext.swiftconstvalues" > "$tmp/constvalues.txt"
xcrun appintentsmetadataprocessor --force --quiet-warnings \
  --output "$appex" --binary-file "$appex/$ext" \
  --toolchain-dir "$(xcode-select -p)/Toolchains/XcodeDefault.xctoolchain" \
  --module-name "$ext" --sdk-root "$sdk" \
  --xcode-version "$(xcodebuild -version | awk '/Build version/ { print $3 }')" \
  --platform-family iOS --deployment-target 16.2 --target-triple "arm64-apple-ios16.2$suffix" \
  --source-file-list "$tmp/sources.txt" --swift-const-vals-list "$tmp/constvalues.txt" >/dev/null
[[ -f "$appex/Metadata.appintents/extract.actionsdata" ]] || { echo "build.sh: no App Intents metadata" >&2; exit 1; }
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
# App Store Connect rejects an extension without these (the app gets them from the .NET build; this plist is made by
# hand): arm64 in UIRequiredDeviceCapabilities, and the DT* keys that say what built it
plist "$p" UIRequiredDeviceCapabilities array
plist "$p" UIRequiredDeviceCapabilities:0 string arm64
sdk_version=$(xcrun --sdk "$sdk_name" --show-sdk-version)
sdk_build=$(xcrun --sdk "$sdk_name" --show-sdk-build-version)
xcode_version=$(xcodebuild -version | awk '/^Xcode/ { print $2 }')
xcode_build=$(xcodebuild -version | awk '/Build version/ { print $3 }')
IFS=. read -r xc_major xc_minor xc_patch <<< "$xcode_version"
plist "$p" DTCompiler string com.apple.compilers.llvm.clang.1_0
plist "$p" DTPlatformName string "$sdk_name"
plist "$p" DTPlatformVersion string "$sdk_version"
plist "$p" DTPlatformBuild string "$sdk_build"
plist "$p" DTSDKName string "$sdk_name$sdk_version"
plist "$p" DTSDKBuild string "$sdk_build"
plist "$p" DTXcode string "$(printf '%02d%d%d' "$xc_major" "${xc_minor:-0}" "${xc_patch:-0}")"
plist "$p" DTXcodeBuild string "$xcode_build"
plist "$p" NSExtension dict
plist "$p" NSExtension:NSExtensionPointIdentifier string com.apple.widgetkit-extension
# The App Group the app writes widget.json into (Phase 24): the app's csproj gives the app the same one
group="group.$app_id"
plist "$p" GainStreakAppGroup string "$group"
# Device builds for TestFlight: the extension has its own App ID, so it needs its own profile, and its signature the
# profile's entitlements (the .NET build signs it with <name>.entitlements from this folder when there is one). That
# profile must include the App Group: the App IDs need the App Groups capability with $group (Phase 24).
# The Simulator signs ad hoc, so the App Group goes in an entitlements file of its own
if [[ -n "$profile" ]]; then
  cp "$profile" "$appex/embedded.mobileprovision"
  security cms -D -i "$profile" > "$tmp/profile.plist"
  plutil -extract Entitlements xml1 -o "$out/$ext.entitlements" "$tmp/profile.plist"
  /usr/libexec/PlistBuddy -c "Print :com.apple.security.application-groups" "$out/$ext.entitlements" >/dev/null 2>&1 ||
    echo "build.sh: warning: the extension's profile has no App Group, so the Home Screen widgets will show no data" >&2
else
  e="$out/$ext.entitlements"
  plist "$e" com.apple.security.application-groups array
  plist "$e" com.apple.security.application-groups:0 string "$group"
fi

rm -rf "$tmp"
echo "Live Activity built for $sdk_name in $out"
