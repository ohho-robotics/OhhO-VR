# ADR 213 — Quest passthrough camera and on-device tag36h11

Status: accepted for the spike. The detection gate is not measured yet. The fallback until a Quest 3/3S run fills the tables below is manual placement.

Linear: OHH-213. Requirement 5 is "detect the robot in AR".

This checkout is the Quest client only (Unity `6000.5.2f1`). Meta XR Core SDK and the Interaction SDK Essentials are vendored at `203.0.0`. `com.meta.xr.sdk.interaction.ovr` is a registry dependency in `Packages/manifest.json` and is not vendored under `Packages/`. No headset was attached for this record.

## SDK inspection (vendored 203.0.0)

### Present: permission, manifest flag, startup request

The Passthrough Camera API permission string is in the vendored Core SDK.

| Item | Exact location |
|---|---|
| Permission constant | `OVRPermissionsRequester.PassthroughCameraAccessPermission` = `horizonos.permission.HEADSET_CAMERA` |
| Enum | `OVRPermissionsRequester.Permission.PassthroughCameraAccess` (global type, no namespace) |
| Id lookup | `OVRPermissionsRequester.GetPermissionId` maps that enum to the constant |
| Platform check | `IsPermissionSupportedByPlatform` returns true for `PassthroughCameraAccess` |
| Editor grant | `IsPermissionGranted` returns true when `UNITY_ANDROID && !UNITY_EDITOR` is false. An Editor play session is not a headset grant |
| File | `Packages/com.meta.xr.sdk.core/Scripts/OVRPermissionsRequester.cs` |

Project and startup wiring, same package:

| Item | Exact location |
|---|---|
| Project flag | `OVRProjectConfig.isPassthroughCameraAccessEnabled` (`Packages/com.meta.xr.sdk.core/Editor/OVRProjectConfig.cs`) |
| Manifest write | `OVRManifestPreprocessor` adds `uses-permission` `horizonos.permission.HEADSET_CAMERA` only when that flag is set and `insightPassthroughSupport` is not `FeatureSupport.None` (`Packages/com.meta.xr.sdk.core/Editor/OVRManifestPreprocessor.cs`) |
| Startup request field | `OVRManager.requestPassthroughCameraAccessPermissionOnStartup` (internal) |
| Startup behaviour | `OVRManager` adds `Permission.PassthroughCameraAccess` to the startup request list when that field is set (`Packages/com.meta.xr.sdk.core/Scripts/OVRManager.cs`) |
| Inspector | `OVRManagerEditor` toggle "Enable Passthrough Camera Access". Tooltip: use the `PassthroughCameraAccess` component from the Mixed Reality Utility Kit (`Packages/com.meta.xr.sdk.core/Scripts/Editor/OVRManagerEditor.cs`) |

The spike does not flip `isPassthroughCameraAccessEnabled` and does not request the permission. Turning the flag on adds the permission to every Android manifest this project builds, including teleop.

### Absent: the camera component, intrinsics, and extrinsics

`PassthroughCameraAccess` is not declared anywhere in this checkout. `Packages/com.meta.xr.sdk.interaction` has no `HEADSET_CAMERA` or `PassthroughCameraAccess` references. `com.meta.xr.mrutilitykit` is not in `Packages/manifest.json` and is not vendored.

Vendored Core code only *names* the component, and only when an asmdef version-define is set:

| Define | Expression | Assembly | What compiles in |
|---|---|---|---|
| `MRUK_INSTALLED` | `com.meta.xr.mrutilitykit` >= `82` | `Meta.XR.BuildingBlocks.AIBlocks` | `PassthroughCameraAccess` field uses |
| `USING_META_XR_MR_UTILITYKIT_SDK` | `com.meta.xr.mrutilitykit` >= `78.99.99` | `Meta.XR.BuildingBlocks.Editor` | Building Block installers that `AddComponent<PassthroughCameraAccess>()` |

Those references use the unqualified type name `PassthroughCameraAccess` (no `using` for a camera namespace). Members the vendored sources actually call:

