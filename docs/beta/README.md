# External Quest beta

This is the tester guide for a short beta of the OhhO Quest app against a simulator. The app in this repository is the headset client only. The only enabled scene is `Assets/scene1.unity`. You do not need Unity.

Varun sends four things before you start:

- the release-candidate APK
- a tester id
- an ohho-robotics.com account that already has a robot in the garage
- where the simulator is (a machine on your LAN, or a rosbridge host and port)

There is no APK, account, or cloud hostname in this repository. If one of those is missing, stop. Do not point the app at a robot address you were not given.

When you are done, fill [feedback-form.md](feedback-form.md) and, if you want a scratch log, [session-notes.md](session-notes.md). The scratch log is not scored. Varun scores the feedback form with `tools/beta_triage.py`.

## Safety

Do this sitting down. Passthrough shows your room. The panels follow your head. Do not walk in the headset.

This beta is for the simulator. The connect field will accept any host. Type only the address you were given.

Drive commands are capped for the OmniBot profile at 0.2 m/s linear and 1.0 rad/s yaw. Holding the right grip is turbo (the stick is scaled by 2) and is still clamped to those caps. Releasing the sticks sends zeros after a short idle. This build has no separate deadman button.

Emergency stop in the app: squeeze both grip buttons together (the hand grips, not the index triggers). The app publishes `/emergency_stop` true and stops sending drive velocity until you release both grips, which publishes false. `simulation.launch.py` does not subscribe to `/emergency_stop`, so a Gazebo model will not react to that message. If you feel unwell, release the sticks, take the headset off, and mark comfort as `quit` on the form.

The controls panel has **Start Rosbag** and **Stop Rosbag**. Leave them alone. They publish `/mission/command`.

## What you are looking at

`Assets/scene1.unity` parents four world-space panels under `OhhO Screen Root` (`SmoothHUD` yaws them with your head). Distances are local to that root, radius 1.6 m:

| Panel | Where it sits | What starts visible |
|---|---|---|
| `OhhO Auth Garage Screen` | straight ahead | **Login Page** ("Sign in to OhhO"). Header stays: OHHO, ohho-robotics.com, **Open Website** |
| `OhhO Camera Screen` | about 45° to your left | **Camera Page** is off until you pick a robot |
| `OhhO Teleop Screen` | about 45° to your right | **Teleop Page** is off until you pick a robot |
| `OhhO Controls Screen` | about 90° to your right | **Controls Page** is off until you pick a robot |

The side panels are active canvases with their pages off, so they can look like empty dark panels until teleop starts. `ScreenToggle` is not on these objects, so left-controller B does not hide the UI in this scene.

