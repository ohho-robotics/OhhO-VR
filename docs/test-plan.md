# OhhO VR test plan

OHH-230. This repository is the Quest headset client only. The robot's ROS workspace, the website, and the Android app are not in this checkout. Automated tests here can prove what the headset computes and what it would publish. They cannot prove that a physical robot stopped.

Unity **6000.5.2f1**. The only enabled build scene is `Assets/scene1.unity`.

## 1. Layers

| Layer | What it answers | Where it runs |
|---|---|---|
| Edit mode | Pure C# (drive-scheme factory, calibration, FABRIK, drive-string parsing). Already in `Assets/Scripts/Editor/Tests/`. | `.github/workflows/unity-tests.yml` |
| Play mode | Login stub → console → garage → picker → teleop → both-grips e-stop, against an in-memory link | `.github/workflows/playmode-tests.yml` job `playmode` |
| Rosbridge broker | A disposable Docker rosbridge accepts the websocket and echoes a harmless topic | same workflow, job `rosbridge-integration` |
| ROSBridgeClient round-trip | The headset client connects, advertises, publishes, and receives `/ohho/ci_smoke` | Test exists. It does not run until `OHHO_ROSBRIDGE_URL` is set. See section 8 |
| Quest RC | The real headset, account, and robot | `docs/quest-manual-checklist.md` |

Play-mode sources live in `Tests/PlayMode/`. Unity ignores scripts under `Packages/` unless they have an assembly definition, and an assembly definition cannot reference the predefined `Assembly-CSharp` assembly that holds `IRobotLink`. CI therefore copies the sources into `Assets/OhhoPlayModeTests/` for the play-mode job only. Do not commit that copy.

OHH-207 `SimulatedRobotLink` is not in this tree. `FakeRobotLink` is the stand-in. It implements `IRobotLink`, records publishes, and never opens a socket. It is wrapped in `UNITY_EDITOR`, so a Quest player build does not include it even if someone leaves the staged folder in place.

## 2. Operator flow the tests follow

The shipping route in `OhhoVrApp` is:

1. **Login.** `Start` shows the login panel. A restored session or `OnAuthChanged(true)` shows the console. `OnAuthChanged(false)` returns to login. The play-mode test invokes that method directly. It does not call Supabase.
2. **Console.** Selecting the product whose slug is `pilot` calls `ShowGarage`. That is what `ConsolePanelController` does.
3. **Garage.** `GaragePanelController.OnRobotSelected` raises `RobotPicked`. The app builds a `RobotProfile`, applies saved calibration, remembers the robot, and calls `TeleopController.StartTeleop`.
4. **Picker.** `RobotSelectionController` walks category → type → model → name → confirm. Confirm writes to Supabase. The automated test walks through the name step and then cancels. It does not confirm.
5. **Teleop.** `StartTeleop` builds the drive and manipulation schemes, and advertises topics when the link reports connected.
6. **E-stop.** `DriveSchemeBase` treats both grips as a deadman. Press publishes `/emergency_stop` true and then returns before any velocity publish. Release publishes `/emergency_stop` false. Motion commands are allowed again after release.

The Google button on `LoginPanelController` calls `ShowGarage` immediately. That shortcut is not the RC1 sign-in path and the automated test does not use it.

`TeleopController.Update` reads `OVRInput`, which a batch-mode editor cannot press. The harness ticks the drive scheme `StartTeleop` actually installed, with an explicit `TeleopInput`. That is the same `Tick` the headset calls every frame.

## 3. Devices

| Device | Role on this plan |
|---|---|
| Quest 3 / Quest 3S | Primary. RC checklist. OpenXR, Meta hand tracking, passthrough |
| Unity Editor play mode, no headset | Fake-link flow and, when a broker URL is set, `ROSBridgeClient` |
| Linux editor batch mode (GameCI image `unityci/editor:ubuntu-6000.5.2f1-android-3`) | CI play mode. The Android module is required because Meta editor scripts reference `UnityEditor.Android` |
| Linux standalone player | Not built here. Command is in section 8 |
| Meta XR Simulator / Quest Link | Not available on the CI runner (no GPU, no headset) |
| Apple Vision Pro | Roadmap. No test on this plan |