| Member | Call site |
|---|---|
| `GetCameraPose()` | `Packages/com.meta.xr.sdk.core/Scripts/BuildingBlocks/AIBlocks/DepthTools/DepthTextureAccess.cs` |
| `GetTexture()` | `ObjectDetectionAgent.cs`, `ImageSegmentationAgent.cs`, `LlmAgent.cs`, `ObjectDetectionVisualizer.cs`, `ImageSegmentationVisualizer.cs`, `SegmentationMask3DVisualizer.cs` under `Scripts/BuildingBlocks/AIBlocks/Agents/` |
| `IsPlaying` | `ObjectDetectionAgent.cs`, `ImageSegmentationAgent.cs`, `LlmAgent.cs` |
| `CameraPosition` compared to `PassthroughCameraAccess.CameraPositionType.Left` | the three visualizer scripts above |
| `ViewportPointToRay(Vector2, Pose)` | the three visualizer scripts above |

No vendored file reads an intrinsics property. With MRUK not installed, `MRUK_INSTALLED` is unset, so those calls are not in the current player build.

Meta's published MRUK 203 reference (same major as this Core SDK) describes the component that is missing here: <https://developers.meta.com/horizon/reference/mruk/v203/class_meta_x_r_passthrough_camera_access/>. Intrinsics and extrinsics on that type:

| API | Role |
|---|---|
| `Intrinsics` (`CameraIntrinsics`) | Static sensor data after the component is enabled: `FocalLength` (`Vector2`), `PrincipalPoint` (`Vector2`), `SensorResolution` (`Vector2Int`), `LensOffset` (`Pose`, sensor relative to the headset) |
| `GetCameraPose()` | World pose of the passthrough camera (extrinsics at the current timestamp) |
| `ViewportPointToRay` / `WorldToViewportPoint` | Image ↔ world. Optional cached pose argument for work that outlives the frame |
| `Timestamp` (`DateTime`) | Timestamp of the latest camera image |
| `GetColors()` | CPU `NativeArray<Color32>` of the latest image. Meta marks this expensive |
| `GetTexture()` | GPU texture. A blocking `Graphics.Blit` sees the previous frame |
| `RequestedResolution` / `CurrentResolution` | Ask vs. what the camera actually opened |
| `GetSupportedResolutions(CameraPositionType)` | Per-eye list |
| `IsPlaying` / `IsSupported` / `IsUpdatedThisFrame` | Lifecycle |
| `CameraPositionType` | `Left`, `Right` |

`1280×720` is in Meta's supported-resolution table for v83 and later, which includes MRUK `203.0.0`. It is absent from that table for v81 and earlier. Source: <https://developers.meta.com/horizon/documentation/unity/unity-pca-documentation/> (page last updated 2026-10-07).

Registry check on 2026-10-14 against `https://npm.developer.oculus.com/com.meta.xr.mrutilitykit`: version `203.0.0` is published. Its `package.json` depends on `com.meta.xr.sdk.core` `203.0.0`, `com.unity.ai.navigation` `1.1.4`, and `com.unity.nuget.newtonsoft-json` `3.0.2`. `licensesUrl` is the Oculus SDK licence: <https://developers.meta.com/horizon/licenses/oculussdk/>.

The Meta scoped registry in `Packages/manifest.json` only lists scope `com.meta.xr.sdk`. That scope does not match `com.meta.xr.mrutilitykit`.

### Not the headset RGB camera

These vendored types are real and easy to confuse with the Passthrough Camera API. They are a different camera path.

| Type | File | Why it is not PCA |
|---|---|---|
| `OVRPlugin.CameraIntrinsics`, `OVRPlugin.CameraExtrinsics` | `Packages/com.meta.xr.sdk.core/Scripts/OVRPlugin.cs` | Mixed-reality-capture external camera. Public reader: `OVRPlugin.GetMixedRealityCameraInfo` → `ovrp_GetExternalCameraIntrinsics` / `ovrp_GetExternalCameraExtrinsics` |
| `OVRPlugin.CameraDeviceIntrinsicsParameters` (`fx`, `fy`, `cx`, `cy`, distortion, fov, `w`, `h`) | same file | `ovrp_GetCameraDeviceIntrinsicsParameters` is a private `DllImport` on `OVRP_1_17_0` with no public wrapper |
| `OVRPlugin.CameraDevice` | same file | `None`, `WebCamera0`, `WebCamera1`, `ZEDCamera` only |
| `Meta.XR.XrStructureType.PassthroughCameraStateGetInfoAndroid` = `1000460000`, `SystemPassthroughCameraStatePropertiesAndroid` = `1000460001` | `Packages/com.meta.xr.sdk.core/Scripts/OpenXRFeatures/OpenXR.cs` | Enum labels only. No struct in this repo reads those values |

`PassthroughManager` (`Assets/Scripts/MR/PassthroughManager.cs`) toggles the insight-passthrough layer (the see-through background). It does not read the RGB cameras.

