# Horizon Store readiness checklist

Reviewed on 2026-10-14 against the [Meta Quest VRC guidelines](https://developers.meta.com/vr/resources/publish-quest-req/) (that page's `last_updated` was 2026-08-19). A check mark in Meta's table is required for the Horizon Store. A plus sign is recommended.

This client is not ready to submit. The sideload pipeline in [sideload.md](sideload.md) is the distribution path added here. Varun decides store versus sideload and owns the Meta developer organization, the app ID, the store artwork, and the hosted privacy-policy URL.

Drive, deadman, and e-stop behavior is unchanged. Arm-control code is unchanged. Hand-tracking frequency stays Low.

## Performance

| VRC | Requirement | Status |
|---|---|---|
| [VRC.Quest.Performance.1](https://developers.meta.com/vr/resources/vrc-quest-performance-1/) | Required. An interactive app uses a refresh rate of 72, 80, 90, 96, 100, or 120 Hz and keeps at least 60 fps, except on loading screens, system UI, or while capture/streaming. | Gap. The scene requests a legal refresh rate. Frame time is not measured in this repo. |
| VRC.Quest.Performance.2 | Retired. | n/a |
| [VRC.Quest.Performance.3](https://developers.meta.com/vr/resources/vrc-quest-performance-3/) | Required. Head-tracked graphics within 4 seconds of launch, or a VR loading indicator. | Gap. Needs a headset launch. The manifest sets a passthrough splash (`com.oculus.ossplash.background` = `passthrough-contextual`). There is no separate VR loading scene in this client. |
| [VRC.Quest.Performance.4](https://developers.meta.com/vr/resources/vrc-quest-performance-4/) | Recommended. At least 85% render scale for most of the experience. | Likely met. `Assets/scene1.unity` sets `VRGraphicsBoost.eyeTextureResolutionScale` to **1.5** and `renderViewportScale` to **1**. Confirm with a render-scale capture. |

`VRGraphicsBoost` on `[OhhO VR App]` in `Assets/scene1.unity` is the runtime quality setup:

| Setting | Scene value |
|---|---|
| Eye texture scale | 1.5 |
| Render viewport scale | 1 |
| MSAA | 4 |
| Fixed foveated rendering | Off |
| Display refresh | 90 Hz |
| Full-resolution textures | On |

90 Hz is an allowed refresh rate. 1.5× plus 4× MSAA plus foveation off is a clarity setting, and it can miss the 60 fps floor on Quest 2 or Quest 3S while the MJPEG camera is decoding. That is a store gap. This change does not retune those values; they are a product choice and need a Performance Analyzer capture on device (Quest 3, and Quest 2 or 3S if those headsets stay in `supportedDevices`).

The script default in `VRGraphicsBoost.cs` is 2.0×. The scene value 1.5 is what a player build uses. Rebuilding the screen with **OmniBot → Rebuild Unified OhhO Screen** adds the component if it is missing and would then use the script default. Check the scene value again after a rebuild.

## Permissions

[VRC.Quest.Security.2](https://developers.meta.com/vr/resources/vrc-quest-security-2/) is required: declare the minimum permissions the app needs, declare no prohibited permission, justify every review-requiring permission, and keep working if the user denies a runtime permission.

Audit of `Assets/Plugins/Android/AndroidManifest.xml`:

| Entry | Present | Why it is there |
|---|---|---|
| `android.permission.INTERNET` | Yes, added in this change | Sign-in, the robot garage, ROSBridge, the MJPEG camera, and episode upload. Normal permission, no runtime prompt. |
| `com.oculus.permission.HAND_TRACKING` | Yes | Arm control when hands are used. `oculus.software.handtracking` is `required="false"`, matching `handTrackingSupport: 1` (Controllers and Hands) in `Assets/Oculus/OculusProjectConfig.asset`. |
| `android.hardware.vr.headtracking` `required="true"` | Yes | VR application. |
| `com.oculus.feature.PASSTHROUGH` `required="false"` | Yes | Passthrough background. `isPassthroughCameraAccessEnabled` is 0, so the client does not request passthrough camera frames. |
| `android.permission.RECORD_AUDIO`, `CAMERA`, location, external storage, phone state, `QUERY_ALL_PACKAGES`, `SYSTEM_ALERT_WINDOW`, `REQUEST_INSTALL_PACKAGES` | Absent | Not used by this client. |

`unityplayer.SkipPermissionsDialog` is `false`, so Unity does not skip the system permission UI.

`android:allowBackup="false"` matches `disableBackups: 1` in the Oculus project config.

Also set in this change: `ForceInternetPermission: 1` in `ProjectSettings/ProjectSettings.asset` (`PlayerSettings.Android.forceInternetPermission`). Unity then adds `INTERNET` on Android builds even when it generates the main manifest itself.

`useCustomMainManifest` is still **0**. Unity generates the main manifest, and Meta's Gradle step (`OVRGradleGeneration`) patches the generated manifest from `OculusProjectConfig`. The file under `Assets/Plugins/Android/` is the manifest Meta's Android Manifest tool maintains. Turning Custom Main Manifest on would make that file the activity manifest Unity ships, including its theme and activity meta-data. That needs a Quest launch to confirm, so it is left off.

Meta's patch can still add non-permission tags from the project config, including the system keyboard feature (`requiresSystemKeyboard: 1`) and `com.oculus.telemetry.project_guid`. After the first release APK, dump the merged manifest (`aapt dump permissions` and `aapt dump xmltree`) and confirm the permission list still matches this table.

Graceful denial of `HAND_TRACKING` is not covered by an automated test. Controllers are the path for base motion. Whether a denial leaves the app usable is a headset check. Arm-control code was not changed to add that path.

### Cleartext to the robot

This is a functional gap for teleop, and it was not "fixed" by opening cleartext.

- `enableNSCConfig: 1` copies Meta's `Packages/com.meta.xr.sdk.core/Editor/network_sec_config.xml`, whose base config sets `cleartextTrafficPermitted="false"`.
- `insecureHttpOption: 0` is Unity's `InsecureHttpOption.NotAllowed`, so `UnityWebRequest` to `http://` fails.
- Camera and episode upload use that API: `http://<ip>:8080` (`MjpegVideoSource`) and `http://<ip>:8765` (`TeleopHudController`).
- ROSBridge opens `ws://<ip>:9090` through NativeWebSocket (`ROSConnectionWrapper`). A release build was not run on a headset here, so whether that socket is blocked is unconfirmed.

Account traffic to `https://ohho-robotics.com` and Supabase is HTTPS. A store build that allows cleartext to arbitrary hosts is a security regression. A LAN exception, or TLS on the robot bridge, is a decision for Varun. The vendored Meta XML is not a file to edit: `watcher.ps1` recopies that package from the Unity PackageCache.

## Privacy policy

| VRC | Requirement | Status |
|---|---|---|
| [VRC.Quest.Privacy.1](https://developers.meta.com/vr/resources/vrc-quest-privacy-1/) | Required. Privacy Policy URL points at a policy the app team manages, on HTTPS from a trusted certificate authority. | Gap. Draft only. |
| [VRC.Quest.Privacy.2](https://developers.meta.com/vr/resources/vrc-quest-privacy-2/) | Required. The policy says what user data is collected. | Draft covers the data this client actually touches. Varun has to review it before it is hosted. |
| [VRC.Quest.Privacy.3](https://developers.meta.com/vr/resources/vrc-quest-privacy-3/) | Required. The policy says how that data is used. | Covered in the draft. |
| [VRC.Quest.Privacy.4](https://developers.meta.com/vr/resources/vrc-quest-privacy-4/) | Required. The policy says how a user requests deletion. | Draft has an on-device path and a TODO contact address. The contact address is Varun's. |
| VRC.Quest.Privacy.5 | Required. Team and app clear data-protection checks. | Varun, in the Meta developer org. |

The draft is [privacy-policy.md](privacy-policy.md). Host it at a stable public HTTPS URL, then put that URL in the Horizon submission and in the SideQuest listing. The URL is not in the client binary today.

Data the client actually handles, which the draft describes:

- Email and one-time code sent to the OhhO Supabase auth endpoint. `RequestCode` sends `create_user: true`, so sign-in can create an account.
- Access token in memory. Refresh token in `PlayerPrefs` under `ohho.supabase.refresh_token`. Sign-out deletes that key.
- Garage rows (robot name, ids, status, config) read from the signed-in user's Supabase table.
- On-device connection memory: `robot_ip`, `robot_port`, `auto_connect`, last robot id/name/ip/port, per-robot calibration JSON, and `ohho.onboarding_seen`.
- Demonstration recordings under `Application.persistentDataPath/recordings/`: arm joint positions, base velocity, odometry, and commanded arm and base motion. Optional HTTP upload to the robot IP the user entered. The recorder does not write camera frames.
- Robot MJPEG displayed from the robot. It is not stored by `DatasetRecorder`.
- Passthrough shown by Horizon OS. `isPassthroughCameraAccessEnabled` is 0.
- Meta XR SDK diagnostic telemetry, including the project GUID the SDK writes into the manifest as `com.oculus.telemetry.project_guid`.

No advertising SDK is referenced under `Assets/`.

## Content

| VRC | Requirement | Status |
|---|---|---|
| [VRC.Content.1](https://developers.meta.com/vr/resources/vrc-content-1/) | Required. Content guidelines. | Gap for Varun. The app teleoperates a physical robot. Reviewers will treat that as the content, not as a game. Describe the existing emergency stop (both grips, README section 6). This change does not modify that control. |
| [VRC.Content.2](https://developers.meta.com/vr/resources/vrc-content-2/) | Required. Store metadata matches the app. | Gap until the listing, screenshots, and `productName` agree. Unity `productName` is still `vr_app`. The draft public name is OhhO Pilot. |
| VRC.Content.3 | Required when the app has user-generated content shared with other users. | Likely n/a. Recordings stay on the headset and, if exported, on the robot the same user connected. There is no in-app public feed. Varun confirms. |
| VRC.Content.4 | Recommended hide control for shared user-generated content. | n/a unless a shared feed is added. |

[VRC.Quest.Ads.1](https://developers.meta.com/vr/resources/vrc-quest-ads-1/) through Ads.7 apply to advertising. This client does not integrate an ad SDK. Do not mark the store page as containing ads.

## Age rating

Meta's [age-group self-certification](https://developers.meta.com/vr/resources/age-groups/) is mandatory for Horizon distribution. Apps on the platform cannot be used by children under 10.

Recommended certification for this client: **Teens and Adults (13+)**. It signs into an account and drives a physical robot. That recommendation is not Mixed Ages, so the Get Age Category API is not required for this group. Mixed Ages would require that API within 30 days of certification. The API is not implemented, and it needs the Meta app ID.

Varun still completes the IARC questionnaire in the Meta submission form and can choose a different age group. If the choice is Mixed Ages, the age API becomes required and is a follow-up code change. Children (under 13) is a poor fit for robot teleop.

There is no in-app age gate. A store build that must block under-13 users needs either the age-group choice above or an age API plus a block. That is unimplemented.

## Other store items

These are outside the five headings above and still block a submission.

| Item | Status |
|---|---|
| [VRC.Quest.Packaging.1](https://developers.meta.com/vr/resources/vrc-quest-packaging-1/) manifest | The Quest manifest has the VR launcher category, `supportedDevices` (`quest2\|questpro\|quest3\|quest3s`), and head tracking. `debuggable` is not set true in the source manifest. Confirm the merged release manifest from the signed APK. |
| [VRC.Quest.Packaging.2](https://developers.meta.com/vr/resources/vrc-quest-packaging-2/) APK signature scheme v2 | The release workflow signs with Varun's keystore. Unity 6 Gradle release builds use v2. Confirm with `apksigner verify --verbose` on the artifact. |
| [VRC.Quest.Packaging.3](https://developers.meta.com/vr/resources/vrc-quest-packaging-3/) unsupported Android features | No telephony, NFC, or other phone-only features are declared. Confirm the merged manifest. |
| [VRC.Quest.Packaging.4](https://developers.meta.com/vr/resources/vrc-quest-packaging-4/) supported engine | Unity **6000.5.2f1** and Meta XR SDK **203.0.0**. Varun checks Meta's current supported-engine list before submission. This repo stays on the pinned editor. |
| [VRC.Quest.Packaging.5](https://developers.meta.com/vr/resources/vrc-quest-packaging-5/) size | APK under 1 GB and any OBB under 4 GB. `androidSplitApplicationBinary` is 0. Measure the release APK. |
| [VRC.Quest.Packaging.6](https://developers.meta.com/vr/resources/vrc-quest-packaging-6/) 64-bit | `AndroidTargetArchitectures: 2` is ARM64 only. `AndroidMinSdkVersion` is 32. `AndroidTargetSdkVersion` is 34. Confirm `arm64-v8a` in the APK. |
| [VRC.Quest.Security.1](https://developers.meta.com/vr/resources/vrc-quest-security-1/) entitlement check | Recommended, not required. Not implemented. Needs the Meta app ID. |
| [VRC.Quest.Functional.2](https://developers.meta.com/vr/resources/vrc-quest-functional-2/) pause | Required for single-player. Not verified on device. |
| [VRC.Quest.Functional.14](https://developers.meta.com/vr/resources/vrc-quest-functional-14/) passthrough launch | Required when the app launches in passthrough. Splash meta-data is `passthrough-contextual`. Confirm a passthrough launch from MR Home. |
| [VRC.Quest.Input.4](https://developers.meta.com/vr/resources/vrc-quest-input-4/) focus | Required. The manifest sets `com.oculus.vr.focusaware` to true (`focusAware: 1`). No script under `Assets/Scripts` handles focus loss by hiding hands and dropping input. Headset check, and a small input change if review fails. |
| [VRC.Quest.Input.7](https://developers.meta.com/vr/resources/vrc-quest-input-7/) and [Input.8](https://developers.meta.com/vr/resources/vrc-quest-input-8/) | Required for hand tracking. System gesture and controller/hand switching are not covered here. Arm and input code was left unchanged. |
| [VRC.Quest.Tracking.1](https://developers.meta.com/vr/resources/vrc-quest-tracking-1/) | Required metadata. Recommend Sitting and Standing. The operator stays in place in passthrough. |
| [VRC.Quest.Tracking.2](https://developers.meta.com/vr/resources/vrc-quest-tracking-2/) | Required metadata. Controllers, plus optional hands. |
| [VRC.Publishing.1](https://developers.meta.com/vr/resources/vrc-publishing-1/) website | Use https://ohho-robotics.com if that page is the one Varun wants on the listing. |
| Store art ([VRC.Quest.Asset.1](https://developers.meta.com/vr/resources/vrc-quest-asset-1/) through Asset.10) | Missing. Android icon textures in Player Settings are empty. Screenshots, logo, and trailer are Varun's, captured on device. |
| Application id and display name | `applicationIdentifier` is empty, `companyName` is `DefaultCompany`, `productName` is `vr_app`. Set these before the first public install. See [sideload.md](sideload.md). |
| Release keystore | `androidUseCustomKeystore` is 0. The tag workflow injects the keystore from secrets. Local **Build and Run** still uses the debug key until Player Settings points at the same keystore. |

## What this change fixed

- Declared `android.permission.INTERNET` in `Assets/Plugins/Android/AndroidManifest.xml`.
- Set `ForceInternetPermission` so Android builds request that permission.
- Left every other declared permission in place, and did not add microphone, camera, location, or storage.
- Documented the release secrets by name and wired them into the tag workflow.

## What remains code, and what remains Varun

Code gaps to schedule after a headset pass, without weakening deadman or e-stop:

- LAN cleartext or TLS for the robot bridge.
- Focus-loss handling if Input.4 fails review.
- Graceful behavior when hand tracking is denied.
- Entitlement check and age API only if the store age group or Security.1 makes them required.
- A real Android application identifier, product name, and icons.
- A performance pass if 1.5× / MSAA 4 / foveation off misses 60 fps.

Varun personally:

- Meta developer org, app ID, and store versus sideload decision.
- Unity licence and keystore secrets in GitHub Actions.
- Host the privacy policy and supply the contact email and legal entity details.
- IARC age questionnaire and age-group self-certification.
- Screenshots, icon, and store copy.
- First signed APK on a Quest: install, update over the same key, permission denial, pause, passthrough launch, and a frame-time capture.
