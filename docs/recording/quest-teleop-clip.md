# Quest teleop clip (OHH-60)

**Needs Varun: Quest + robot, record and upload.**

**Status: clip pending.** Nothing in this checkout was filmed. The README has no video URL. Do not add one until the file is on YouTube and the label matches the take.

This is one 30–60 second passthrough recording: drive the OmniBot base, then move the arm. Sim first. A real robot is a second take, and only after the [OHH-234](https://github.com/ohho-robotics/OhhO-VR/pull/42) safety stages have passed, with a hardware e-stop and a second person on set.

Do not treat this page as those other drafts. Link them; do not restate them.

| Draft | What it already covers | Where |
|---|---|---|
| OHH-247 | 90-second launch cut, five-minute walkthrough, fail cases. Same Quest client commit as this page. | [OhhO-VR PR #44](https://github.com/ohho-robotics/OhhO-VR/pull/44), [docs/OHH-247-demo-video-script.md](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-247-demo-video-script/docs/OHH-247-demo-video-script.md) (`d060836`) |
| OHH-234 | Seven-stage Quest-to-OmniBot HIL kit. Dry-run only. Real wheels wait on that order. | [OhhO-VR PR #42](https://github.com/ohho-robotics/OhhO-VR/pull/42), [docs/hil-vr-session.md](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-234-hil-session-kit/docs/hil-vr-session.md) (`4da8c79`) |
| OHH-39 | OmniBot flagship script. Sim tables, claim lock, `SIM` / `REAL STILL` labels. | [OmniBot PR #76](https://github.com/ohho-robotics/OmniBot/pull/76), [docs/flagship-video-script.md](https://github.com/ohho-robotics/OmniBot/blob/ohh-39-flagship-video-script/docs/flagship-video-script.md) (`2547aaa`) |

Written against OhhO-VR commit `4ebc555` (branch `ohh-60-quest-clip-kit`, from `main`). The headset app is the Quest client only. Gazebo and the physical robot live in OmniBot.

On this commit the drive loop is the one in `DriveSchemeBase`: both grips publish `/emergency_stop` true and then send no new twists; releasing the grips publishes false. That is not a hardware power cut, and it does not publish a zero twist on the rising edge. Right-grip turbo is multiplied and then clamped back to `MaxLinVel` (`0.2` m/s on the OmniBot profile) and `MaxAngVel` (`1.0` rad/s). Left thumbs-up held `0.5` s toggles `/arm/enable`. Pinch is the gripper. A green status dot means the WebSocket opened. It does not mean video, odometry, or a stop is working.

## Safety gate

Film **SIM** before any real-robot attempt. If the day only has time for one file, that file is SIM.

Film **REAL** only when all of these are true the same day:

1. The seven OHH-234 stages have a pass, in order: blocks, deadman, e-stop, link-loss, HMD remove, floor drive, arm. That kit's rule still applies: a headset build from `main` uses both grips as a momentary stop and is the wrong build for stages 2 through 7. Re-read the HIL page before calling a stage passed.
2. A second person is on set with a hand on the hardware e-stop for the whole take, including the room camera.
3. The floor and the arm's reach are clear. Link lengths on this profile are `0.117` m, `0.133` m, and `0.080` m (`0.33` m reach). People who are not the spotter stay outside that reach.
4. The take ends with the hardware stop, then **Disconnect**. Do not end by pulling the headset off and leaving the last twist as the last command.

If any row is false, the published title stays `SIM`. Do not grade Gazebo to look like the lab.

## Shot list

One continuous take. Default length **45 seconds**. Acceptable published length is **30–60 seconds**. No speed ramp, no cut that hides a stall, no feature that is not on the build in the slate.

Say the slate in the first three seconds, on the headset audio or out loud for the room camera: commit `4ebc555`, then the word `SIM` or `REAL`. If the word is `REAL`, also say "hardware e-stop attended."

`SIM` or `REAL` stays burned in for every frame after the slate. The strings are in [Captions](#captions).

| Time | Picture | Super |
|---|---|---|
| 0:00–0:03 | Passthrough of the room. The OhhO screen is in front of the operator. Slate spoken. | `SIM` or `REAL` |
| 0:03–0:12 | Teleop page. IP is the PC's LAN address (sim) or the robot PC (real). Port `9090`. **Connect**. Dot turns green. The front camera well shows the Gazebo image, or the robot camera. It is not a photo of the room. | `Headset opens the socket.` |
| 0:12–0:27 | Left stick: a short forward roll, a short strafe, a small yaw. The base follows on the monitor (sim) or on the floor (real). HUD velocity numbers change. Full stick stays at 0.2 m/s. | `Sticks drive the base. Cap 0.2 m/s.` |
| 0:27–0:42 | Only with an arm driver actually in this session, and the HUD not reading `no arm data`. Left thumbs-up, hold half a second. Right hand moves inside the workspace. Pinch closes the gripper, release opens it. The arm follows. | `Hand drives the arm.` |
| 0:42–0:45 | Sticks released. REAL: the spotter is still on the hardware stop, then **Disconnect**. | `SIM` or `REAL` still up |

The YouTube acceptance file includes both the base move and the arm move. If the HUD says `no arm data`, stop. That recording is a spare, not the upload. OHH-247 shot 3 is the note on which sim launch starts the arm driver and which does not: `sim.launch.py` does not; `simulation.launch.py` starts `arm_driver_node.py` and the base there is still Gazebo. Confirm the launch on the PC before rolling. Do not wave a hand at a frozen arm and narrate it as tracking.

If the arm block is dropped, end between 0:30 and 0:36 on the base move and keep the file off YouTube until an arm take exists.

Assembly for the single published file, still inside 60 seconds:

- **SIM.** The headset recording can be the whole upload. Put two seconds of the room camera at the head if it fits, so the Gazebo monitor is visible. Keep the spare room file.
- **REAL.** The first three seconds of the published file are the room camera: operator, robot, hardware e-stop, spotter's hand. Then cut to the headset passthrough. Drop the same three seconds off the end of the passthrough so the sum stays at or under 60.

## Before you start the recorder

Do this, then start the capture. The recorder does not launch the sim and does not talk to the robot.

1. Pick SIM or REAL from the safety gate. Write it on a card the room camera can see.
2. SIM: Gazebo is already up, rosbridge is on `0.0.0.0:9090`, `web_video_server` is on port `8080`, and `ROS_DOMAIN_ID` matches (the sim docs use 30). A phone on the Quest Wi-Fi opens `http://<lan-ip>:8080` before the take. If the phone cannot, do not start recording. The arm driver is the one you confirmed above.
3. REAL: OHH-234 stages passed today. Spotter in place. Wheels down only because floor drive already passed. Arm only because the arm stage already passed.
4. Quest in developer mode, USB connected, USB prompt allowed. `adb devices` shows the headset as `device`.
5. External camera is already rolling (see [External camera](#external-camera)).
6. Start the headset capture (MQDH, or one of the scripts below). Then do the shot list. Do not expect the script to stop the robot.

Sign in, if you must, with the email code. The **Google** button does not sign in. Keep it out of frame. OHH-247 has the rest of the fail cases (red dot, **No Signal**, export toast). Do not press **Export to Robot** in this take.

## Capture

Prefer Meta Quest Developer Hub. It records the compositor (passthrough plus the OhhO screen). `adb shell screenrecord` and scrcpy record the Android mirror, which on a Quest is sometimes a black frame or the 2D shell. If that happens, stop and use MQDH. Do not invent frames to fill the gap.

### Meta Quest Developer Hub

Install [Meta Quest Developer Hub](https://developer.oculus.com/meta-quest-developer-hub) and connect the headset (USB, or the same Wi-Fi with **ADB over Wi-Fi**). Developer mode is on. Source: [MQDH media capture](https://developers.meta.com/horizon/documentation/native/android/ts-mqdh-media/).

**Cast, then the camcorder button** (the capture Meta documents as source-resolution, higher bitrate than filming the cast window):

1. Device Manager → **Cast**.
2. Aspect dropdown: **Cropped (16:9)** for the YouTube frame. **Original (1:1)** is the uncropped per-eye compositor. Use it only if the crop cuts off the OhhO screen, and letterbox later. Do not scale the picture up with a generative tool. **Cinematic (16:9)** restarts the immersive app and widens the rendered FOV; do not switch it mid-take.
3. Gear → recording **Target Bitrate: 20 Mbps**. Meta's documented bitrate control runs from 5 Mbps (very low, the default) to 40 Mbps (very high). 20 Mbps is the setting for this clip.
4. Leave **Capture Format** off **MAX** for this short file. MAX is the near-lossless path (about 1.2 GB for a minute of mixed-reality footage) and is more than this upload needs.
5. Click the camcorder button in the cast window. Click it again to stop. Keep the take between 30 and 60 seconds.
6. The file is on the headset: File Manager → **On Device** → **Videos**. Copy it off, then delete it from the headset after the copy plays.

**Device Manager → Record** (CTRL+Shift+R) is the other documented path. It caps at three minutes and does not record audio. Gear → **Video Record Settings**, then **Save**:

| Setting | Value for this clip |
|---|---|
| Eye View | **Single Eye, Stereo Audio** (monoscopic; the MQDH default). **Both Eyes, No Audio** is stereoscopic and is the wrong file for a normal YouTube upload. |
| Resolution | **1080p @ 36fps (16:9)**. If that preset is missing, use Cast → Cropped (16:9) instead of typing a resolution. |
| Bitrate | **20 Mbps** (inside the documented 5–40 Mbps range). |

Those files land on the PC, not the headset: `%APPDATA%\odh\captures` on Windows, `~/Library/Application Support/odh/captures` on macOS.

Do not point OBS at the cast window. Meta's note on that path is that the camcorder button keeps source resolution and smoother frame pacing.

### `adb shell screenrecord` (the helper)

Android `screenrecord` writes an H.264 mp4. Default bitrate in current platform sources is 20 Mbps. Default and maximum `--time-limit` is 180 seconds. This clip uses **1920x1080**, **8 Mbps** (`8000000`, YouTube's published 1080p SDR recommendation at up to 30 fps), and a limit inside **30–60 seconds** (default **45**).

From this repo, with one Quest in `adb devices`:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/record-quest-clip.ps1 --seconds 45
```

```text
bash scripts/record-quest-clip.sh --seconds 45
```

`--dry-run` prints the commands and does not call adb. It is safe on a machine with no headset. CI runs that mode.

The commands the helper actually issues, and the only device commands it is allowed to issue:

```text
adb devices -l
adb -s <serial> shell getprop ro.product.model
adb -s <serial> shell rm -f /sdcard/ohh60-quest-teleop.mp4
adb -s <serial> shell screenrecord --size 1920x1080 --bit-rate 8000000 --time-limit 45 /sdcard/ohh60-quest-teleop.mp4
adb -s <serial> pull /sdcard/ohh60-quest-teleop.mp4 recordings/quest-clips/<UTC yyyyMMdd-HHmmss>/ohh60-quest-teleop.mp4
adb -s <serial> shell rm -f /sdcard/ohh60-quest-teleop.mp4
```

`getprop` runs only when `adb devices -l` does not already show a `model:` containing `Quest`. The pull folder is created only after a real recording. `--seconds` outside 30–60 is rejected. More than one Quest requires `--serial`.

Wired mirror, if you need to watch the same stream in VLC instead of saving an mp4, is Meta's documented loop and is **not** this clip (2 Mbps, no file). Do not use it for the upload:

```text
adb exec-out "while true; do screenrecord --bit-rate=2m --output-format=h264 --time-limit 180 -; done"
```

### scrcpy

scrcpy records the same Android mirror as `screenrecord`. If that mirror is black, scrcpy is black too. No `--crop`: a guessed crop hides the passthrough. For 16:9, use MQDH Cropped (16:9).

```text
scrcpy --no-audio --video-codec=h264 --max-size=1920 --video-bit-rate=8M --max-fps=30 --time-limit=45 --record=ohh60-quest-teleop.mp4
```

`--video-bit-rate=8M` is 8 Mbps, the same rate as the helper. `--max-size=1920` caps the longer edge; it does not crop to 16:9. `--time-limit=45` stops the process. If `scrcpy --help` does not list `--time-limit`, omit it and press Ctrl+C between 30 and 60 seconds. The helper scripts do not launch scrcpy.

### External camera

A second camera, on a tripod, landscape, at least 1920×1080, 30 fps. Start it before the headset capture. Do not walk it during the 45 seconds.

- **SIM.** Chest height. The Quest on the operator and the Gazebo monitor are in the same frame, wide enough to see the robot move on that monitor. No other product UI on a screen behind them.
- **REAL.** The same frame holds four things for the whole take: the operator, the robot, the hardware e-stop, and the spotter's hand on that stop. Wide enough that a 0.33 m arm move stays inside the frame.
- The lens glow is not the passthrough. Passthrough is the headset file. This camera is the room file.

## Captions

Burn the label on every frame after the slate. Use one of these, and nothing else:

- `SIM`
- `REAL`

`REAL` is allowed only when the safety gate passed and the spotter's hand is visible in the published file. A sim take keeps `SIM` even if a physical robot is sitting in the room, switched off.

Leave these off the audio and off the supers: a latency number, a success rate, "sub-100 ms", WebRTC, a policy rollout, AprilTags, a digital twin. OHH-39's claim lock is the longer list for the flagship film. This clip does not quote the Nav2 table.

**No AI-generated footage (OHH-28).** This upload is a recording of the session. Do not insert generated frames, a concept render, an AI upscale, or interpolated motion of the arm. OHH-28 keeps generated pictures off the public docs, and the only caption those pictures may carry is `Concept render — not footage of OmniBot`. That caption is how you mark a render. It is not a way to put a render inside this clip.

YouTube's own captions, if you upload an SRT, repeat the burned-in word. Do not let an auto-caption rewrite `SIM` into a claim the slate did not make.

## File names

The helper writes an unlabeled pull (UTC stamp from the clock on the PC):

```text
recordings/quest-clips/<yyyyMMdd-HHmmss>/ohh60-quest-teleop.mp4
```

That directory is gitignored. After you watch the file, copy it to:

| File | When |
|---|---|
| `ohh60-sim-quest-<yyyyMMdd>.mp4` | Headset capture, Gazebo |
| `ohh60-sim-room-<yyyyMMdd>.mp4` | External camera, Gazebo monitor in frame |
| `ohh60-real-quest-<yyyyMMdd>.mp4` | Headset capture, and the safety gate passed |
| `ohh60-real-room-<yyyyMMdd>.mp4` | External camera, e-stop and spotter in frame |

`<yyyyMMdd>` is the UTC date of the take. Do not name a Gazebo recording `real`. Do not commit the mp4s. OHH-234's bag names (`ohh234-<stage>-quest.mp4`) are for that evidence folder, not for this YouTube file.

## Upload checklist

- [ ] Published file is 30–60 seconds and shows a base move and an arm move.
- [ ] HUD during the arm move is not `no arm data`.
- [ ] Burned-in label is `SIM` or `REAL` for the whole picture after the slate.
- [ ] `REAL` is used only if OHH-234's seven stages passed that day, and the spotter's hand on the hardware e-stop is in the published file.
- [ ] No generated frames. No concept render.
- [ ] Title is one of the two lines below, with the UTC date of the take.
- [ ] Description is the template below, with the blanks filled from the slate.
- [ ] Visibility starts **Unlisted**. Watch it once. Switch to **Public** only after this list is true.
- [ ] Then, and only then, replace the README "pending" sentence with the real YouTube URL. Until that URL exists, the README stays as it is.

Title, SIM:

```text
OmniBot Quest teleop (SIM) — passthrough drive and arm — YYYY-MM-DD
```

Title, REAL:

```text
OmniBot Quest teleop (REAL) — passthrough drive and arm — YYYY-MM-DD
```

Description:

```text
Quest passthrough teleop of OmniBot. Label: SIM or REAL (this file is LABEL).
Commit: 4ebc555. Length: 30–60 seconds. The base move and the arm move are in this file.

Headset client: https://github.com/ohho-robotics/OhhO-VR
Robot stack: https://github.com/ohho-robotics/OmniBot
Site: https://ohho-robotics.com

Shot list for this clip: docs/recording/quest-teleop-clip.md on branch ohh-60-quest-clip-kit.
Longer launch script: https://github.com/ohho-robotics/OhhO-VR/blob/ohh-247-demo-video-script/docs/OHH-247-demo-video-script.md (OhhO-VR PR 44)
Real-robot gate: https://github.com/ohho-robotics/OhhO-VR/blob/ohh-234-hil-session-kit/docs/hil-vr-session.md (OhhO-VR PR 42)
OmniBot flagship script: https://github.com/ohho-robotics/OmniBot/blob/ohh-39-flagship-video-script/docs/flagship-video-script.md (OmniBot PR 76)

On this client the speed cap is 0.2 m/s. Both grips publish /emergency_stop. They are not a hardware power cut. A second person holds the hardware stop on any REAL take.

This video is a camera recording of the session. It is not AI-generated footage (OHH-28).
```

Replace `LABEL` with `SIM` or `REAL`. Do not leave the word "or" in the uploaded text.

## What the recorder will not do

`scripts/record-quest-clip.ps1` and `scripts/record-quest-clip.sh` never publish a robot command, never open a robot port, and never start Gazebo. `--dry-run` does not call adb. A dry-run passing in CI is not a recording and not a HIL pass.