## 4. Sim / real matrix

| Step | FakeRobotLink (this branch) | Docker rosbridge | SimulatedRobotLink (OHH-207, not merged) | Real robot |
|---|---|---|---|---|
| Login / session route | Automated, no network | — | — | RC checklist, email code |
| Console → Pilot → garage | Automated, in-memory panels | — | — | RC checklist |
| Add-robot picker steps | Automated through name, then cancel | — | — | RC checklist; confirm hits Supabase |
| Profile for OmniBot Pro | Automated. Mecanum, 0.2 m/s, 1.0 rad/s | — | Same profile once the link exists | RC checklist |
| Topic advertise | Recorded, not sent | Python smoke on `/ohho/ci_smoke` | Future | RC, with `ros2 topic echo` if a shell is available |
| `/cmd_vel/teleop` | Recorded and checked against the cap | Not published by the smoke or the Unity broker test | Future | RC, low-speed only, after the abort rules |
| `/emergency_stop` | Recorded true on press, false on release, no non-zero twist while held | Not published | Future | RC both-grips, then the on-screen button if it is visible |
| Camera, recording, passthrough, hands | Not automated | — | — | RC checklist |
| Link down | Automated: zero advertises and zero publishes | Broker can be stopped | Future | RC: red status, then physical e-stop |

Default robot endpoint in the client is `192.168.1.101:9090` (ROSBridge). Recording export expects HTTP `8765`. The camera view expects MJPEG on `8080`. None of those processes are in this repository.

## 5. Safety cases

These are the contracts the automated tests lock in, and the contracts that still belong to the robot.

**S1. Both grips are a deadman.** Rising edge publishes `std_msgs/Bool` true on `/emergency_stop` once. While `EmergencyStopActive` is set, `DriveSchemeBase.Tick` returns before `cmd_vel`. Falling edge publishes false. The onboarding string is "Both grips: emergency stop · Release to clear". Release allows motion again. Do not turn this into a latch without a product decision and a checklist change.

**S2. No non-zero velocity while the deadman is held.** Sticks may be fully deflected. The test counts non-zero twists and requires zero. A later change that also publishes an explicit zero twist on the rising edge still passes. The current code does not publish that zero twist.

**S3. Leaving teleop publishes a zero twist.** `TeleopController.StopTeleop` calls `IDriveScheme.Stop`, which publishes a zero `geometry_msgs/Twist` on `/cmd_vel/teleop` and, if the scheme had entered teleop mode, publishes `control_mode` `nav2`.

**S4. OmniBot Pro speed cap.** Flagship override is 0.2 m/s linear and 1.0 rad/s yaw (`RobotProfileFactory`, matching `RobotConfig`). Full stick maps to that cap. Right-grip turbo multiplies by 2 and is then clamped to 1 before the cap, so turbo cannot exceed 0.2 m/s or 1.0 rad/s. Dead zone is 0.1. A stick value of 0.05 produces no non-zero twist.

**S5. Disconnected link sends nothing.** `AdvertiseTopics` and `DriveSchemeBase.Tick` return when `IsConnected` is false. An e-stop gesture in that state does not publish `/emergency_stop`. The headset cannot deliver a stop after the socket is already down. The robot must watchdog `cmd_vel` and treat loss of the bridge as a stop.

**S6. Robot-side e-stop is mandatory.** `/emergency_stop` true must hard-stop the base and the arm, and must ignore a latched previous twist. The drive scheme does not call `IManipulationScheme.Stop` on the grip deadman. The arm scheme starts disabled. If the arm was enabled, both grips alone do not publish `/arm/enable` false. Firmware has to stop the arm when the bool goes true.

**S7. On-screen e-stop, when the button exists.** `ControlPanel` publishes the same bool through `ROSBridgeClient` on pointer down and clears it on pointer up. It requires a connected socket. The RC checklist covers it when the control is actually on the teleop page. The automated test covers the grip path `TeleopController` feeds.