## Options

| Option | tag36h11 | Licence | Fit for this repo |
|---|---|---|---|
| AprilRobotics AprilTag C library, Android `arm64-v8a` plugin | Reference detector, family `tag36h11` | BSD-2-Clause. Copyright (C) 2013-2016, The Regents of the University of Michigan. <https://github.com/AprilRobotics/apriltag/blob/master/LICENSE.md> | Chosen detector. Not vendored in this commit |
| OpenCV for Unity (Enox Software) | `DICT_APRILTAG_36h11` is an ArUco predefined dictionary. The detector is ArUco, not AprilRobotics | Unity Asset Store Standard EULA for the plugin. Bundled OpenCV is Apache-2.0. The publisher has stated the EULA forbids redistribution in a public repository | Do not add. Do not commit the binaries |
| Meta Passthrough Camera API samples | No AprilTag detector. Camera access, intrinsics, and a Sentis object-detection sample | Sample project, not a UPM package: <https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples>. Read that repo's LICENSE before copying any file. This spike copies none | How-to for `PassthroughCameraAccess` after MRUK is installed |
| `jp.keijiro.apriltag` | Published `TagDetector` supports `tagStandard41h12` only | BSD-2-Clause wrapper around AprilTag | Do not add. It is the wrong family, and its published native binaries are not a Quest camera plugin |

`com.unity.ai.inference` `2.6.1` is already in the manifest (the Sentis path the Meta samples use). It does not detect AprilTags.

## Decision

1. Camera access: add `com.meta.xr.mrutilitykit` `203.0.0` when a headset build is made (steps below). Same Oculus SDK licence as the vendored Core SDK. Do not vendor the package into git; UPM can resolve it. Pin `203.0.0` so it matches vendored Core. Do not jump to `205` or `207`, which are also on the registry.
2. Detector: AprilRobotics AprilTag (BSD-2-Clause), family `tag36h11`, built as `libapriltag.so` for `arm64-v8a`. Pose comes from that library's `estimate_tag_pose`, fed with MRUK `Intrinsics.FocalLength` and `Intrinsics.PrincipalPoint`, and the measured printed tag size in meters. World position of the tag origin is `AprilTagFrames.ComposeWorld(cameraWorldPose, unityCameraPointPose)` after `AprilTagFrames.OpenCvCameraPointToUnityCameraPoint` on the camera-frame translation (OpenCV camera axes are x right, y down, z forward; Unity camera axes are x right, y up, z forward).
3. This commit ships the seam those two pieces plug into: `IPassthroughFrameSource`, `IAprilTagDetector`, `StubAprilTagDetector` (not ready, no detections), and `ManualPlacementBaseline`. Nothing is added to `Assets/scene1.unity`. The host does not request `HEADSET_CAMERA`.
4. Fallback, in force until the measurement tables below are filled and the 1.5 m gate passes: the robot pose in AR is the manually placed transform (`ManualPlacementBaseline`). A failing or missing detector must not be wired into teleop. Drive, deadman, and `RobotProfile` stay untouched.

Rotation from the OpenCV camera frame into Unity is a handedness change. This spike converts the tag-origin *point* only. Leave the angular-error column blank until a tag that faces the headset agrees in yaw with the manual placement. A systematic Y sign error, or an angular error near 180°, means the frame conversion is wrong and the position column is not evidence.

## Why MRUK and the native library are not in this commit

Adding MRUK `203.0.0` turns on `MRUK_INSTALLED` inside the vendored Core SDK and pulls `com.unity.ai.navigation` `1.1.4`. That is a project-wide compile change, and it needs a registry edit. The native `.so` is a binary that this machine cannot produce or licence-check against a Quest. OpenCV for Unity cannot be added offline: it is an Asset Store import, and its EULA does not allow committing it here.

### UPM steps for MRUK (do this on the measurement build)

In `Packages/manifest.json`, add the dependency and extend the Meta scope:

```json
"com.meta.xr.mrutilitykit": "203.0.0"
```

```json
"scopes": [
  "com.meta.xr.sdk",
  "com.meta.xr.mrutilitykit"
]
```

The registry URL stays `https://npm.developer.oculus.com`. Unity will also resolve `com.unity.ai.navigation` `1.1.4`. After import, confirm `PassthroughCameraAccess` exists and that `1280×720` is in `GetSupportedResolutions(CameraPositionType.Left)`.

### Native AprilTag steps (do this before claiming the gate)

