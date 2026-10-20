# DRAFT for Varun — OHH-247 demo video

**Status: DRAFT. For Varun to approve before anyone films.**

This file is a shot list and a talk track. It is not a recording, not a release, and not evidence that a take succeeded. Nothing in this checkout was filmed. Do not put a hero video, a latency number, or a success rate on the site from this draft.

Written against OhhO-VR commit `4ebc555` (branch `ohh-247-demo-video-script`, from `main`). The headset app is the Quest client only. Gazebo, the physical OmniBot, and policy training live in the OmniBot repo, not here.

## What this draft is allowed to show

| Beat | Film this | Do not imply |
|---|---|---|
| Garage | Signed-in OhhO Pilot garage. Cards come from the account's `user_robots` rows. Empty copy is "No robots yet. Add one on ohho-robotics.com or the app — it'll appear here." | That the headset created the robot. It does not. |
| Sim screen | Gazebo on the PC, then the teleop page pointed at that PC (`ws://<lan-ip>:9090`, MJPEG on port 8080). | A Simulate button on this commit. That page is on `ohh-212-session-picker` (`cb6a6b3`), not in `4ebc555`. |
| Drive + arm | Sticks drive the base at up to 0.2 m/s. Right hand is the IK target. Pinch is the gripper. Left thumbs-up held 0.5 s toggles `/arm/enable`. | That `sim.launch.py` moves the arm. It does not start the arm driver. The HUD then reads `no arm data`. |
| Real | A short, slow motion only after a tested hardware e-stop, with a second person at that stop. | That both grips cut motor power. They publish `/emergency_stop` and then stop sending new twists. Releasing the grips publishes `false`. |
| AR HUD | Passthrough room behind the floating OhhO screen. Telemetry on that screen: pose, velocity, up to six arm joints, control mode, camera, REC timer. Left controller B hides the screen. | A robot twin, AprilTags, or a detection overlay. Passthrough does not locate the robot. |
| Dataset / policy | Headset **Start Recording** → `REC ep_<unix time>` and a seconds timer → **Stop & Save**. JSONL stays on the headset. Policy is an OmniBot cutaway (`lerobot_engine`, `policy_node`) and only if that process is actually running. | That **Export to Robot** succeeded. The POST target `http://<ip>:8765/upload_episode` has no handler in this repo. The toast can still count a failed POST as exported. Do not use that toast. |

Sign in on camera with the email code. The **Google** button does not sign in. It sets the status to "Simulating Google Login..." and opens the garage with no token.

A green status dot means the WebSocket opened. It does not mean video, odometry, or a stop is working.

---

## 1. 90-second launch cut

One robot, one task (drive to a mark, pinch a light object, set it down). Quest cast or a camera on the headset lenses, plus the cutaways named below. No speed ramp that hides a stall. No composite of a feature that is not on the build in the slate.

**Slate, spoken or burned in for one second, then cut:** build commit, SIM or REAL, and "hardware e-stop attended" if shot 4 is in the cut. If the arm driver is not running, drop shot 3b and do not cover the gap with stock motion.

Total 1:30.

### Shot 1 — Garage (0:00–0:12)

- **Picture.** Passthrough. The OhhO screen is about 1.2 m in front of the operator. Console header reads "Your VR consoles". Tap the OhhO Pilot card. Garage lists the saved robot (name on the card). Status line ends as "N robot(s) in your garage". Finger or controller selects the card. Status becomes "Selected &lt;name&gt; — starting teleop…".
- **Cutaway, 2 s, optional.** The same robot name on ohho-robotics.com, already saved. No typing on camera.
- **Super.** `Same account. Same garage.`
- **VO.** "Put the headset on. The robots you already saved are here."
- **Must be true.** A real Supabase session and at least one `user_robots` row. If the list is empty, that is the fail-case reel, not this shot.

### Shot 2 — Sim screen (0:12–0:26)

