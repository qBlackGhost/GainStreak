#!/usr/bin/env bash
# Builds the iOS app with a free Apple account (Personal Team) and installs it on the iPhone plugged into this Mac.
# Release build: full AOT, talks to the beta server (GymAppServerUrl in Directory.Build.props).
#
#   build/run-on-phone.sh                         # build, install, launch
#   build/run-on-phone.sh -p:Foo=bar              # extra arguments go to dotnet build
#   GAINSTREAK_FREE_BUNDLE_ID=com.you.gainstreak build/run-on-phone.sh   # another bundle id (default com.gainstreak.app.dev)
#
# One-time setup (Xcode makes the profiles; a free account can't make them on the developer website):
#   1. Xcode > Settings > Accounts: add your Apple ID. Its "Personal Team" appears.
#   2. Plug in the iPhone, unlock it, tap Trust. On the phone: Settings > Privacy & Security > Developer Mode > On
#      (it restarts).
#   3. In Xcode: File > New > Project > iOS App, any name, Team = your Personal Team, Bundle Identifier = the bundle id
#      above. Signing & Capabilities > + Capability > HealthKit. Then File > New > Target > Widget Extension, product
#      name "LiveActivity" (bundle id <bundle id>.LiveActivity), tick "Include Live Activity", Activate the scheme.
#      Pick the iPhone as the run destination and run the app target once (Product > Run): Xcode registers the phone
#      and makes both profiles. The first run may ask you to trust the developer on the phone: Settings > General >
#      VPN & Device Management > your Apple ID > Trust. You can delete that Xcode project afterwards.
#   Profiles and the installed app expire after 7 days: rerun step 3's Product > Run (or open the project and let
#   Xcode refresh), then this script.
set -euo pipefail
cd "$(dirname "$0")/.."

bundle_id=${GAINSTREAK_FREE_BUNDLE_ID:-com.gainstreak.app.dev}
ext_id="$bundle_id.LiveActivity"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# The signing certificate Xcode made for the Personal Team
identity=$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Development: [^"]*\)".*/\1/p' | head -1)
if [[ -z "$identity" ]]; then
  echo "No \"Apple Development\" certificate in the keychain. Do the one-time setup at the top of $0." >&2
  exit 1
fi

# A profile for an app id: the one that expires last. Prints "<path>|<UUID>"
find_profile() {
  local want=$1 best="" best_date=""
  for dir in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" "$HOME/Library/MobileDevice/Provisioning Profiles"; do
    [[ -d "$dir" ]] || continue
    for f in "$dir"/*.mobileprovision; do
      [[ -f "$f" ]] || continue
      security cms -D -i "$f" > "$tmp/p.plist" 2>/dev/null || continue
      local app_id expires name
      app_id=$(plutil -extract Entitlements.application-identifier raw "$tmp/p.plist" 2>/dev/null) || continue
      [[ "${app_id#*.}" == "$want" ]] || continue
      expires=$(plutil -extract ExpirationDate raw "$tmp/p.plist")
      [[ "$expires" > "$(date -u +%Y-%m-%dT%H:%M:%SZ)" ]] || continue
      name=$(plutil -extract UUID raw "$tmp/p.plist")   # by UUID: renewed profiles keep the same name
      if [[ -z "$best_date" || "$expires" > "$best_date" ]]; then best="$f|$name" best_date=$expires; fi
    done
  done
  echo "$best"
}

app_profile=$(find_profile "$bundle_id")
ext_profile=$(find_profile "$ext_id")
for pair in "$bundle_id:$app_profile" "$ext_id:$ext_profile"; do
  if [[ -z "${pair#*:}" ]]; then
    echo "No valid provisioning profile for ${pair%%:*}. Do step 3 of the setup at the top of $0 (or rerun it: they expire after 7 days)." >&2
    exit 1
  fi
done
# The .NET build looks for profiles in the older folder too
mkdir -p "$HOME/Library/MobileDevice/Provisioning Profiles"
cp -n "${app_profile%%|*}" "$HOME/Library/MobileDevice/Provisioning Profiles/" 2>/dev/null || true

# The plugged-in iPhone
xcrun devicectl list devices --json-output "$tmp/devices.json" >/dev/null
device=$(plutil -convert json -o - "$tmp/devices.json" | python3 -c '
import json, sys
for d in json.load(sys.stdin)["result"]["devices"]:
    hw = d.get("hardwareProperties", {})
    if hw.get("reality") == "physical" and hw.get("platform") == "iOS":
        print(hw.get("udid") or d["identifier"]); break')
if [[ -z "$device" ]]; then
  echo "No iPhone found. Plug it in, unlock it and trust this Mac." >&2
  exit 1
fi

echo "Signing as $identity, profiles \"${app_profile#*|}\" and \"${ext_profile#*|}\", device $device"
dotnet build src/GymApp.iOS -c Release -r ios-arm64 \
  -p:FreeAppleAccount=true -p:FreeBundleId="$bundle_id" \
  -p:CodesignKey="$identity" -p:CodesignProvision="${app_profile#*|}" \
  -p:LiveActivityProvisioningProfile="${ext_profile%%|*}" \
  "$@"

app=$(find src/GymApp.iOS/bin/Release/net10.0-ios/ios-arm64 -maxdepth 1 -name "*.app" | head -1)
xcrun devicectl device install app --device "$device" "$app"
xcrun devicectl device process launch --device "$device" "$bundle_id" || \
  echo "Installed. If it didn't open: Settings > General > VPN & Device Management > trust your Apple ID, then tap the icon."
