# Sideload distribution

This is the sideload path for the Quest headset client. Varun decides whether a build also goes to the Meta Horizon Store, and Varun owns the Meta developer organization, the Meta app ID, and the public privacy-policy URL.

Store readiness is tracked in [horizon-store-checklist.md](horizon-store-checklist.md). The privacy-policy text to host is [privacy-policy.md](privacy-policy.md).

A sideload APK is installed with Quest developer mode. It is not a Horizon Store submission, and it does not perform a Platform entitlement check.

## Release pipeline

`.github/workflows/quest-release.yml` runs only when a `v*` tag is pushed. It does not run on pull requests.

The workflow reuses the GameCI setup from `.github/workflows/unity-tests.yml`:

- Unity **6000.5.2f1**
- image `unityci/editor:ubuntu-6000.5.2f1-android-3`
- the same Unity licence secret names
- a `Library` cache and the same disk-cleanup step

The build step is `game-ci/unity-builder` (the build action paired with `game-ci/unity-test-runner`). It produces one Android APK (`androidExportType: androidPackage`), uploads it as an Actions artifact, and attaches it to the GitHub Release for that tag.

GameCI's default versioning scheme is Semantic (latest git tag plus commits since the tag). This workflow does not use that. It reads `bundleVersion` and `AndroidBundleVersionCode` from `ProjectSettings/ProjectSettings.asset` and passes them as `versioning: Custom` and `androidVersionCode`. The tag must be `v` plus the exact `bundleVersion` string (`v1.0.1` for `1.0.1`).

Push the tag only after this workflow file is on the default branch. GitHub reads the workflow from the tagged commit, and a tag pushed before the workflow is on `main` can fail to start. This change does not push a tag and does not create a release.

## Required GitHub Actions secrets

Set these in the repository at Settings → Secrets and variables → Actions. The workflow and this document name the secrets. They do not contain secret values.

| Secret | What it is |
|---|---|
| `UNITY_LICENSE` | Full contents of the Unity personal licence `.ulf` file. Same secret as the edit-mode tests. |
| `UNITY_EMAIL` | Unity ID that owns that licence. Same secret as the edit-mode tests. |
| `UNITY_PASSWORD` | Password for that Unity ID. Same secret as the edit-mode tests. |
| `ANDROID_KEYSTORE_BASE64` | Base64 of the release keystore file (`ohho-release.keystore`). |
| `ANDROID_KEYSTORE_PASS` | Keystore password. |
| `ANDROID_KEYALIAS_NAME` | Key alias inside the keystore. |
| `ANDROID_KEYALIAS_PASS` | Key alias password. |

The workflow fails the tag build if any of these are empty. A release that skipped signing or licensing would publish nothing useful.

`UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` are the three secrets already documented in README section 4. The Android secrets are new and are only used by the release workflow. The keystore file is decoded in the runner as `ohho-release.keystore` and is not committed. `*.keystore` and `*.jks` are gitignored.

### Creating the keystore

Run this once on a machine Varun controls. `keytool` prompts for the passwords and the certificate name. Use the same alias later as `ANDROID_KEYALIAS_NAME`.

```
keytool -genkeypair -v -keystore ohho-release.keystore -alias ohho -keyalg RSA -keysize 2048 -validity 10000
```

Encode the file for `ANDROID_KEYSTORE_BASE64`.

Linux or macOS:

```
base64 -w 0 ohho-release.keystore
```

Windows PowerShell:

```
[Convert]::ToBase64String([IO.File]::ReadAllBytes("ohho-release.keystore"))
```

Keep the keystore file offline. Every future sideload update has to be signed with this same key. An APK signed by Unity's debug keystore, or by a different release key, will not install over this package. Quest will report a signature mismatch. The debug keystore from a local **Build and Run** is a different key.

Unity 6's Gradle release signing uses APK signature scheme v2, which is what [VRC.Quest.Packaging.2](https://developers.meta.com/vr/resources/vrc-quest-packaging-2/) requires for a later store upload. After the first tagged build, confirm with `apksigner verify --verbose` on the downloaded APK.

## Version and bundle code

`scripts/bump-quest-version.py` updates two fields in `ProjectSettings/ProjectSettings.asset`:

- `bundleVersion` (the version users see, and the git tag without the `v`)
- `AndroidBundleVersionCode` (the integer Android uses to allow an update)

It leaves `tvOSBundleVersion` and every other field alone. It does not commit or tag.

```
python scripts/bump-quest-version.py --patch
python scripts/bump-quest-version.py --minor
python scripts/bump-quest-version.py --major
python scripts/bump-quest-version.py --set 1.4.0
python scripts/bump-quest-version.py --patch --dry-run
python scripts/bump-quest-version.py --print-current
python scripts/bump-quest-version.py --print-code
```

`--patch` is the default. From the current `1.0` it writes `1.0.1` and adds 1 to the version code. `--set` requires `major.minor.patch`. A new version code must be greater than the current one unless `--keep-code` is set. The code has to stay in `1..2147483647`.

`--dry-run` prints the change and does not write. Tests live in `scripts/test_bump_quest_version.py` and run in `.github/workflows/version-bump.yml`.

Release steps, after the workflow is on `main`:

1. Run the bump script (or confirm `--print-current` is the version you intend to ship).
2. Commit the `ProjectSettings.asset` change.
3. Tag that commit `v` plus the exact `bundleVersion` (`git tag v1.0.1`).
4. Push the commit and the tag. The tag push is what starts the APK build and the GitHub Release.