- **Picture.** PC monitor: Gazebo, `world:=apartment` or `world:=flat`, robot visible, front camera in the sim. Then the headset teleop page: IP is the PC's LAN address (the one a phone on the same Wi-Fi could open as `http://<ip>:8080`), port `9090`, **Connect**. Dot turns green. Front camera on the page is the Gazebo image, not a photo of the room.
- **Super.** `Sim on the PC. Headset only opens the socket.`
- **VO.** "Simulation is Gazebo on the computer. The headset connects to it."
- **Must be true.** `sim.launch.py` was started with `rosbridge_address:=0.0.0.0`, `web_video_server` is on 8080, and `ROS_DOMAIN_ID` matches (the sim docs use 30). Phone check of port 8080 passed before the take.
- **This commit has no Simulate / Local desktop page.** If the shoot uses `ohh-212-session-picker` instead, replace the IP typing with **Simulate → Local desktop**, show the blue **SIM** strip, and say that build on the slate. Do not mix the two builds in one cut.
- **Do not film** In-headset (offline). That mode never opens a socket and does not move Gazebo. Cloud start is refused. Do not show it as a working cloud robot.

### Shot 3 — Drive + arm (0:26–0:46)

- **3a Drive (0:26–0:36).** Left stick forward, a short strafe, a small yaw. Gazebo base follows. HUD velocity numbers change (`vx`, `vy`, `yaw`). Onboarding hint for OmniBot is "Left stick: strafe · Right stick X: yaw · Right grip: turbo". Full stick is still capped at 0.2 m/s. The right grip does not raise that cap.
- **3b Arm (0:36–0:46).** Only if an arm driver is actually in this session. Right hand moves inside the workspace. The arm follows. Pinch closes the gripper, release opens it. HUD arm line shows joint degrees, not `no arm data`.
- **Super.** `Hands for the arm. Sticks for the base.`
- **VO.** "The stick drives the base. The hand drives the arm."
- **If the arm is not in the launch.** End the shot at 0:36 on the base move. Super: `Arm driver not in this sim launch.` `sim.launch.py` does not start it. `simulation.launch.py` starts `arm_driver_node.py` and the base there is still Gazebo, not the motor driver. Confirm which launch is on screen before rolling.
- **Safety on this shot.** Both grips are a software flag. On this sim they do not stop the Gazebo base. Stopping the launch is the stop. Do not practice a "real robot stop" here and call it a pass.

### Shot 4 — Real (0:46–1:02)

- **Picture.** Wide of the room first: hardware e-stop in frame, second person with a hand on it, wheels already tested off the floor earlier (that test can be B-roll, not this 16 seconds). Headset **Connect** to the robot PC's LAN IP, port 9090. Green dot. One short roll, stop, then one small arm move only if `/arm/enable` was sent while `/emergency_stop` was false and `arm.launch.py` is the node on the bus.
- **Super.** `Real robot. Hardware stop stays in reach.`
- **VO.** "Same controls, on the robot. Someone who is not in the headset holds the hardware stop."
- **Must be true.** Serial is the port that launch expects (`/dev/ttyUSB0` at 115200 unless the launch was changed). `ss` shows rosbridge on `0.0.0.0:9090`, not only loopback. Quest and robot PC are on the same private LAN. Workspace is clear for the arm's 0.33 m reach (link lengths 0.117 m, 0.133 m, 0.080 m).
- **End the take** with the hardware stop, then **Disconnect**. Do not end by pulling the headset off and leaving the last twist as the last command.
- **If the hardware stop is not tested that day, this shot is out.** Do not substitute the sim and grade it to look like the lab.

### Shot 5 — AR HUD (1:02–1:16)

- **Picture.** Operator's real room in passthrough. The screen shows, at the same time: green dot, pose (`x` / `y` metres), velocity, arm joints or the honest `no arm data`, control mode, and the front camera. Press left controller B: the screen hides, the room stays. Press B again: the screen returns.
- **Super.** `The room stays visible. The panel is the HUD.`
- **VO.** "You keep the room. The panel is the status, the camera, and the recording controls."
- **Do not add** a tracked outline on the robot, a floating twin, or tag markers. AprilTag on `ohh-213-apriltag-spike` is a stub detector and is not in this scene. WebRTC is not the camera path. The image is MJPEG. Do not say "sub-100 ms" or any other latency.

### Shot 6 — Dataset / policy (1:16–1:30)

- **Picture.** On the teleop page, **Start Recording**. The line reads `REC ep_<unix time>` plus a live second count. Drive the same short move again. **Stop & Save**. Status returns to `Idle`.
- **Cutaway.** A file listing of that episode's `.jsonl` on the headset path `recordings/`, or a LeRobot dataset directory that already exists on the workstation (`observation` images, parquet). Then, only if a checkpoint is loaded and the process is up: OmniBot `ros2 launch omnibot_lerobot policy_inference.launch.py` with the sticks idle and the robot doing the task. If the policy is not running, hold on the dataset files.
- **Super, dataset half.** `Recorded on the headset. 30 Hz JSONL.`
- **Super, policy half, if and only if it is really running.** `Policy runs in OmniBot, not in the headset app.`
- **Super, if the policy is not running.** `Policy training is a separate OmniBot step. Not in this take.`
- **VO.** "That demonstration is the dataset. A policy is a later step, on the robot stack, and only when one is actually loaded."
- **Do not press Export to Robot** in this cut. Foxglove on the robot also uses port 8765 and is not this upload. A green "Exported" line is not proof.

