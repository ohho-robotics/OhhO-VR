# Quest to OmniBot HIL session (OHH-234)

Dry-run kit for the Quest to OmniBot evidence protocol. A person runs the seven stages on the robot. This repository only holds the checklist, the empty timing table, and a script that prints the plan.

`python scripts/hil_vr_session_dry_run.py` checks these files and exits 0. It does not open a serial, USB, CAN, or network port, and it does not publish a motor command. `OHHO_HIL=1` is not a switch on that script.

Order: blocks -> deadman -> e-stop -> link-loss -> HMD remove -> floor drive -> arm.

The timing sheet is [hil-vr-results.template.md](hil-vr-results.template.md). Copy it into the evidence folder and fill the copy. Measured cells in the template stay empty.

## Safety

Two people for every stage, including blocks.

- One person wears the Quest and operates.
- The second person stands at the robot with a hand on the hardware e-stop for the whole session.
- The robot stays in line of sight. The floor is clear before the wheels come off the blocks.
- Stages 1 through 5 run with the wheels off the ground. Stage 6 is the first stage with the wheels on the floor. Stage 7 keeps people outside the SO-101 reach.
- Any failed row: the second person presses the hardware e-stop, the bag is stopped, and later stages do not run.
- OmniBot ceilings stay 0.2 m/s linear and 1.0 rad/s angular. The session does not raise them.
- Nav2, a policy, and turbo stay off.

## Dependencies

Confirm these in Linear the day of the session. This kit does not query Linear. In Review is the gate for a stage. The session can use an In Review build of that issue; it does not wait for the issue to merge.

| Issue | Gate | Stages |
|---|---|---|
| OHH-220 | In Review before the session | blocks through arm |
| OHH-202 | In Review before the session | blocks through arm |
| OHH-193 | In Review before deadman | deadman through arm |
| OHH-221 | In Review before arm | arm |