Point the cyan laser from the right controller at a control and press **A** (`OVRInput.Button.One`, `joyPadClickButton: 1` on the scene's `OVRInputModule`). A pale bar under a panel moves it. The cyan square at the bottom-right resizes it. You do not need either for the first drive.

Each text field in this scene has `m_HideMobileInput: 1`, and the system-keyboard listener in the UI builder is not saved into the scene. If the Quest keyboard does not appear when you select a field, you cannot type. Report that on the form and stop. Do not hunt for another keyboard.

## 1. Install the APK

Developer Mode has to be on before a computer can install a sideload. The project README already requires Developer Mode for a Quest deploy. Turn it on for this headset, plug in a USB cable, and accept the USB debugging prompt inside the headset.

On the computer:

```text
adb devices
adb install -r <the apk file Varun sent>
```

`adb devices` should list the headset as `device`. If install reports a signature mismatch, uninstall that package and install again only if Varun tells you the package name. A build of this checkout has an empty `applicationIdentifier`, `companyName` DefaultCompany, and `productName` vr_app, so the package id Unity embeds is `com.DefaultCompany.vr_app` and the library name is `vr_app`. The APK you received may differ. Use the name in the invite.

In the headset library, launch that app. You should see your room and the login panel in front of you.

## 2. Start the simulator

The simulator is not in this repository. The Quest app speaks ROSBridge only: **Connect** opens `ws://<ip>:<port>` (`ConnectionManager.Connect`). It does not speak the `ohho sim-serve` JSON protocol, so do not point it at port 8765.

### Local Gazebo

Use an OmniBot checkout on Ubuntu 24.04 with ROS 2 Jazzy and Gazebo Harmonic, from `omnibot-digital-twin/README.md` option C. In a shell where that workspace is built:

```text
source install/setup.bash
export ROS_DOMAIN_ID=30
ros2 launch omnibot_bringup simulation.launch.py rviz:=false
```

`simulation.launch.py` defaults `rosbridge` to true. That starts `rosbridge_websocket` on port **9090** and `web_video_server` on port **8080**.

The Linux compose file does the same launch (`rviz:=false`, `foxglove:=false`) and sets `ROS_DOMAIN_ID=30` and `network_mode: host`:

```text
docker compose -f omnibot-digital-twin/docker/docker-compose.yml up
```

`network_mode: host` is a Linux Docker setting. This repository does not document a Windows or macOS sim launch.

`ROS_DOMAIN_ID` is for the ROS nodes on the sim machine. The headset does not use it. If the domain does not match the rest of that ROS graph, rosbridge can miss Gazebo topics. The headset dot can still turn green.

### Cloud

This repository has no cloud rosbridge URL. Hosted OhhO cloud is a waitlist, not a server you can start from these docs. If Varun sends a host and port that are already running rosbridge, use those in the IP and port fields and mark `sim: cloud` on the form. If you were not sent a host, use local Gazebo or stop.

### Address the headset should use

The teleop IP field is filled at runtime from the saved address, default `192.168.1.101`, port `9090`. That default is the robot placeholder, not "this computer".

- Sim on another machine on the same LAN: type that machine's LAN address. Port stays `9090`. The sim machine must accept inbound TCP 9090 (and 8080 if you want the camera).
- Headset USB-tethered to the same computer that is running rosbridge:

```text
adb reverse tcp:9090 tcp:9090
adb reverse tcp:8080 tcp:8080
```

Then type `127.0.0.1` and port `9090`. `adb reverse` is Android Debug Bridge, not a script in this repo. It lasts until the USB session drops.

Do not clear the port field. If the text is not an integer, `TeleopHudController` still calls connect, and `int.TryParse` yields port 0.

### What a green dot does not mean

The headset publishes base motion on `/cmd_vel/teleop` (`RobotProfileFactory`). The Gazebo bridge in `omnibot_bringup/config/ros_gz_bridge.yaml` subscribes to `/cmd_vel`. `simulation.launch.py` does not start `cmd_vel_mux` (the node that reads `/cmd_vel/teleop` lives in `omnibot_hybrid` and writes `/cmd_vel/out`, which that bridge also does not subscribe to).

So for this launch:

- **Connected** means the WebSocket opened.
- A `[FirstDrive]` line means the app published a non-zero twist on a drive topic.
- The Gazebo base is not wired to that topic. Do not expect the model to roll. Do not add a relay unless Varun asks. A relay would also skip the app's e-stop, which this launch does not consume.

Camera: the bridge does publish `/camera/front/image_raw`. The mobile-manipulator profile also requests `/camera/wrist/image_raw` and `/camera/base/bev/image_raw`. Those can show no signal. The picture is MJPEG from `http://<ip>:8080/stream?topic=...`. WebRTC is not the working path in this repo.

## 3. Sign in and open teleop

Aim at the front panel.

1. The title is **Sign in to OhhO**. Use the email on the account you were given. Select the email field, type it, press **Send Code**.
2. The status line should read `Check <email> for a 6-digit code.` A code field (placeholder `6-digit code`) and **Verify** appear. Enter the code and press **Verify**.
3. Do not press **Sign in with Google**. That button does not sign in. It sets the status to `Simulating Google Login...` and opens the garage with no session, so the robot list will not load.
4. You land on **Your VR consoles**. Select the **OhhO Pilot** card (manifest slug `pilot`). Other cards do not open the garage.
5. The front page becomes **Your garage** and the status starts at `Loading your garage…`. Select the robot card (name, then model and type). An empty garage says `No robots yet. Add one on ohho-robotics.com or the app — it'll appear here.` **Refresh** reloads the list. You cannot add the robot from this headset in a useful way if the account has none. Stop and tell Varun.
6. Picking a card opens three pages: teleop on your right, camera on your left, controls further right. The front panel keeps the header. The robot name on the teleop page comes from the garage card.

The headset needs a network path to ohho-robotics.com for the email code and the garage. The sim WebSocket is a separate connection.

## 4. Connect

On **OhhO Teleop Screen**, under CONNECTION:

1. Replace the IP if the sim is not `192.168.1.101`. Leave the port at `9090` unless you were given another port.
2. Press **Connect**. The status text becomes `Connecting…`, then `Connected` with a green dot, or `Disconnected` with a red dot.
3. **Disconnect** is the other button. **< Garage** goes back.

The first time the status becomes Connected, logcat gets one line:

```text
[FirstDrive] ready since_launch_s=<seconds since the app started>
```

`since_launch_s` on that line is not the number the feedback form wants.

## 5. First drive

Confirm the status text is **Connected**. Push the **left thumbstick** forward past a small deadzone (10% of stick travel). A tiny nudge publishes nothing.

Within a second the app logs one line and writes the same line to `beta/first_drive.txt` under the app's persistent data directory:

```text
[FirstDrive] since_connect_s=<seconds> since_launch_s=<seconds> topic=/cmd_vel/teleop linear_x=<m/s> linear_y=<m/s> angular_z=<rad/s>
```

`since_connect_s` is the beta metric: seconds from the first successful connect in this process to the first non-zero `/cmd_vel/teleop` (or any topic that ends in `/cmd_vel`). Later stick motion does not change it. Exact zeros do not count. Arm topics and `/emergency_stop` do not count.

Put `since_connect_s` in `time_to_first_drive_s`, set `time_to_first_drive_source` to `headset-log`, and set `completed_first_drive` to `yes`.

With the headset still on USB:

```text
adb logcat -d | findstr FirstDrive
```

On macOS or Linux, `findstr` is `grep`. A non-development APK may still print `Debug.Log` to logcat. Pulling `beta/first_drive.txt` with `adb shell run-as <package> cat files/beta/first_drive.txt` works only when that APK is debuggable. Prefer logcat.

If you never get the line, set `completed_first_drive` to `no` and `time_to_first_drive_source` to `not-reached`. A phone stopwatch is `self-timed` and is the weaker source.

Right stick X is yaw. Right grip is turbo, still inside the caps above. Right thumbstick press, or **Next Camera** on the camera panel, cycles cameras.

## 6. Send feedback

Copy [feedback-form.md](feedback-form.md). Keep the key names. `notes:` stays last. `confusion:` is either `none` or a list of `- ` lines. Send the file back to Varun. One session, one file.

If you use GitHub, the same questions are in `.github/ISSUE_TEMPLATE/beta-feedback.yml`. The triage script accepts that issue body as well as the key/value file.

Optional scratch log: [session-notes.md](session-notes.md).

### How the score is chosen

`python tools/beta_triage.py <files or a folder of filled forms>` prints P1, then P2, then P3.

- **P1**: any crash (`crash_count` above 0, or `crashes` other than none / no / n/a), first drive not completed, time source `not-reached`, or comfort `quit`.
- **P2**: comfort `mild`, one or more confusion points, or `time_to_first_drive_s` greater than 180.
- **P3**: a completed drive with none of those. A blank time on an otherwise clean drive stays P3 and is marked missing.

Fake fixtures live in `tools/testdata/beta_feedback/`. They are not reports. `python tools/test_beta_triage.py` checks them.

## If something is wrong

| What you see | What it means here |
|---|---|
| `adb devices` is empty | Developer Mode, the USB prompt, or the cable. The headset has to show as `device`. |
| Library has no app | The install did not succeed, or the library name is `vr_app` / the name in the invite. |
| Keyboard never opens | Expected risk: scene fields set `m_HideMobileInput: 1`. Report it. You cannot finish login without typing. |
| Status stays `Disconnected` | The WebSocket did not open. Check the IP, that port 9090 is rosbridge, and that the headset can route to it (`adb reverse` if you are tethered). |
| Connected, telemetry stays `x —` | The socket is up and `/odom` is not arriving. On the sim PC, check `ROS_DOMAIN_ID` and that `simulation.launch.py` is still running. |
| Connected, no `[FirstDrive]` line after a real stick push | The stick may be inside the 0.1 deadzone, or teleop is not the active page. Push the left stick further. The Gazebo model is not the signal. |
| Camera says no signal | MJPEG needs `web_video_server` on port 8080. Wrist and BEV topics are not on the Gazebo bridge this guide uses. Front camera topic is `/camera/front/image_raw`. |
| Google button dumped you on an empty garage | That button does not authenticate. Go back and use email plus the 6-digit code. |