**S8. Broker tests must not move a robot.** The Docker compose file and the Unity broker test use `/ohho/ci_smoke` only. `OHHO_ROSBRIDGE_URL` points at that broker. It does not point at a live robot.

**S9. Physical e-stop.** Before any RC teleop, the tester can reach the robot's hardware stop. If the base is still moving while both grips are held, keep the grips held and use that hardware stop. Releasing the grips clears the software deadman.

## 6. Regression list

| ID | Expected | Automated |
|---|---|---|
| R1 | Login panel is shown; signed-out returns to login; signed-in shows the console | `LoginStub_Garage_Picker_Teleop_EStop_OnFakeLink` |
| R2 | Pilot opens the garage | same |
| R3 | Picker reaches the name step and cancel hides the steps without a network write | same |
| R4 | OmniBot Pro pick starts teleop, mecanum scheme, max 0.2 m/s and 1.0 rad/s | same |
| R5 | Advertises `/cmd_vel/teleop` as `geometry_msgs/Twist` and `/emergency_stop` as `std_msgs/Bool` | same |
| R6 | Stick inside the 0.1 dead zone adds no non-zero twist | same |
| R7 | Both-grips press publishes e-stop true once and adds no non-zero twist while held | same |
| R8 | Release publishes e-stop false once | same |
| R9 | Full stick after release is vx=0.2, vy=0.2, yaw=−1.0. Turbo forward stays at 0.2 | same |
| R10 | `StopTeleop` publishes a zero twist and `control_mode` `nav2`, and clears the active flag | same |
| R11 | A disconnected fake link advertises nothing and publishes nothing, including e-stop | `DisconnectedFakeLink_PublishesNothing` |
| R12 | Disposable rosbridge echoes `/ohho/ci_smoke` | `smoke_rosbridge.py` in CI |
| R13 | `ROSBridgeClient` round-trip on `/ohho/ci_smoke` | Returns without a socket until `OHHO_ROSBRIDGE_URL` is set |
| R14 | Edit-mode factory, calibration, FABRIK, drive parsing | Existing `unity-tests.yml` (unchanged) |

Manual regressions, each RC: email OTP, real garage list, real robot motion under the cap, both-grips stop, release behaviour, hardware e-stop location, red-status / link loss, MJPEG camera, recording start/stop while e-stop still works, left-controller B hides the screen and is not an e-stop, passthrough, hand-tracking arm only after the base stop is confirmed.

## 7. Play-mode job

Workflow: `.github/workflows/playmode-tests.yml`.

The `playmode` job matches the edit-mode licence gate. If `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` are unset, the job records a notice and succeeds without starting Unity. When they are set, the job:

1. Frees disk space (the Android editor image is large).
2. Runs `bash Tests/PlayMode/stage-into-assets.sh`.
3. Runs GameCI `testMode: playmode` on `unityci/editor:ubuntu-6000.5.2f1-android-3` with `-testFilter OmniBot.VR.PlayModeTests`.

`RosBridgeDockerPlayModeTests` is in that filter. With the url unset it logs the section 8 command and returns without opening a socket. A green play-mode job does not mean the Unity client talked to rosbridge.

Local editor, from the repo root, after a licence is active:

```text
bash Tests/PlayMode/stage-into-assets.sh
unity -batchmode -nographics -projectPath <clone> -runTests -testPlatform playmode -testFilter OmniBot.VR.PlayModeTests -testResults <xml> -logFile <log>
```

Windows editor:

```text
"C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe" -batchmode -nographics -projectPath <clone> -runTests -testPlatform playmode -testFilter OmniBot.VR.PlayModeTests -testResults <xml> -logFile <log>
```

Stage on Windows PowerShell if bash is not on the path:

```text
$dest = "Assets/OhhoPlayModeTests"
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Path $dest | Out-Null
Copy-Item Tests/PlayMode/Runtime $dest\Runtime -Recurse
Copy-Item Tests/PlayMode/Tests $dest\Tests -Recurse
Copy-Item Tests/PlayMode/Editor $dest\Editor -Recurse
```

