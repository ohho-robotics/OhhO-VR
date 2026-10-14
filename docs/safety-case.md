# Safety case: VR headset to robot

Linear: [OHH-245](https://linear.app/ohho-robotics/issue/OHH-245)

Review date: 2026-10-14. Method: read the source named below. No robot was connected, commanded, or launched. No Quest headset was used. No bag, video, or hardware log was produced. OHH-234 (hardware HIL) is not done. Ratings below are engineering judgement from that read. They are not measured rates.

Sign-off: unsigned. The placeholder is [release-signoff-checklist.md](release-signoff-checklist.md). Operator procedure: [operator-rules.md](operator-rules.md) (rules version 1).

## Trees that were read

| Tree | Git | What it is for this case |
|---|---|---|
| OhhO-VR `origin/main` | `4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b` | Headset client this branch was cut from. This is the release client until the draft PRs below merge. |
| OmniBot `origin/main` | `d296c9be6cc898e11e8f671c2457292b30cec905` | Robot stack the headset talks to. |
| ohho-os `origin/main` | `85db848268168fa27b4ebb1b5992220b3bfacfcd` | Python `SafetyGate` (OHH-117, merged as ohho-os PR #12). |
| Draft PRs | listed per control | Not on those `origin/main` commits. Pending merge. |

## How a command gets to the motors today

On OhhO-VR `origin/main`, `TeleopController.StartTeleop` builds a profile and `DriveSchemeBase` publishes `geometry_msgs/Twist` on `RobotProfile.Topics.CmdVel`. `RobotProfileFactory` sets that topic to `/cmd_vel/teleop` for any robot with a base. The same factory sets `/arm/joint_commands` when the robot has an arm. The link is a ROSBridge WebSocket. `ConnectionPanel` opens whatever IPv4 address and port the operator types (`ws://`, no token).

On OmniBot `origin/main`, `cmd_vel_mux` forwards `/cmd_vel/teleop` to the base output while mode is `teleop` and `/emergency_stop` is false. `arm_cmd_mux` holds `/arm/joint_commands` in `teleop` mode, so a VR arm command on that topic does not reach the driver until the mode is autonomous. `arm_safety` then clamps joint targets on the driver output. The Yahboom driver stores the last `Twist` and sends it again on every control tick. Its default `max_linear_vel` is 0.5 m/s and is a ROS parameter.

`teleop_safety` (draft) sits on `/cmd_vel/teleop_raw` and `/arm/joint_commands/teleop_raw`, and publishes the capped commands on `/cmd_vel/teleop` and `/arm/joint_commands/teleop`. The headset on `origin/main`, and the headset on draft OhhO-VR PR #26, still publish `/cmd_vel/teleop`. Those twists enter the mux beside the gate. The hard caps in PR #57 do not see them.

ohho-os `SafetyGate` wraps Python transports inside `Robot.connect`. The headset does not use that process. It is not a control on this path.

## Scales

Severity, from the code path, not from a counted incident:

| Level | Meaning |
|---|---|
| S4 | The robot can move in a way that can injure a person. |
| S3 | The robot can move outside the operator's intent, or the operator can act on false information. |
| S2 | A barrier is missing and a further mistake reaches a motor command. |
| S1 | Nuisance only. |

Likelihood, same caveat:

| Level | Meaning |
|---|---|
| L4 | The behaviour is what the current code does on a normal session. |
| L3 | A likely fault (dropped link, wrong IP, late frame) reaches motors or misleads the operator. |
| L2 | Needs a mode change or a second failure. |
| L1 | Needs several independent failures. |

Residual risk is for the **merged** headset plus the **merged** robot, after this review. A draft PR does not lower it.

## FMEA

| ID | Hazard | Cause | Sev | Lik | Controls | Evidence | Residual | Status |
|---|---|---|---|---|---|---|---|---|
| H1 | Unexpected base motion | Thumbsticks publish every 20 Hz on `/cmd_vel/teleop`. Turbo is ×2 on `MaxLinVel` (`DriveSchemeBase.TurboMultiplier`). `CalibrationManager.ApplyTo` writes a stored override onto `MaxLinVel` (edit-mode test sets 0.5). Both-grips e-stop publishes `false` when the grips release. Right grip alone is turbo. No deadman on `origin/main`. | S4 | L4 | C1, C2, C3, C4 | [C1](#c1) [C2](#c2) [C3](#c3) [C4](#c4) | High | OPEN on the release client. C3: evidence on draft PR #57, pending merge, and it does not see `/cmd_vel/teleop`. C4: OHH-193 skipped. PENDING HIL - Varun |
| H2 | Runaway when the link drops | `TeleopController.Update` returns while the link is down and does not publish a final zero. `cmd_vel_mux` / `stream_gate` on `origin/main` have no source-age timeout. The Yahboom driver keeps the last `Twist` and transmits it every tick. `serial.Serial(..., timeout=1.0)` is the port read timeout. | S4 | L3 | C5, C3, C6 | [C5](#c5) [C3](#c3) [C6](#c6) | High | C5: evidence on draft PR #46, pending merge. C3: evidence on draft PR #57, pending merge. C6: evidence on draft PR #26, pending merge. The Gazebo test on PR #57 was not run here and has no result log. PENDING HIL - Varun |
| H3 | Wrong robot connected | The operator types an IP. The garage name is not compared with a live robot identity. There is no sim-versus-real target on `OhhoVrApp.OnRobotPicked`. | S4 | L3 | C7, C13 | [C7](#c7) [C13](#c13) | High | C7: evidence on draft PR #47, pending merge. The headset client is unchanged and still uses `ws://`. C13 is procedural and the in-app gate is not wired. PENDING HIL - Varun |
| H4 | Stale video misleads the operator | `MjpegVideoSource` displays each JPEG it can parse. It has no capture time and no maximum age. The source comment records about 200–500 ms. `WebRtcVideoSource` does not complete signaling. | S3 | L4 | C8, C13 | [C8](#c8) [C13](#c13) | High | C8 records budgets and leaves every measured cell empty. It does not blank a frame or stop the base. PENDING HIL - Varun |
| H5 | Arm pinch | Position servos. No current or force limit in `arm_safety`. The gripper and links can close on a hand inside the joint limits. | S4 | L2 | C9, C10, C11, C13 | [C9](#c9) [C10](#c10) [C11](#c11) [C13](#c13) | High once the arm moves from VR. Medium while `arm_cmd_mux` holds VR arm commands in teleop. | C9 unit-tested on origin/main; that is not a pinch sensor. C10 is why VR arm motion is held on the merged robot. C11: evidence on draft PR #57 and draft PR #53, pending merge. PENDING HIL - Varun |
| H6 | Policy takeover | Policy and VR share `/arm/joint_commands` on origin/main. `/control_mode` `vla` or `nav2` forwards the policy source. The headset publishes `teleop` while sticks are active and `nav2` after 0.5 s idle. | S4 | L2 | C10, C12, C14, C11 | [C10](#c10) [C12](#c12) [C14](#c14) [C11](#c11) | Medium | C10, C12, C14 unit-tested on origin/main. C11 topic split: evidence on draft PR #53, pending merge. PENDING HIL - Varun |
| H7 | Unauthorised remote access | The headset opens `ws://` ROSBridge with no token. Draft PR #47 records that `robot.launch.py` and `omnibot_pi.launch.py` published rosbridge, Foxglove, and video on every interface before that change. | S4 | L3 | C7, C15 | [C7](#c7) [C15](#c15) | High | C7: evidence on draft PR #47, pending merge. `gateway:=false` is an opt-out that binds upstreams on `0.0.0.0` with no authentication. Other launch files were left open by that PR. C15 is not on this path. PENDING HIL - Varun |
| H8 | Battery or thermal runaway | No node in the reviewed OmniBot or OhhO-VR trees stops motion on pack voltage, state of charge, or temperature. OmniBot `README.md` lists battery, wiring, and mounts as not documented. `blackboard.battery_pct` is an optional field with no inhibit. | S3 | L2 | C13 | [C13](#c13) | High | OPEN. No control in the reviewed code. No evidence. TODO Varun: name the sensor and the trip, or accept the gap in writing. PENDING HIL - Varun |
| H9 | REAL session starts with no rules acceptance | `OnRobotPicked` calls `StartTeleop` for every garage robot. Nothing reads operator-rules acceptance. | S4 | L4 | C16 | [C16](#c16) | High until a caller passes `SessionTarget.Real` | Gate and edit-mode tests are on this branch. Not wired. See `RealSessionGate.WiringTodo`. PENDING HIL - Varun for a headset pass after it is wired. |

## Control register

### C1 — Profile numbers, not a hard ceiling

`RobotProfileFactory` sets OmniBot `omnibot` / `omnibot-pro` to `MaxLinVel = 0.2` and `MaxAngVel = 1.0`. `MecanumDriveScheme` multiplies stick input by those fields after the turbo multiplier. `CalibrationManager.ApplyTo` replaces them when the stored override is greater than zero.

- Code: [RobotProfileFactory.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Control/RobotProfileFactory.cs), [MecanumDriveScheme.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Control/Drive/MecanumDriveScheme.cs), [DriveSchemeBase.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Control/Drive/DriveSchemeBase.cs), [CalibrationManager.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Control/CalibrationManager.cs)
- Test: `CalibrationManagerTests.ApplyTo_VelocityOverride_WhenPositive_Overrides` asserts a 0.2 profile becomes 0.5 m/s and 2.0 rad/s. [CalibrationManagerTests.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Editor/Tests/CalibrationManagerTests.cs)
- Robot-side clip: Yahboom `max_linear_vel` default 0.5 m/s, `max_angular_vel` default 2.0 rad/s, both parameters. The driver does not subscribe to `/emergency_stop`. [driver_node.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/yahboom-python-driver/yahboom_ros2/driver_node.py)
- Status: present on origin/main. It is not the 0.2 m/s ceiling. PENDING HIL - Varun

### C2 — Mux e-stop while the bool is true

`stream_gate.set_emergency_stop` stores the bool. A true value zeros the base and blocks sources. A later false value clears it. `test_emergency_stop_zeros_and_blocks_teleop` publishes true, checks that a teleop twist is dropped, publishes false, and checks that the next twist is forwarded.

- Code: [stream_gate.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_hybrid/omnibot_hybrid/stream_gate.py), [cmd_vel_mux.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_hybrid/omnibot_hybrid/cmd_vel_mux.py)
- Test: `TestStopWithinOneCycle.test_emergency_stop_zeros_and_blocks_teleop` in [test_cmd_vel_mux.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_hybrid/test/test_cmd_vel_mux.py)
- Headset side of the same bool: `DriveSchemeBase.HandleEmergencyStop` publishes true on both-grips pressed and false on release. [DriveSchemeBase.cs](https://github.com/ohho-robotics/OhhO-VR/blob/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b/Assets/Scripts/Control/Drive/DriveSchemeBase.cs)
- Status: unit-tested on origin/main. Cleared by a false message. PENDING HIL - Varun

### C3 — `teleop_safety` hard caps, watchdogs, latched e-stop

Draft [OmniBot PR #57](https://github.com/ohho-robotics/OmniBot/pull/57) (OHH-194, branch `ohh-194-teleop-safety`). Not on OmniBot `origin/main` (`teleop_safety.py` is absent there).

`teleop_gate.py` fixes 0.2 m/s, 1.0 rad/s, acceleration 0.5 / 0.5 / 2.0, joint step 0.15 rad, and 300 ms command and heartbeat timeouts. Callers and parameter changes cannot raise them. `/emergency_stop` true latches until `/ohho/estop_reset`. A false message does not clear it. Command loss and heartbeat loss zero the base and do not latch.

- Code: [teleop_gate.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-194-teleop-safety/omnibot-ros2/omnibot_hybrid/omnibot_hybrid/teleop_gate.py), [teleop_safety.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-194-teleop-safety/omnibot-ros2/omnibot_hybrid/omnibot_hybrid/teleop_safety.py)
- Tests in [test_teleop_safety.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-194-teleop-safety/omnibot-ros2/omnibot_bringup/test/test_teleop_safety.py): `test_ceilings_match_omnibot_profile_and_stop_budget`, `test_callers_cannot_raise_caps_or_timeouts`, `test_parameter_changes_cannot_raise_limits_or_use_sim_time`, `test_velocity_cap_clamp`, `test_acceleration_limit_and_free_braking`, `test_command_watchdog_trips_at_300ms_and_does_not_latch`, `test_heartbeat_watchdog_zeros_without_latching`, `test_latch_survives_false_and_reset_requires_service`, `test_joint_limits_and_policy_guard_step`, `test_launch_files_wire_teleop_safety`
- [test_teleop_safety_sim.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-194-teleop-safety/omnibot-ros2/omnibot_bringup/test/test_teleop_safety_sim.py) `TestTeleopSafetyWatchdog.test_client_kill_stops_base_within_350ms` is a Gazebo test that publishes `/cmd_vel/teleop_raw`. This review did not run it. There is no pass log. It is not evidence of a stop.
- Gap: the current headset publishes `/cmd_vel/teleop`, which is the mux input, not `/cmd_vel/teleop_raw`.
- Status: evidence on draft PR #57, pending merge. PENDING HIL - Varun

### C4 — Headset deadman and hard turbo cap (OHH-193)

OHH-193 is skipped. [OhhO-VR PR #6](https://github.com/ohho-robotics/OhhO-VR/pull/6) is an open draft and is not on OhhO-VR `origin/main`. `DriveSchemeBase` on `4ebc555` has no `RequireDeadman` and no hard linear ceiling. Turbo is the constant `2.0`.

- Status: not a control for this release. Do not treat PR #6 as merged.

### C5 — Mux source staleness

Draft [OmniBot PR #46](https://github.com/ohho-robotics/OmniBot/pull/46) (OHH-195, branch `ohh-195-mux-staleness`). `origin/main` `stream_gate.py` and `cmd_vel_mux.py` contain no staleness timeout.

The draft defaults `source_timeout_s` to 0.3 s, clamps longer values down, and zeros `/cmd_vel/out` on the tick at or after the timeout.

- Tests in [test_cmd_vel_mux.py on that branch](https://github.com/ohho-robotics/OmniBot/blob/ohh-195-mux-staleness/omnibot-ros2/omnibot_hybrid/test/test_cmd_vel_mux.py): `test_default_timeout_and_tick_are_armed`, `test_active_source_zeros_once_within_timeout_plus_one_tick`
- Status: evidence on draft PR #46, pending merge. PENDING HIL - Varun

### C6 — Headset heartbeat, latch display, deadman re-press

Draft [OhhO-VR PR #26](https://github.com/ohho-robotics/OhhO-VR/pull/26) (OHH-202, branch `ohh-202-heartbeat-safety`). Not on OhhO-VR `origin/main`.

`TeleopSafetySession` publishes `/ohho/heartbeat` (`std_msgs/Empty`) at 10 Hz while the operator is present and the link is up. Link loss, headset-off, and pause stop the heartbeat and hold motion until a fresh one-grip deadman press. A latched `/ohho/safety/state` suppresses drive. Reset is a confirmed call to `/ohho/estop_reset`.

`RobotProfileFactory` on that branch still sets `CmdVel` to `/cmd_vel/teleop`.

- Tests: `TeleopSafetyTests` in [TeleopSafetyTests.cs](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-202-heartbeat-safety/Assets/Scripts/Editor/Tests/TeleopSafetyTests.cs). Methods include `Heartbeat_PublishesAt10Hz_InsideTheWatchdog`, `WifiLoss_StopsHeartbeat_AndWatchdogZerosWithin350ms`, `HmdUnmounted_StopsHeartbeat_AndWatchdogZerosWithin350ms`, `Reconnect_StaysStillUntilDeadmanRepress`.
- Contract: [OhhoSafetyContract.cs](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-202-heartbeat-safety/Assets/Scripts/Control/Safety/OhhoSafetyContract.cs) (`WatchdogSec` 0.300, `StopBudgetSec` 0.350).
- Status: evidence on draft PR #26, pending merge. The "watchdog zeros" assertions are client-side calculations against a fake link, not a robot. PENDING HIL - Varun

### C7 — TLS token gateway

Draft [OmniBot PR #47](https://github.com/ohho-robotics/OmniBot/pull/47) (OHH-196, branch `ohh-196-secure-gateway`). Not on OmniBot `origin/main`.

`ohho-gateway` terminates TLS and requires one bearer token before it proxies `/rosbridge`, `/video`, and `/foxglove` to loopback. A missing or wrong token returns 401 and does not open the upstream. The headset, Android app, and website are explicitly out of scope of that PR (`docs/security/OHH-196-client-followups.md`). The token identifies the gateway process, not a chassis serial. `sim.launch.py` stays on loopback.

- ADR: [OHH-196-authenticated-gateway.md](https://github.com/ohho-robotics/OmniBot/blob/ohh-196-secure-gateway/docs/adr/OHH-196-authenticated-gateway.md)
- Tests: `test_missing_and_wrong_tokens_are_401_and_do_not_reach_upstream`, `test_valid_token_proxies_http_and_websocket_without_forwarding_the_token` in [test_proxy.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-196-secure-gateway/ohho-gateway/tests/test_proxy.py); `test_robot_and_pi_include_the_gateway_by_default`, `test_sim_launch_stays_on_loopback` in [test_launch_snippet.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-196-secure-gateway/ohho-gateway/tests/test_launch_snippet.py); `test_plain_listener_refuses_non_loopback_before_writing_a_cert` in [test_tls.py](https://github.com/ohho-robotics/OmniBot/blob/ohh-196-secure-gateway/ohho-gateway/tests/test_tls.py)
- Status: evidence on draft PR #47, pending merge. The Quest client in this repo does not send the token. PENDING HIL - Varun

### C8 — Latency budgets (measurement only)

Draft [OhhO-VR PR #25](https://github.com/ohho-robotics/OhhO-VR/pull/25) (OHH-240, branch `ohh-240-latency-budgets`).

[LATENCY_BUDGETS.md](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-240-latency-budgets/docs/LATENCY_BUDGETS.md) sets p95 ceilings and leaves measured p50, p95, n, and verdict empty (`not_measured`). The same file says a missing `OHHO_CAP:` stamp is `incomplete`, not a pass, and that recording heartbeat age does not add a deadman or change the e-stop. Capture is off unless a session is started.

- Status: evidence on draft PR #25, pending merge. No measurement exists. This is not a motion interlock. PENDING HIL - Varun

### C9 — Arm torque latch, hold, joint step

On OmniBot `origin/main`, `arm_safety.py`: `MAX_JOINT_DELTA_RAD = 0.15`, `COMMAND_TIMEOUT_SEC = 0.2`. `/emergency_stop` true sets torque off. Clearing the stop leaves torque off until a later `/arm/enable` true while the stop is false. The file states it is position servos only and does not command the base.

- Code: [arm_safety.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_arm/scripts/arm_safety.py)
- Tests in [test_arm_safety.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_arm/test/test_arm_safety.py): `test_delta_limited_to_0_15_per_cycle`, `test_timeout_holds_last_command_and_logs_once`, `test_estop_disables_torque`, `test_clear_does_not_reenable_until_arm_enable`, `test_command_rejected_while_estop`
- Status: unit-tested on origin/main. No force or current trip. PENDING HIL - Varun

### C10 — Arm mux holds sources in teleop

On OmniBot `origin/main`, `arm_cmd_mux` forwards `/arm/joint_commands` only in autonomous modes. Startup mode is teleop, so policy and VR commands on that shared topic are held. The mux does not clamp.

- Code: [arm_cmd_mux.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ai-ros2/omnibot_rl/omnibot_rl/arm_cmd_mux.py)
- Tests in [test_teleop_default.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_bringup/test/test_teleop_default.py): `test_default_holds_autonomous_sources_and_passes_teleop` (`arm.allows("policy")` is false in teleop), `test_return_to_teleop_stops_both_streams_within_one_call`, `test_arm_cmd_mux_wrapper_without_ros` (policy and RL callbacks publish nothing in teleop, the policy callback publishes in `vla`, and both are empty again after the mode returns to teleop), `test_emergency_stop_stops_both_streams_within_one_call` (true holds arm and base; false allows the previous mode again).
- The headset publishes `/arm/joint_commands`, which is the policy input on this node. Those messages are held in teleop.
- Status: unit-tested on origin/main. PENDING HIL - Varun. This hold is also why a VR arm command does not move the arm on the merged robot.

### C11 — Dedicated arm teleop topic, and joint step inside `teleop_safety`

Draft [OmniBot PR #53](https://github.com/ohho-robotics/OmniBot/pull/53) (OHH-197) forwards `/arm/joint_commands/teleop` only in teleop and holds the policy topic in that mode. PR #57 is stacked on that branch and clamps the raw teleop joint command (see C3 `test_joint_limits_and_policy_guard_step`).

- Status: evidence on draft PR #53, pending merge. Evidence on draft PR #57, pending merge. PENDING HIL - Varun

### C12 — Base mux mode isolation

On OmniBot `origin/main`, teleop wins over Nav2 and VLA while mode is teleop, and a return to teleop zeros the output and blocks VLA.

- Tests in [test_cmd_vel_mux.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ros2/omnibot_hybrid/test/test_cmd_vel_mux.py): `test_nav2_ignored_while_teleop_active`, `test_return_to_teleop_zeros_and_blocks_vla`, `test_switching_to_teleop_stops_autonomous_feed`
- Status: unit-tested on origin/main. The headset can publish mode `nav2` itself after 0.5 s without sticks (`DriveSchemeBase.IdleMode`). PENDING HIL - Varun

### C13 — Operator rules (procedure)

[operator-rules.md](operator-rules.md) rules version 1: hardware e-stop in reach, line of sight, 0.2 m/s and 1.0 rad/s with no calibration raise, area clear, Varun present. This is a procedure. No software reads battery, temperature, or a pinch sensor to enforce it.

- Status: written on this branch. Not a verified interlock. PENDING HIL - Varun for the hardware e-stop check.

### C14 — Policy guard

On OmniBot `origin/main`, `policy_guard` refuses a black or missing camera frame, drops a late action, and clamps joint deltas.

- Code: [policy_guard.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ai-ros2/omnibot_lerobot/omnibot_lerobot/policy_guard.py)
- Tests in [test_policy_guard.py](https://github.com/ohho-robotics/OmniBot/blob/d296c9be6cc898e11e8f671c2457292b30cec905/omnibot-ai-ros2/omnibot_lerobot/test/test_policy_guard.py): `test_tick_black_frame_refused_no_publish`, `test_tick_missing_frame_refused_no_publish`, `test_tick_late_action_dropped`, `test_clamp_positive_large_delta`, `test_node_drops_late_action`
- Status: unit-tested on origin/main. It guards the policy node, not the VR twist topic. PENDING HIL - Varun

### C15 — ohho-os SafetyGate

Merged [ohho-os PR #12](https://github.com/ohho-robotics/ohho-os/pull/12) (OHH-117). [safety.py](https://github.com/ohho-robotics/ohho-os/blob/85db848268168fa27b4ebb1b5992220b3bfacfcd/ohho/safety.py) on `origin/main`: `SafetyGate` wraps a Python `Transport` when `protocol != "simulated"`. Caps, a 300 ms velocity watchdog, a latched e-stop that needs a TTY release, an action allowlist, and a JSONL audit. The module docstring says a safety config file can raise `HARD_MAX_LIN` / `HARD_MAX_ANG`. Test `test_config_file_raises_ceiling_and_overrides_profile` in [test_safety.py](https://github.com/ohho-robotics/ohho-os/blob/85db848268168fa27b4ebb1b5992220b3bfacfcd/tests/test_safety.py) covers that raise. `test_hard_ceiling_cannot_be_raised_by_caller` covers the caller path.

The headset uses `ROSBridgeClient`, not this wrapper.

- Status: unit-tested in ohho-os. Not a control on the VR path. Do not cite it as covering Quest teleop.

### C16 — In-app rules acknowledgement

This branch. `OperatorRulesAcknowledgement` stores `ohho.operator_rules.accepted_version`. `RealSessionGate.TryBegin(SessionTarget.Real)` and `SessionTarget.Unspecified` return false until the stored version equals rules version 1. `SessionTarget.Sim` returns true with no acceptance. A later rules version re-blocks real sessions. `RealSessionAcknowledgement` is the MonoBehaviour hook.

Edit-mode tests: `OperatorRulesAcknowledgementTests` (9 methods) in `Assets/Scripts/Editor/Tests/OperatorRulesAcknowledgementTests.cs`. Unity 6000.5.2f1 batchmode on 2026-10-14, filter `OmniBot.VR.Tests`: 48 passed, 0 failed, 0 skipped (the previous 39 plus these 9). Local log `C:\Users\varun\ohho\logs\ohh-245-editmode.log`, results `C:\Users\varun\ohho\logs\ohh-245-editmode.xml`. That run is the editor on a development PC. It is not a headset session and not OHH-234.

`OhhoVrApp.OnRobotPicked` was not modified. There is no real-versus-sim flag on that path. The TODO is `RealSessionGate.WiringTodo`.

- Status: software gate present and covered by edit-mode tests once those tests have been run (see the test log for this branch). Not called by the app. PENDING HIL - Varun after it is wired.

## Related drafts that do not close a row by themselves

| PR | What was read | Why it is not a closed control |
|---|---|---|
| [OmniBot #64](https://github.com/ohho-robotics/OmniBot/pull/64) OHH-220 | `robot_vr.launch.py` starts driver, cameras, `teleop_safety`, both muxes, and expects clients on `*_raw` topics. | Draft, pending merge. Depends on nodes that are themselves unmerged. No HIL. The docstring says "wheels on blocks". |
| [OmniBot #65](https://github.com/ohho-robotics/OmniBot/pull/65) OHH-203 | `test_sim_vr_launch.py` `test_teleop_moves_only_in_teleop_and_estop_stops`, `test_sim_vr_profile.py` `test_defaults_stay_on_the_real_robot_contract`. | Draft, pending merge. Sim contract. Not run in this review. Not OHH-234. |

## What would close a row

A row is closed only when every one of these is true:

1. The control is on the merged commit the headset and the robot actually run.
2. The headset topic is the one the control subscribes to (`/cmd_vel/teleop_raw` and `/arm/joint_commands/teleop_raw` for C3).
3. OHH-234 has a hardware record Varun accepts: the command, the stop, and a video or bag. This review has none of those.
4. The checklist line for that row is signed. None are.

## External reviewer

TODO (human): no external reviewer is named. Leave the name, date, and comments blank on the checklist until Varun asks a person to review this case.

## Human TODOs

- Varun: sign or reject [release-signoff-checklist.md](release-signoff-checklist.md). It is unsigned.
- Varun: OHH-234 hardware session. Not done. No video, no bag.
- Varun: OHH-193 (PR #6 safety fixes) was skipped. Deadman and a hard headset ceiling are not on `origin/main`.
- Varun: battery and thermal. No inhibit was found. Name the sensor and the trip, or accept the gap in writing.
- Varun: choose an external reviewer, or record that none will be used.
- Engineering, when a sim-versus-real target exists: call `RealSessionGate.TryBegin` from that start path. Do not call it for every garage pick.
