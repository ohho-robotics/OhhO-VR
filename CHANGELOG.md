# Changelog

All notable changes to the OhhO-VR Quest client are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims at [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once a human tags it.

**Status:** not yet tagged. Nothing in this file is a GitHub Release.

## [1.0.0] — not released yet

Intended git tag: `v1.0.0`.

Baseline for what is actually in this branch: `origin/main` at
`4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b` (merge of PR #5).
`ProjectSettings/ProjectSettings.asset` `bundleVersion` is `1.0`, not `1.0.0`.
This commit does not contain `.github/workflows/release.yml`, so a tag of this
SHA would not build the Quest APK job that lives on `ohh-232-release-pipeline`.

The compatibility matrix is [docs/compatibility-matrix.md](docs/compatibility-matrix.md).
Commands a human would run later are in [docs/release-tags.md](docs/release-tags.md).

### Added

On `4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b`:

- Quest headset client (Unity **6000.5.2f1**, Meta XR SDK 203.0.0, OpenXR). The only enabled build scene is `Assets/scene1.unity`. This repository is the headset client only.
- Passthrough OhhO screen: login, console, garage, teleop. `WorldSpaceUiPlacer` default distance is 1.2 m. Left controller B hides or shows the screen.
- ROSBridge WebSocket teleop. Flagship profile `omnibot` / `omnibot-pro` sets `MaxLinVel` 0.2 m/s and `MaxAngVel` 1.0 rad/s, and publishes arm commands on `/arm/joint_commands` (`RobotProfileFactory`).
- Thumbstick drive at 20 Hz. Right grip alone multiplies stick deflection by 2 and the scheme clamps that product to ±1 before scaling by `MaxLinVel`, so full stick on the OmniBot profile stays at 0.2 m/s.
- Both grips publish `/emergency_stop` as `std_msgs/Bool` true and suppress new twists until release, which publishes false. That edge does not itself publish a zero `Twist`. It is not a hardware e-stop.
- Hand-tracked arm IK, wrist orientation, and pinch-to-gripper in the Unity client.
- MJPEG camera view. WebRTC remains roadmap: `WebRtcVideoSource` does not complete signaling, and `com.unity.webrtc` is not in `Packages/manifest.json`.
- Local JSONL episode recording, and an HTTP POST to `http://<robot>:8765/upload_episode`. No upload handler ships in this repository.
- Edit-mode NUnit fixtures under `Assets/Scripts/Editor/Tests/` (`OmniBot.VR.Tests`).

### Not in this commit

These branches exist and are not merged into `4ebc555`. They are not part of a tag of this SHA. Subjects are the branch tips, not a claim that the feature is finished or safe on hardware.

| Branch | Tip | Tip subject |
|---|---|---|
| `ohh-232-release-pipeline` | `6f87566db190045c2281333cbb703e486121c316` | Publish a signed Quest APK from semver tags (the tip commit is a CI audio-thread fix on that branch). |
| `ohh-218-connection-preflight` | `b427a2d11055e3c1e817960369945a501234e9e9` | Check rosbridge, the descriptor, and safety before teleop starts. `PinnedProtocolVersion` is `1.0.0`. |
| `ohh-222-hud-v2` | `71ada80b723469b9900fa5246f75e16caf7f2074` | Show telemetry signals on the VR HUD. Generated C# is `ohho.telemetry.v1` version `1.0.0`. |
| `ohh-225-profile-from-descriptor` | `b9e761d` | Build the teleop profile from the latched robot descriptor. |
| `ohh-197-arm-teleop-mux` | `31c0d6f` | Publish hand IK on `/arm/joint_commands/teleop` (this commit still uses `/arm/joint_commands`). |
| `ohh-202-heartbeat-safety` | `f5a50f7` | Stop teleop when the operator or link goes silent. |
| `ohh-212-session-picker` | `cb6a6b3` | Garage chooses simulate or drive real before teleop. |
| `ohh-217-webrtc-client` | `f769070` | Headset WebRTC receive path. WebRTC is still not the camera path on this commit. |

`ohh-218`, `ohh-222`, and `ohh-232` are not ancestors of each other. Short SHAs above are the tips recorded on 2026-10-14; the three full SHAs in the matrix are the ones the tag sheet requires.

### Known limits (do not drop these when a human writes the real notes)

- visionOS is not in this repository.
- Google sign-in is not completed by `LoginPanelController` on this tree; the email code is the path the login panel implements.
- Idle drive reverts `/control_mode` toward `nav2` after 0.5 s without input (`DriveSchemeBase`). Holding real robots in teleop until the operator starts Nav2 is a different branch (`ohh-227-nav2-headset`), not this commit.
- A tag of this SHA is not a coordinated release with `protocolVersion` `1.0.0`, `RobotDescriptor`, the OmniBot VR profile, or `ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0`.