## Identity still to set before the first public install

`ProjectSettings` still has `companyName: DefaultCompany`, `productName: vr_app`, and an empty `applicationIdentifier`. With `overrideDefaultApplicationIdentifier` off, Unity's default application id is `com.DefaultCompany.vr_app`.

Set an explicit Android application identifier before the first public sideload. The package name is permanent for updates: a later rename installs a second app and leaves the old one in place. Set the identifier before renaming `productName`, because the default id is derived from the company and product names. The launcher label follows `productName`, so the headset will show `vr_app` until that field changes. Suggested public name, once Varun confirms it: **OhhO Pilot**.

Confirm the real package name on the first APK with `aapt dump badging`.

## SideQuest listing draft

Do not publish this listing until the package name, release keystore, privacy-policy URL, and screenshots exist. Copy the fields into SideQuest; the values below are a draft.

| Field | Draft |
|---|---|
| App name | OhhO Pilot |
| Package name | `com.DefaultCompany.vr_app` until an Android application identifier is set. TODO(Varun): choose the permanent id and set it in Player Settings before the first public APK. |
| Version name | `bundleVersion` from Project Settings (currently `1.0`) |
| Version code | `AndroidBundleVersionCode` (currently `1`) |
| APK | The `.apk` attached to the GitHub Release for tag `v<bundleVersion>` |
| Summary | Drive your OhhO robot from a Quest headset. Sign in, pick a robot, and teleoperate in passthrough. |
| Description | OhhO Pilot is the Meta Quest client for an OhhO robot. Sign in with the same email account as ohho-robotics.com, open the garage, and connect to a robot on the local network. Controllers drive the base. Hand tracking can drive the arm. The headset shows the robot's MJPEG camera and can record a demonstration on the device, then upload that recording to the robot you connected. The robot, ROS 2 workspace, and website are not in this repository. WebRTC video is roadmap and is not part of this build. |
| Category | Application / utility |
| Comfort rating | Comfortable (passthrough, the operator stays in place). TODO(Varun): confirm on a headset. |
| Play style | Sitting or standing. Controllers required for the base. Hand tracking optional for the arm. |
| Headsets | Quest 2, Quest Pro, Quest 3, Quest 3S. This matches `com.oculus.supportedDevices` in the Android manifest. |
| Website | https://ohho-robotics.com |
| Privacy policy URL | TODO(Varun): host [privacy-policy.md](privacy-policy.md) at a public HTTPS URL and paste that URL here. |
| Support email | TODO(Varun) |
| Age | 13+. The app controls a physical robot. See the age section of the store checklist. |
| License | First-party code is Apache-2.0. The vendored Meta XR SDK stays under the Oculus SDK License Agreement. |
| Icon | TODO(Varun). Player Settings has no Android icon textures yet. |
| Screenshots | TODO(Varun). Capture them on a Quest. Store and SideQuest art should show this app, with Quest hardware only if a headset or controller is pictured. |
| Release status | Sideload / early access. Horizon Store submission is a separate decision. |

## Install on a Quest

The headset needs a Meta developer account with developer mode. That account is Varun's. This guide does not create one.

1. In the Meta Horizon phone app, open the headset and turn on developer mode.
2. On the headset, accept USB debugging when the PC connects.
3. Download the APK from the GitHub Release for the tag you intend to install. Use a release-signed APK from that workflow, or a local build signed with the same keystore. Mixing this key with Unity's debug keystore causes a signature mismatch.
4. Install with SideQuest or with `adb`.

SideQuest on a PC:

1. Install SideQuest and open it.
2. Connect the headset over USB and allow the connection.
3. Use **Install APK** and choose the file from the release. The app appears under unknown sources.

`adb`:

```
adb devices
adb install -r path\to\OhhO-VR.apk
```

`-r` replaces an existing install only when the package name and the signing key both match. If install reports a signature mismatch, the old build was signed with a different key. Uninstalling the old package removes on-device preferences, the saved sign-in session, and local recordings. Export anything you need before uninstalling.

Launch the app from the headset library (unknown sources). Sign-in and teleop behave as in README section 5.

### Local robot connection on a release build

Release builds block cleartext `UnityWebRequest`. `Assets/Oculus/OculusProjectConfig.asset` has `enableNSCConfig: 1`, and the Meta network security config that setting copies sets `cleartextTrafficPermitted` to false. `ProjectSettings` has `insecureHttpOption: 0` (Unity `InsecureHttpOption.NotAllowed`).

The camera (`http://<robot-ip>:8080`) and episode upload (`http://<robot-ip>:8765`) use `UnityWebRequest`, so those calls fail until a network security config allows that LAN host or the robot bridge serves TLS. Sign-in and the garage use HTTPS (`https://ohho-robotics.com` and the Supabase URL from the VR manifest) and are not affected by the cleartext block.

ROSBridge opens `ws://<robot-ip>:9090` through NativeWebSocket. This repo has no headset run of a release build, so whether that socket is blocked too is unconfirmed. Allowing cleartext is a security choice and is not changed here. Details are in the store checklist.

## Updating an installed sideload build

1. Bump `bundleVersion` and `AndroidBundleVersionCode`.
2. Tag `v<bundleVersion>` and let the release workflow sign the APK with the same keystore.
3. Install with `adb install -r` or SideQuest.

The package name and the signing certificate have to match the installed app. The version code has to increase.