`EmptySceneSetup` replaces the open scene with an unsaved empty scene before the run, so `scene1` does not start its manifest fetch. Batch mode does not restore the scene. An interactive editor restores the previous scene path after the run. Delete `Assets/OhhoPlayModeTests` when finished. Do not commit it.

The tests snapshot and restore `auto_connect`, the last-robot PlayerPrefs keys, and `ohho.calibration.omnibot-pro`.

## 8. Rosbridge integration

Job `rosbridge-integration` always runs (no Unity licence). It starts `Tests/PlayMode/docker/compose.yml` and runs `Tests/PlayMode/docker/smoke_rosbridge.py`.

The compose service is `ros:humble-ros-base`. It installs `ros-humble-rosbridge-suite` and launches:

```text
ros2 launch rosbridge_server rosbridge_websocket_launch.xml address:=0.0.0.0 port:=9090
```

Host port `9090`. Nothing in that container subscribes to velocity or e-stop. The smoke test advertises and publishes:

- `/ohho/ci_smoke` as `std_msgs/String` (the type string `ROSBridgeClient` uses)
- `/ohho/ci_smoke_ros2` as `std_msgs/msg/String` (the ROS 2 form)

The job passes when either echo comes back. If only the ROS 2 form echoes, the script prints a warning. That warning matters: the headset advertises ROS 1 names (`std_msgs/Bool`, `geometry_msgs/Twist`, `std_msgs/String`). A broker that only accepts `std_msgs/msg/...` will not deliver the headset's messages until the type strings are aligned.

Check the framing client without Docker:

```text
python Tests/PlayMode/docker/smoke_rosbridge.py --self-test
```

### TODO — Unity Editor batch mode against the broker

This is the next command once the broker is healthy and a Unity licence is available. Run it from the repo root with the compose stack still up. Keep the url on the disposable broker.

```text
bash Tests/PlayMode/stage-into-assets.sh
export OHHO_ROSBRIDGE_URL=ws://127.0.0.1:9090
unity -batchmode -nographics -projectPath <clone> \
  -runTests -testPlatform playmode \
  -testFilter OmniBot.VR.PlayModeTests.RosBridgeDockerPlayModeTests \
  -testResults <xml> -logFile <log>
```

GameCI form, added to the `playmode` job environment when the broker is up on that runner: `OHHO_ROSBRIDGE_URL=ws://127.0.0.1:9090` and `customParameters: -testFilter OmniBot.VR.PlayModeTests.RosBridgeDockerPlayModeTests`. The image stays `unityci/editor:ubuntu-6000.5.2f1-android-3`.

A Linux standalone player is a separate build and is not required for that Editor run:

```text
unity -batchmode -nographics -projectPath <clone> -buildTarget StandaloneLinux64 -quit
```

That player build is not produced by this workflow.

## 9. Human TODOs

1. Execute `docs/quest-manual-checklist.md` for RC1 on a Quest 3 or 3S and sign it. That pass is not claimed by CI.
2. Set `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` on the GitHub repo so the play-mode job actually starts Unity. Until then it skips and stays green, same policy as edit mode.
3. Run the Unity batch-mode command in section 8 against the compose broker. Read the smoke warning about type strings before treating a failed echo as a client bug.
4. When OHH-207 lands, drive the same flow through `SimulatedRobotLink` if that type is the production `IRobotLink`. Keep this fake under `Tests/` until then. Do not edit OHH-207's production files from this branch.
5. Robot firmware: honor `/emergency_stop`, watchdog `cmd_vel`, and stop the arm on the bool. The headset gap in S2 and S6 is intentional documentation of today's code, not a waiver.
6. Delete `Assets/OhhoPlayModeTests` after a local run and do not commit it.

## 10. What CI reports

| Job | Green means |
|---|---|
| `playmode` with secrets unset | Unity was skipped. The flow tests did not run |
| `playmode` with secrets set | R1–R11 passed. R13 did not open a socket |
| `rosbridge-integration` | The Docker broker echoed `/ohho/ci_smoke` in at least one type dialect |
| `unity-tests.yml` | Unchanged edit-mode suite. This work does not edit that file |

Quest RC1 is green only when a person completes the checklist.
