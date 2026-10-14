Quest release of the OhhO VR headset client (`com.ohhorobotics.ohhovr`).

| | |
|---|---|
| Tag | `{{TAG}}` |
| bundleVersion | `{{BUNDLE_VERSION}}` |
| versionCode | `{{VERSION_CODE}}` |
| Commit | `{{COMMIT}}` |
| APK | `{{APK_NAME}}` |
| SHA-256 | `{{SHA256}}` |
| Signing certificate SHA-256 | `{{CERT_SHA256}}` |

Signed with the release keystore stored in GitHub Actions secrets. This is not the CI debug keystore (`androiddebugkey`).

Android ARM64, IL2CPP. The only player scene is `Assets/scene1.unity`.

## What works

- Meta Quest headset client for the OmniBot base and SO-101 arm over ROSBridge (WebSocket, v2 JSON).
- Sign-in, console, garage, and teleop on the passthrough OhhO screen.
- Base drive from the thumbsticks. Turbo is a 2× scale clamped to 0.2 m/s. Both grips held is the software emergency stop (release clears it).
- Hand-tracked arm IK, wrist orientation, and index–thumb pinch for the gripper.
- MJPEG camera view. Episode recording on the headset, and HTTP export when a robot bridge is listening.

## Known limits

- This APK is the headset client only. The robot ROS workspace, the website, and the phone app are not in it.
- WebRTC is not in this build. The camera path is MJPEG.
- visionOS is not this build.
- Connect expects ROSBridge on port 9090, MJPEG on port 8080, and recording export on port 8765. Those processes run on the robot PC.
- The Meta Quest store is not updated by this pipeline. The asset is a signed sideload APK.
- Tags ending in `-rcN` are release candidates.
- Rebuilding this same tag keeps the same versionCode. A replacement build needs a new tag.
- The workflow does not install the APK on a headset.

## Safety notice

- The app commands a physical robot. A person who can reach the robot stays with it while the headset is connected.
- The software emergency stop is both grips held. Letting go of the grips clears that stop. The 0.2 m/s turbo cap is a software limit, not a hardware interlock.
- Confirm the SHA-256 above before install (`sha256sum -c {{APK_NAME}}.sha256`). Install only on a headset you control.
- A release candidate can regress. Do not treat an `-rc` tag as a store-final build.

Checksum file: `{{APK_NAME}}.sha256`
