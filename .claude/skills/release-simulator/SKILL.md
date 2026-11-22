---
name: release-simulator
description: Prep a local test build of GainStreak for the simulators on this Mac - run the app tests, build the iOS Release app for the Simulator (full AOT, as CLAUDE.md asks), install and launch it on the booted iPhone simulator, and optionally the Android emulator. Nothing is published, signed for stores or tagged.
disable-model-invocation: true
argument-hint: "[--android] [--device <simulator name>] [--skip-tests]"
---

# Prep GainStreak for the simulators on this Mac

A local check, not a release: no version bump, tag, upload or server deploy. For the stores use `/release-public`. Work from the repository root. The desktop is frozen (CLAUDE.md), so it is not touched here.

## 1. Tests

Unless `--skip-tests` is given:

```bash
dotnet build
```

```bash
dotnet test tests/GymApp.Tests
```

This includes `CompiledModelTests`. If it fails after an entity change, regenerate the compiled model (CLAUDE.md, "Commands"). Stop on a failure and report it. Server tests (Docker) are not needed here.

## 2. Build the iOS app for the Simulator

Release, not Debug: a Debug build JITs and hides EF Core model-building failures (CLAUDE.md). It takes several minutes (full AOT), so run it in the background and wait for it.

```bash
dotnet build src/GymApp.iOS -c Release -f net10.0-ios -r iossimulator-arm64
```

(`mcp__Claude_Code_iOS_Simulator__build` is an alternative if it is available.) The app lands in `src/GymApp.iOS/bin/Release/net10.0-ios/iossimulator-arm64/GymApp.iOS.app` and its bundle id is `com.gainstreak.app`. If the build complains about stale plists after an `Info.plist` or `Entitlements.plist` change, delete `obj/Release/net10.0-ios/iossimulator-arm64/AppManifest.plist` and `nativelibraries/GymApp.iOS` (CLAUDE.md, Apple Health) and build again.

## 3. Install and launch

- Use the simulator named by `--device`, else the one already booted (`xcrun simctl list devices booted`), else boot `iPhone 17`. Open the live panel first with `mcp__Claude_Code_iOS_Simulator__control` (`attach`) so the user can watch.
- Install over the old copy; the app's data stays, so a migration runs on top of the old database, which is what a tester's update does.

```bash
xcrun simctl install booted src/GymApp.iOS/bin/Release/net10.0-ios/iossimulator-arm64/GymApp.iOS.app
```

```bash
xcrun simctl launch booted com.gainstreak.app
```

- Take a screenshot to confirm it opened (no crash on start, the home screen shows). The simulator build talks to the beta server (`GymAppServerUrl` in `Directory.Build.props`), so sign-in and friends work against real data.
- Ask the user before wiping the app's data (`xcrun simctl uninstall booted com.gainstreak.app`), for example to test a fresh install.

## 4. Android emulator (only with `--android`)

Check `adb devices` for a running emulator and say so if none is. Then build and install the Debug head for it:

```bash
dotnet build src/GymApp.Android -c Debug -t:Install
```

## Report

List what passed, which simulator got the build and the app version (`Directory.Build.props`), what you saw in the screenshot, and anything not checked (the phone-only features that need a real device: Live Activity, Apple Health data, push, the map's location).