**End card,  hold under the last frame if the cut runs long:** `Draft. Quest client + OmniBot sim/robot. Not a release.`

---

## 2. Five-minute walkthrough outline

Same story, with the setup and the limits said out loud. Target 5:00. If a beat is not true that day, skip it and say why. Do not fill the time with the Google button, the export toast, or an offline "sim".

| Time | Beat | What the operator does and says |
|---|---|---|
| 0:00–0:20 | Open | Room, robot, headset. "This is OhhO Pilot on a Quest 3. The app is the headset client. The simulator and the robot stack are OmniBot." Name the commit on the slate. |
| 0:20–0:50 | Sign in | Email used on ohho-robotics.com, **Send Code**, 6-digit code, **Verify**. Mask the inbox if this is a real mailbox. Say that a restored session skips this form. Do not tap Google. |
| 0:50–1:10 | Console | "Your VR consoles." Open OhhO Pilot. "The product list is the VR entries from the site manifest." |
| 1:10–1:40 | Garage | Cards match the website garage. Read the status line. Select the OmniBot card. "Picking a card builds that robot's profile and opens teleop. On this build the next page asks for an IP and port. There is no Simulate button in this commit." |
| 1:40–2:20 | Sim screen | Show the PC: Gazebo world, then a phone opening `http://<lan-ip>:8080`. "If the phone cannot open that page, the headset will not either." On the headset, type that IP, port `9090`, **Connect**. "Green means the socket opened." Cycle the camera with a right-stick press. "Front is the sim image. Wrist and the overhead camera stay on No Signal on `sim.launch.py`." |
| 2:20–3:05 | Drive | Left stick forward, strafe, yaw. Read the velocity line as it changes. "Dead zone is 0.1. Commands go out at 20 Hz on `/cmd_vel/teleop`. The speed cap is 0.2 metres per second." Show both grips once, and say: "That publishes emergency stop true and stops new twists. It does not send a zero twist. Letting go publishes false. On this Gazebo launch that flag does not stop the base. I stop the launch if it keeps moving." |
| 3:05–3:40 | Arm | If the arm driver is up: thumbs-up on the left hand, hold half a second, then move the right hand and pinch. "IK runs at 20 Hz. Joints stay inside the limits in the profile. Pinch is the gripper." If the arm line says `no arm data`, point at that text and skip the gesture. Do not wave a hand at a frozen arm and narrate it as tracking. |
| 3:40–4:10 | Real | Only with the hardware stop and a second person. Repeat Connect against the robot PC. One short move. "The orange REAL strip is not on this commit. It is a label on the session-picker branch, and it does not change the drive loop." End with the hardware stop, then Disconnect. If this block is skipped, say "No real robot in this walkthrough." |
| 4:10–4:35 | AR HUD | Walk the panel: pose, velocity, arm, mode, camera, hints, **Back** (returns to the garage and stops teleop). Hide and show with left B. "Passthrough is the room behind the panel. It does not find the robot." |
| 4:35–5:00 | Dataset / policy | **Start Recording**, a few seconds of motion, **Stop & Save**. "The file is JSONL on the headset, episode name `ep_` plus the unix time, written at 30 Hz: arm, base velocity, odom, and the commands." Mention **Discard** as the way to drop a bad take. "Export to the robot is not available. Policy training and `policy_node` are OmniBot. We show them only when a checkpoint is loaded." End on the dataset file or on a real policy rollout, with the same super as shot 6. |

Controls to mention if a viewer asks, not as a separate montage: right grip alone is turbo in the hint text and does not exceed 0.2 m/s; right-stick press cycles cameras; **Back** leaves teleop.

---

## 3. B-roll and fail-case notes

Capture these on purpose. They are for the longer cut, a backup, or a caption. They are not the 90-second spine unless a hero shot died and the slate says so.

### B-roll that is safe to use

