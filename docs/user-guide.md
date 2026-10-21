# VR user guide

OhhO-VR is the Meta Quest client. The robot and the simulator live in [OmniBot](https://github.com/ohho-robotics/OmniBot). This guide matches commit [`4ebc555`](https://github.com/ohho-robotics/OhhO-VR/commit/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b) (the tree this file is written against). Where a screen exists only on another branch, the section says so and links the commit. [OHH-57](https://linear.app/ohho-robotics/issue/OHH-57): no behaviour is described as shipped without that pointer.

Build and version pins are in the [README](../README.md). Unity **6000.5.2f1**. The only enabled scene is `Assets/scene1.unity`.

## Install

1. Install Unity **6000.5.2f1** (changeset `eb73d3b415a1` in `ProjectSettings/ProjectVersion.txt`) and the Android Build Support module.
2. Add this repo as a Unity project. The first open resolves `Packages/manifest.json` (Meta XR Core and Interaction are vendored at 203.0.0).
3. Enable OpenXR on the Android tab, including Meta Quest support, hand tracking, and passthrough.
4. Switch the build target to Android and build `Assets/scene1.unity` to the headset. Developer Mode and a USB cable are required for that deploy.

The README has the same steps with the editor menus. This repo does not produce a visionOS app. Apple Vision Pro is not started here.

`WebRtcVideoSource` does not finish signaling, and `com.unity.webrtc` is not a package dependency. Video in a build from this commit is MJPEG. Treat any "sub-100 ms" comment in the video scripts as a wish, not a measurement.

## Sign in

The screen follows [`OhhoVrApp`](../Assets/Scripts/App/OhhoVrApp.cs): Login, then Console, then Garage, then Teleop.

On launch the app loads `https://ohho-robotics.com/vr/manifest.json` ([`OhhoPlatform`](../Assets/Scripts/Core/Platform/OhhoPlatform.cs)). If that fetch fails it uses `Resources/vr_manifest`. Auth is configured from the manifest, then a saved session is restored. A restored session skips the login form. Otherwise:

1. Type the email you use on ohho-robotics.com.
2. **Send Code**. [`SupabaseAuthService`](../Assets/Scripts/Core/Platform/SupabaseAuthService.cs) asks Supabase for a one-time code.
3. Type the 6-digit code and **Verify**.

The **Google** button does not sign in with Google. [`LoginPanelController.OnGoogleSignIn`](../Assets/Scripts/UI/Auth/LoginPanelController.cs) sets the status to "Simulating Google Login..." and opens the garage with no token. Use the email code.

**Open Website** calls `Application.OpenURL` on the platform base URL (default `https://ohho-robotics.com`).

## Garage

The console lists VR products from the manifest. The one the app is built around is OhhO Pilot. Opening it shows the garage.

[`GarageClient`](../Assets/Scripts/Core/Platform/GarageClient.cs) reads the signed-in user's `user_robots` rows from the Supabase project in the manifest. A robot added on the website shows up here when that read succeeds. An empty list means the account has no robots or the read failed. Add the robot on the website, then come back. The headset does not create the row.

Picking a card builds a [`RobotProfile`](../Assets/Scripts/Control/RobotProfile.cs) and opens teleop. On this commit that is immediate: the teleop page asks for an IP and port. There is no Simulate / Drive real step in `4ebc555`.

### Simulate or Drive real

**Vision for this commit. Implemented on another branch.**

[OHH-212](https://linear.app/ohho-robotics/issue/OHH-212/ohho-vr-robot-card-simulate-drive-real-target-picker-local-desktop) is commit `cb6a6b3` on `ohh-212-session-picker` ([PR #9](https://github.com/ohho-robotics/OhhO-VR/pull/9)). It is not in `4ebc555`. A build from that branch inserts a page after the robot card:

1. **Simulate** or **Drive real**. Drive real always shows the safety notice before connect. Acknowledging it does not enable a grip deadman. See [safety.md](safety.md).
2. Simulate then offers **Local desktop** (one click, IP already filled), **Cloud**, and **In-headset (offline)**.
3. Local desktop uses the IP and port on the page. Defaults are `192.168.1.101` and `9090`. For a PC sim, replace the IP with the address from [setup-local-sim.md](setup-local-sim.md).
4. Cloud start is refused. [setup-cloud.md](setup-cloud.md).
5. In-headset sets [`OfflineKinematicLink`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/Control/OfflineKinematicLink.cs): the UI says connected, no socket is opened, and twists stay on the headset. It is not Gazebo and it does not move a robot.
6. Drive real then asks for an announced robot or a typed IP. [setup-real-omnibot.md](setup-real-omnibot.md).

A blue **SIM** or orange **REAL** strip is drawn on teleop, the camera, and the controls by `SessionModeBanner`. The strip is a label.

The session registry (`robot_sessions`) is optional. If the table is missing, the picker says to use a manual IP. That path works with an empty registry.

## Controls

OmniBot's profile is mecanum plus a 6-DOF arm. Other catalog robots get other schemes. The hints on the teleop page come from the active scheme (`HudHint`), not from a fixed card. For OmniBot the drive hint is "Left stick: strafe · Right stick X: yaw · Right grip: turbo".

| Input | What the code does on `4ebc555` |
|---|---|
| Left stick Y | Forward / back (`linear.x`), dead zone 0.1 |
| Left stick X | Strafe (`linear.y`) |
| Right stick X | Yaw (`angular.z`) |
| Right grip alone | Multiplies stick deflection by 2, then clamps to ±1, then multiplies by `MaxLinVel` (0.2 m/s for OmniBot). Full stick is not faster |
| Both grips | Publishes `/emergency_stop` true and stops new twists. Does not publish a zero twist. Release publishes false. Not a hardware stop |
| Left controller B | Hides or shows the screen ([`ScreenToggle`](../Assets/Scripts/MR/ScreenToggle.cs)) |
| Right stick press | Next camera ([`CameraFeedController`](../Assets/Scripts/Video/CameraFeedController.cs)) |
| Right hand in the workspace | IK target for the arm, published at 20 Hz |
| Right pinch | Gripper |
| Left thumbs-up held 0.5 s | Toggles `/arm/enable` |

Commands go out at 20 Hz on `/cmd_vel/teleop` and `/arm/joint_commands`. The CCD solver in [`ArmIKSolver`](../Assets/Scripts/Input/ArmIKSolver.cs) stops at 50 iterations or 1 mm. Joints are clamped to the limits in `RobotConfig` on every step.

The hand workspace radius constant is 0.40 m. Link lengths used for the SO-101 are 0.117 m, 0.133 m, and 0.080 m.

A grip-to-drive deadman is not in this commit. It is on `ohh-59-gameci-quest-teleop` (`458d2a8`) and is not merged here. Do not brief an operator on that gesture until their build contains it.

## HUD

[`TeleopHudController`](../Assets/Scripts/UI/TeleopHudController.cs) is the teleop page, refreshed at 10 Hz:

- IP, port, Connect, Disconnect, and a status dot. Green means the WebSocket connected. It does not mean video, odometry, or a stop is working.
- Pose from `/odom` (`x`, `y` in metres) and velocity (`vx`, `vy`, yaw rate).
- Up to six arm joint positions from `/arm/joint_states`, or the text "no arm data".
- Control mode from `/control_mode/active`.
- Camera image. The transport that actually runs is MJPEG from port 8080. Right-stick press cycles the profile's topic list.
- Recording status, a timer, and Export.
- The scheme hints, the robot name, and Back (returns to the garage and calls `StopTeleop`).

The first teleop session can show an onboarding card with those hints plus "Both grips: emergency stop · Release to clear". Left B dismisses it. That sentence matches this commit: release clears the software flag.

`sim.launch.py` does not start the arm, so the arm line stays "no arm data" on the local-sim path in [setup-local-sim.md](setup-local-sim.md). The front camera is the one that launch bridges.

## AR

**Passthrough, in this commit.** [`PassthroughManager`](../Assets/Scripts/MR/PassthroughManager.cs) clears the camera background and enables an assigned passthrough layer so the room shows behind the panels. [`WorldSpaceUiPlacer`](../Assets/Scripts/MR/WorldSpaceUiPlacer.cs) defaults to 1.2 m in front of you. Hiding the screen with left B leaves the room visible. If no passthrough layer is assigned, the script logs a warning and the UI sits on black. Passthrough does not detect the robot, draw a twin on it, or read AprilTags.

**AprilTags, not in this commit.** [OHH-213](https://linear.app/ohho-robotics/issue/OHH-213) (`ohh-213-apriltag-spike`, `c5774ac`) is a spike: family name `tag36h11`, a stub detector that returns no detections, and a host that is not in `scene1.unity`. No tag is read on a build from `4ebc555`. Do not expect an overlay on the robot. [setup-real-omnibot.md](setup-real-omnibot.md) says the same for printing tags.

**WebRTC and a visionOS client** are not in this repo.

## Recording

On the teleop page:

1. **Start Recording**. [`EpisodeManager`](../Assets/Scripts/Recording/EpisodeManager.cs) names the episode `ep_<unix time>` if you do not pass a name, and publishes `/vr/record_start` (`std_msgs/String`).
2. [`DatasetRecorder`](../Assets/Scripts/Recording/DatasetRecorder.cs) appends JSONL at 30 Hz: arm position, base velocity, odom position and heading, arm command, base command. The file is written when the episode stops, under `Application.persistentDataPath/recordings/<episode>.jsonl` on the headset.
3. **Stop & Save** publishes `/vr/record_stop` true. **Discard** publishes false and drops the buffer.

**Export to Robot** POSTs each JSONL as multipart field `file` to `http://<robot-ip>:8765/upload_episode`. Nothing in this repo or in the OmniBot tree searched for this guide implements that path. Foxglove on the robot also uses port 8765 and is a WebSocket, not this upload. Expect export to fail. The JSONL remains on the headset.

A robot-side subscriber for `/vr/record_start` is not part of OhhO-VR. Publishing the string does not by itself start a bag or a LeRobot episode.

## Where to go next

| You want | Page |
|---|---|
| Gazebo on a Windows PC, Quest on Wi-Fi | [setup-local-sim.md](setup-local-sim.md) |
| A cloud VM | [setup-cloud.md](setup-cloud.md) — not ready |
| The physical robot | [setup-real-omnibot.md](setup-real-omnibot.md) |
| What the software stop does and does not do | [safety.md](safety.md) |