[OHH-220](https://linear.app/ohho-robotics/issue/OHH-220) is the real-robot VR launch (`robot_vr.launch.py` on the Pi, wheels on blocks until the contract check has passed). [OHH-202](https://linear.app/ohho-robotics/issue/OHH-202) is the headset heartbeat at 10 Hz, stop on link-loss and on `HMDUnmounted`, the `/ohho/estop_reset` confirm, and no motion after reconnect until the deadman is released and pressed again.

[OHH-193](https://linear.app/ohho-robotics/issue/OHH-193) is the headset deadman (left grip, on for a real session, zero on the release frame), the latched e-stop (A+B or the HUD button), and the hard speed caps. Parallel coding jobs were told to treat OHH-193 as skipped. This session still waits until OHH-193 is In Review before deadman. A headset build from `main` uses both grips as a momentary e-stop and is the wrong build for stages 2 through 7.

[OHH-221](https://linear.app/ohho-robotics/issue/OHH-221) is the arm enable confirm and the clutch. Stages 1 through 6 do not wait on it.

The 350 ms stop is OHH-194 `teleop_safety`: heartbeat and command watchdogs at 0.300 s, ticked every 0.010 s. OHH-220's real VR launch is what starts that node. The headset constant is OHH-202 `StopBudgetSec` (0.350 s).

On 14 Oct 2026 a planning note recorded OHH-202 as OhhO-VR draft PR 26 and OHH-220 as OmniBot draft PR 64, and recorded OHH-193 as skipped for coding. Re-check Linear. Those numbers are a snapshot, not a live status.

## Evidence layout

One bag per stage. Session ids are the suffix the OHH-239 recorder accepts (letters, digits, hyphen, underscore, at most 48 characters):

| Stage | Session id |
|---|---|
| blocks | `ohh234-blocks` |
| deadman | `ohh234-deadman` |
| e-stop | `ohh234-estop` |
| link-loss | `ohh234-link-loss` |
| HMD remove | `ohh234-hmd-remove` |
| floor drive | `ohh234-floor-drive` |
| arm | `ohh234-arm` |

On the robot the recorder writes:

```text
~/.ohho/vr_sessions/<YYYYMMDDTHHMMSSffffffZ>-<session id>/part-0000/
```

`part-0000` is the rosbag2 directory (metadata plus the `.mcap`). The stamp is UTC from the recorder. Start a stage with `std_msgs/String` on `/ohho/vr_session/start` whose data is the session id. End it with `std_msgs/Empty` on `/ohho/vr_session/stop` after the wheels have been still long enough for the zero command to be in the bag. Launch the recorder with `auto_session:=false` so the first heartbeat does not open an unnamed bag, and so link-loss does not close the bag on the 2 s idle timer before `/ohho/vr_session/stop`.

Topics the recorder already subscribes to: `/cmd_vel/teleop*`, `/arm/joint_commands/teleop*`, `/ohho/heartbeat`, `/ohho/telemetry`, `/ohho/safety/state`, `/control_mode*`.

Copy those directories off the Pi by hand into:

```text
evidence/ohh-234/<YYYYMMDD>/
  checklist.md
  timing.md
  video/ohh234-<stage>-quest.mp4
  video/ohh234-<stage>-room.mp4
  blocks/<recorder directory name>/
  deadman/<recorder directory name>/
  estop/<recorder directory name>/
  link-loss/<recorder directory name>/
  hmd-remove/<recorder directory name>/
  floor-drive/<recorder directory name>/
  arm/<recorder directory name>/
```

`<YYYYMMDD>` is the UTC date of the session. `timing.md` is the filled copy of the template. `checklist.md` is this list with a pass or fail on each stage. The room video shows the wheels and the second person's hardware e-stop. Do not commit bags, videos, or filled numbers.

## How to score

Read times from the bag. The deadman, e-stop, and clutch rows pass only when the zero (or the hold) is on the same control frame as the operator action. A watchdog zero hundreds of milliseconds later fails `same tick` even when it is inside 350 ms.

Link-loss and HMD remove pass when the base is zero within 350 ms of the last `/ohho/heartbeat`, `/ohho/safety/state` shows the watchdog reason, the latch stays clear, and after the link returns the base stays still until the deadman is released and pressed again.

`/ohho/heartbeat` on the wire is `std_msgs/Empty` at 10 Hz. `/ohho/safety/state` is JSON `{"armed","latched","reason"}`. `/ohho/estop_reset` (`std_srvs/Trigger`) is the robot-side clear. It refuses while `/emergency_stop` is still true. Publishing false does not clear the latch.

## Stage 1 - Blocks

Wheels off the ground. OHH-220 and OHH-202 are In Review. The second person has the hardware e-stop.

Pass: no wheel motion while the sticks are released, `/cmd_vel/teleop` stays zero, `/ohho/safety/state` is present. Fail the session if the wheels move.

- WOULD: second person holds the hardware e-stop, wheels on blocks
- WOULD: on the Pi, ros2 launch omnibot_bringup robot_vr.launch.py
- WOULD: on the Pi, ros2 launch omnibot_hybrid vr_session_recorder.launch.py auto_session:=false
- WOULD: publish std_msgs/String ohh234-blocks on /ohho/vr_session/start
- WOULD: Quest connects as a real session and the operator does not hold the deadman or the sticks
- WOULD: watch the wheels and /cmd_vel/teleop, then publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 2 - Deadman

OHH-193 is In Review, and the headset build is that branch. Still on blocks.

Left grip is the deadman. Releasing it publishes zero on that frame. Motion after a reconnect waits for a release and a new press.

- WOULD: second person holds the hardware e-stop, wheels still on blocks
- WOULD: publish std_msgs/String ohh234-deadman on /ohho/vr_session/start
- WOULD: operator holds the left grip and a small forward stick so the wheels spin in the air
- WOULD: operator releases the left grip and the bag shows zero /cmd_vel/teleop on that frame
- WOULD: operator presses the left grip again before any further stick input
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 3 - E-stop

Still on blocks, OHH-193 build. A+B or the HUD e-stop latches. Releasing A+B, the HUD button, or the grips leaves the latch set. Motion waits for an explicit reset and a deadman release and re-press. On the robot, `/ohho/estop_reset` is that clear, and it refuses while the stop is still asserted. OHH-202's HUD confirm calls that service. Record which build performed the reset.

- WOULD: second person holds the hardware e-stop, wheels still on blocks
- WOULD: publish std_msgs/String ohh234-estop on /ohho/vr_session/start
- WOULD: operator holds the deadman and a small stick so the wheels spin in the air
- WOULD: operator presses A+B or the HUD e-stop, then releases those buttons and the grips
- WOULD: bag shows a latched /ohho/safety/state and zero /cmd_vel/teleop with the buttons released
- WOULD: operator completes the explicit reset and a deadman release and re-press
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 4 - Link-loss

Still on blocks. Headset build includes OHH-202. Kill Wi-Fi on the headset. The heartbeat stops. The base is zero within 350 ms. The watchdog does not latch. When Wi-Fi returns, the base stays still until the deadman is released and pressed again.

- WOULD: second person holds the hardware e-stop, wheels still on blocks
- WOULD: publish std_msgs/String ohh234-link-loss on /ohho/vr_session/start
- WOULD: operator holds the deadman and a small stick so the wheels spin in the air
- WOULD: operator kills headset Wi-Fi
- WOULD: score last /ohho/heartbeat to first zero /cmd_vel/teleop against 350 ms
- WOULD: operator restores Wi-Fi and leaves the stick alone until the deadman is released and pressed again
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 5 - HMD remove

Still on blocks. Take the headset off so `HMDUnmounted` fires. The heartbeat stops. The base is zero within 350 ms. Putting the headset back on does not resume motion. The deadman is released and pressed again first.

- WOULD: second person holds the hardware e-stop, wheels still on blocks
- WOULD: publish std_msgs/String ohh234-hmd-remove on /ohho/vr_session/start
- WOULD: operator holds the deadman and a small stick so the wheels spin in the air
- WOULD: operator removes the headset
- WOULD: score last /ohho/heartbeat to first zero /cmd_vel/teleop against 350 ms
- WOULD: operator puts the headset on and leaves the stick alone until the deadman is released and pressed again
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 6 - Floor drive

Stages 2 through 5 passed. The second person still has the hardware e-stop. Clear the floor, then set the robot down. The first action on the floor is one latched e-stop, then the documented reset, then a short drive under 0.2 m/s. Deadman release stops the wheels on that frame.

- WOULD: second person holds the hardware e-stop while the robot is lifted onto a clear floor
- WOULD: publish std_msgs/String ohh234-floor-drive on /ohho/vr_session/start
- WOULD: operator latches e-stop once on the floor, resets, then holds the deadman for a short drive under 0.2 m/s
- WOULD: operator releases the deadman and the wheels stop on that frame
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Stage 7 - Arm

OHH-221 is In Review, and the floor stage passed. People stay outside the arm's reach. The second person still has the hardware e-stop. Left X requests enable; on a real robot Left X confirms it. The arm does not move on enable. The right index trigger is the clutch: the hand tracks only while that trigger is held. Releasing the clutch holds the joint command. Servo home ticks are OHH-94 and are not invented here.

- WOULD: second person holds the hardware e-stop, people outside the arm reach
- WOULD: publish std_msgs/String ohh234-arm on /ohho/vr_session/start
- WOULD: operator requests arm enable and confirms it on the real robot, with the clutch released
- WOULD: bag shows no joint step on enable
- WOULD: operator holds the right index trigger, moves within the workspace, then releases the clutch
- WOULD: joint command holds on the clutch-release frame
- WOULD: publish std_msgs/Empty on /ohho/vr_session/stop

## Dry run

From this repository:

```text
python scripts/hil_vr_session_dry_run.py
```

The script prints every `WOULD:` line above and exits 0 when the checklist, the gates, the template, and the bag names are intact. It has no hardware mode.