1. Build AprilRobotics/apriltag for Android `arm64-v8a` with the NDK from the Unity `6000.5.2f1` Android module. Produce `libapriltag.so`.
2. Commit the binary only together with a copy of upstream `LICENSE.md` (BSD-2-Clause requires the notice to ship with the binary). Suggested path: `Assets/Ohho/AR/AprilTagSpike/Plugins/Android/arm64-v8a/`.
3. Implement `IAprilTagDetector` with P/Invoke to `apriltag_detector_create`, `tag36h11_create`, `apriltag_detector_add_family`, `apriltag_detector_detect`, and `estimate_tag_pose`. Count a hit toward Hz only when Hamming distance is 0.
4. Copy grayscale out of `PassthroughCameraAccess.GetColors()` (or a non-blocking readback). Do not `Graphics.Blit` the texture and detect on that copy; Meta documents that blit as one frame late.
5. Keep `StubAprilTagDetector` as the default until `IsReady` is true on device.

### OpenCV for Unity steps (not recommended)

Import the purchased Asset Store package on a machine that owns a seat. Use the ArUco predefined dictionary `DICT_APRILTAG_36h11`. Do not commit the imported plugin. Treat results as a different detector if they are ever compared with AprilRobotics.

### Samples (reference only)

Clone <https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples> beside this repo, not inside it. Read CameraToWorld for `GetCameraPose` and intrinsics. Meta's own note: request the camera permission from one place. This app already has `OVRPermissionsRequester` / OVRManager startup requests. On the measurement scene, use the OVRManager "Enable Passthrough Camera Access" toggle plus "request on startup". Do not also add a second permission caller.

## Scaffold

Runtime (player assembly, not in `scene1`):

| Type | Role |
|---|---|
| `OmniBot.VR.AprilTagSpike.HeadsetCameraPermission` | The permission string and Horizon OS v74 floor |
| `AprilTagSpikeConfig` | `tag36h11`, `1280×720`, ranges 0.5–3 m |
| `IAprilTagDetector`, `StubAprilTagDetector` | Detector seam. Stub is never ready |
| `ManualPlacementBaseline` | Fallback pose, also the ground truth for error |
| `IPassthroughFrameSource`, `UnavailablePassthroughFrameSource`, `FakePassthroughFrameSource` | No camera in this commit. The fake source is for edit-mode dry runs and has no pixels |
| `AprilTagPoseError`, `AprilTagFrames` | Position error, angular error, Hz, OpenCV→Unity point, world compose |
| `AprilTagMeasurementLog` | Markdown rows for the tables below |
| `AprilTagSpikeHost` | Inert unless placed in a scene. `RequestsHeadsetCameraPermission` is false. `runOnStart` defaults false |

Edit-mode tests: `Assets/Ohho/AR/AprilTagSpike/Editor/AprilTagSpikeTests.cs`, namespace `OmniBot.VR.Tests`, so the existing `-testFilter OmniBot.VR.Tests` run includes them. They do not need a headset.

Acceptance constants in code, matching the issue: at 1.5 m (±1 cm tape tolerance) the run must be ≥ 10 Hz and the position error must be **strictly under** 0.02 m. CPU, frame age, detect time, and angular error are recorded and are not part of the gate.

## Measurements

Empty on purpose. Fill them with the script in the next section. Do not invent numbers.

### Device

| Field | Value |
|---|---|
| Headset | — |
| Horizon OS version (must be v74 or newer) | — |
| `adb shell getprop ro.product.model` | — |
| `adb shell getprop ro.build.display.id` | — |
| MRUK version | not installed (`203.0.0` planned) |
| Detector | `stub-tag36h11` (not ready) |
| Resolution requested | 1280×720 |
| `CurrentResolution` | — |
| Eye | Left |
| Tag family / id | tag36h11 / — |
| Printed black-square side (m) | — |
| App package (`adb shell pm list packages -3`) | — |
| Date | — |

### Results

Position error is the tag origin in tracking space against `ManualPlacementBaseline`. Gate row is 1.50 m: Hz ≥ 10 and position error < 0.02 m, using the worst of three repeats. Other rows are recorded and do not pass the gate.

| Range (m) | Hz | Frame age (ms) | Detect CPU (ms) | App CPU (%) | Position error (m) | Angular error (deg) | Detector | Gate |
|---:|---:|---:|---:|---:|---:|---:|---|---|
| 0.50 | — | — | — | — | — | — | — | record |
| 1.00 | — | — | — | — | — | — | — | record |
| 1.50 | — | — | — | — | — | — | — | gate |
| 2.00 | — | — | — | — | — | — | — | record |
| 3.00 | — | — | — | — | — | — | — | record |