- Hands inside the workspace, pinch open and close, with the arm driver confirmed on.
- The OhhO screen billboarding as the operator turns their head, then left B to hide it so the room is all that remains.
- Garage refresh, and the website and the headset showing the same robot name. No new robot created on camera unless the add-robot flow is actually saved to the account.
- Gazebo `apartment` and `flat`, a two-second establish each.
- Front MJPEG filling the camera well. A separate insert of the wrist and base cameras with **No Signal**, labeled as this launch.
- `REC` timer running, then `Idle` after **Stop & Save**.
- **Discard** on a throwaway episode, so editors have a clean "bad take" button.
- Hardware e-stop, close and in the wide, including the unloaded-wheels test from before the hero. Second person's hand in frame.
- Terminal establishing shots: `ss` showing `0.0.0.0:9090` and `:8080`, not a claim about what the topics contain.

### Fail cases — film the symptom, say the cause

| What you see | What to say | Use in the 90s cut? |
|---|---|---|
| Garage: "No robots yet…" | The account has no row, or the read failed. Add the robot on the website. The headset does not create it. | No. B-roll only. |
| Garage: "Catalog unavailable." or "Garage client unavailable." | Manifest or catalog fetch failed and the bundled fallback did not load. | No. |
| Status dot stays red | Wrong IP, or rosbridge is not on `0.0.0.0:9090`. The video port is 8080. The socket port is 9090. | No. |
| Phone cannot open port 8080 | Bind is still loopback, firewall profile is Public, a VPN, guest Wi-Fi, or AP isolation. `sim.launch.py` without `rosbridge_address:=0.0.0.0` never leaves the PC. | No. |
| Green dot, camera **No Signal** | `web_video_server` is down or in another `ROS_DOMAIN_ID`. Green did not mean video. | One second is allowed inside shot 2 only if the next frame is the working front camera. |
| Green dot, robot never moves | Gazebo still starting, or the stick is inside the 0.1 dead zone. | No. |
| Arm line stays `no arm data` | This launch did not start the arm. | Yes, as the honest end of shot 3 if you skip 3b. |
| Wrist or overhead camera **No Signal** while front works | Expected on `sim.launch.py`. It bridges `/camera/front/image_raw` only. | B-roll. |
| Both grips, Gazebo keeps rolling | This sim does not run the motor driver, so `/emergency_stop` is not a base stop. Kill the launch. | B-roll, with that sentence. Never as the real-robot proof. |
| Grips released and the real base creeps | The motor node can resume the twist it stored. Hardware stop, then disconnect. | Do not publish. Note it for Varun. |
| **Export to Robot** error, or a success count after an error toast | No upload service on 8765. The UI can still increment its exported count after a failed POST. The JSONL is still on the headset. | Do not show the toast. |
| Worked, then died after sleep or `wsl --shutdown` | Portproxy's WSL address went stale. | B-roll for a setup video, not the launch cut. |
| "Simulating Google Login..." | Not a sign-in. | Do not show. |

### Real-robot rules for every take, including B-roll

1. Hardware e-stop tested with the wheels off the floor, second person at it, before any shot with the wheels down.
2. Floor and arm reach clear. People who are not the spotter stay outside that reach.
3. Know the binary. This commit drives from the sticks with no grip held, and it clears `/emergency_stop` when the grips are released.
4. A sim pass is not permission to skip the hardware stop on the real shot.
5. Stop the day with the hardware stop, then **Disconnect**.

### Do not shoot

- Any latency, frame rate, or "it works every time" line. None of those were measured for this draft.
- AprilTags, a digital twin stuck to the robot, WebRTC, visionOS, or a cloud robot session.
- In-headset offline mode presented as Gazebo.
- A policy rollout with someone secretly on the sticks.
- The export success string.

---

## Before Varun calls this ready to film

- [ ] Pick the build and write it on the slate: `4ebc555`, or `ohh-212-session-picker` if the Simulate page is required. Do not mix them.
- [ ] Confirm the sim launch and whether the arm driver is in it. Shot 3b is in or out from that fact.
- [ ] Hardware e-stop tested, spotter named, real shot in or out.
- [ ] Demo account ready. Email code works. Google button stays off camera.
- [ ] Phone can open `http://<lan-ip>:8080` from the Quest's Wi-Fi.
- [ ] Policy: either a loaded checkpoint with sticks idle, or the dataset-only end card. No middle option.
- [ ] Export button stays out of the cut.

Varun's sign-off replaces this DRAFT banner. Until then this page is the script, not the video.