`AprilTagMeasurementLog.FormatProtocolTemplate()` prints this empty table. `AprilTagMeasurementLog.FormatRow` prints a filled row.

## Quest measurement script

Run this on a Quest 3 or Quest 3S. This spike's EditMode tests do not collect these numbers.

1. Confirm the headset software is Horizon OS v74 or newer (Settings → System → Software Update). From a PC with USB debugging: `adb shell getprop ro.product.model` and `adb shell getprop ro.build.display.id`. Write both into the device table.
2. Install MRUK `203.0.0` with the manifest edit above. Import. On a Quest, log `PassthroughCameraAccess.GetSupportedResolutions` for `CameraPositionType.Left`. If `1280×720` is missing, stop and write the list the device returned into the device table. Do not substitute another resolution and still call it the 720p spike.
3. Build `libapriltag.so` and implement `IAprilTagDetector` as above. `IsReady` must be false until the library loads. Hamming 0 only.
4. Print a `tag36h11` image from <https://github.com/AprilRobotics/apriltag-imgs>. Measure the black square's side with a ruler and use that length, in meters, as the tag size passed to `estimate_tag_pose`. Mount it flat, matte, facing the left camera. Do not use a monitor.
5. Create a local scene that is not `Assets/scene1.unity`. Add a Camera Rig, enable passthrough, enable OVRManager "Enable Passthrough Camera Access", and enable the startup permission request on that OVRManager only. Add `PassthroughCameraAccess` (left eye, `RequestedResolution` = 1280×720) and `AprilTagSpikeHost`. Do not save this scene into `ProjectSettings/EditorBuildSettings.asset` on the branch that ships teleop. Build a development APK with that scene enabled, install it, then revert the build-settings change.
6. `ProjectSettings/ProjectSettings.asset` has an empty `applicationIdentifier`, so the package name is assigned at build. After install: `adb shell pm list packages -3`. If the permission dialog was missed: `adb shell pm grant <package> horizonos.permission.HEADSET_CAMERA`. The manifest must already contain the `uses-permission` or the grant fails.
7. Place the headset on a stand. For each distance 0.50, 1.00, 1.50, 2.00, 3.00 m, measure along the left camera's forward axis from the tag center to the camera, ±1 cm. The 1.50 m row is the only gate, and `AprilTagPoseError.IsAcceptanceRange` accepts 1.50 m ± 1 cm.
8. At each distance, put a transform on the tag origin and assign it with `ManualPlacementBaseline.SetPlaced`. That is the reference pose. Detector output is a camera-frame point: flip Y with `AprilTagFrames.OpenCvCameraPointToUnityCameraPoint`, then `AprilTagFrames.ComposeWorld` with the `GetCameraPose()` captured for that frame. Position error is `AprilTagPoseError.PositionErrorMeters`. Leave angular error as `—` until yaw matches the placed transform.
9. Hold still for 10.0 s. Hz = (Hamming-0 detections in that window) / 10. Use `AprilTagPoseError.Hertz`. Frame age is the milliseconds from `PassthroughCameraAccess.Timestamp` to the moment detection returns. Detect CPU is a stopwatch around `Detect` only. Record both; the gate ignores them.
10. CPU: during the 10 s window run `adb shell dumpsys cpuinfo` and record the app process percentage. This is a short dump, not a profiler trace.
11. Repeat the 1.50 m row three times. The gate uses the lowest Hz and the largest position error of the three. Pass means Hz ≥ 10 and position error < 0.02 m (`AprilTagPoseError.MeetsRateAcceptance` and `MeetsPositionAcceptance`). A result of exactly 0.02 m fails.
12. Paste `AprilTagMeasurementLog.FormatRow` output into the results table and fill the device table. If the gate fails, or MRUK / the `.so` / the OS version blocks the run, leave the fallback as manual placement and write the blocker in the device table. Do not point teleop at the detector.

## Consequences

- Teleop behaviour is unchanged. `scene1` does not reference the spike. Deadman and drive code are unchanged.
- The next headset change will touch `Packages/manifest.json` (scope plus MRUK) and will add a native binary plus the BSD notice. That change should stay off the teleop scene.
- Edit-mode coverage is the gate math, the stub, the manual baseline, the permission string against `OVRPermissionsRequester`, and a check that `scene1` and Editor Build Settings do not reference the host.
