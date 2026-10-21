# Safety

A physical OmniBot can hurt someone. The headset software in this repository is not a safety system. For a real robot, a hardware emergency stop that cuts power to the motors must be installed, tested, and within reach of a person who is not wearing the headset before the wheels touch the floor or the arm is enabled.

This page describes the software that exists in OhhO-VR commit [`4ebc555`](https://github.com/ohho-robotics/OhhO-VR/commit/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b) and the robot-side code read from OmniBot branch `ohh-205-wsl2-network` ([OHH-205](https://linear.app/ohho-robotics/issue/OHH-205/quest-wsl2-container-networking-on-windows-mirrored-networking-or)). Claims follow [OHH-57](https://linear.app/ohho-robotics/issue/OHH-57): each behaviour names the file that implements it.

## Hardware stop

Required for every real-robot session. Nothing in OhhO-VR, and nothing in the ROS nodes cited below, opens a relay or cuts battery power. If the PC, the WebSocket, or the driver process dies, those nodes stop being a stop.

Test the hardware control with the wheels unloaded before the first drive. Keep a second person at that control for the whole session.

## What the headset publishes

On commit `4ebc555`, both grip buttons together are the only software stop in the drive loop.

[`DriveSchemeBase.HandleEmergencyStop`](../Assets/Scripts/Control/Drive/DriveSchemeBase.cs) and [`BaseController.Update`](../Assets/Scripts/Input/BaseController.cs):

- On the rising edge, publish `std_msgs/Bool` `true` on `/emergency_stop` and stop publishing new `/cmd_vel/teleop` messages.
- They do not publish a zero `Twist` when the stop turns on. The last non-zero command is left as the last command the peer received.
- On the falling edge (both grips released), publish `/emergency_stop` `false`. Motion commands are allowed again on the next tick.
- There is no latch and no separate reset button in this commit.

Releasing the grips is not "safe". It clears the software flag.

[`MecanumDriveScheme`](../Assets/Scripts/Control/Drive/MecanumDriveScheme.cs) scales stick deflection by 2 when the right grip is held alone, then clamps that deflection to ±1 before multiplying by `Profile.MaxLinVel`. Full deflection is the same speed with or without the grip. The OmniBot flagship profile sets `MaxLinVel` to 0.2 m/s and `MaxAngVel` to 1.0 rad/s ([`RobotProfileFactory`](../Assets/Scripts/Control/RobotProfileFactory.cs), [`RobotConfig.MAX_LINEAR_VEL`](../Assets/Scripts/Core/RobotConfig.cs)). That cap is in the publisher. It is not a motor-driver interlock.

No grip is required to publish velocity in this commit. Thumbsticks alone produce `/cmd_vel/teleop` while the WebSocket is up. The onboarding string "Both grips: emergency stop · Release to clear" ([`OnboardingController`](../Assets/Scripts/UI/OnboardingController.cs)) matches the release-to-clear behaviour and does not add a deadman.

### Other branches, not in this commit

| Branch | Commit | What it adds | What to assume until that commit is in your build |
|---|---|---|---|
| `ohh-59-gameci-quest-teleop` | `458d2a8` | Grip deadman (one grip held to drive; release publishes a zero twist) and a both-grips stop that stays latched until an explicit reset. Turbo linear speed is also hard-capped at 0.2 m/s inside the drive loop. | Do not tell an operator to "hold a grip to drive" or "press left X to reset". Those controls are not in `4ebc555`. |
| `ohh-212-session-picker` | `cb6a6b3` ([PR #9](https://github.com/ohho-robotics/OhhO-VR/pull/9)) | A safety notice before Drive real. [`RealRobotSafety.Notice`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/Control/Session/RealRobotSafety.cs) says the deadman cannot be turned off and that one grip must be held to drive. `DriveSchemeBase` on that commit is the same release-to-clear loop as `4ebc555`. `RequireDeadman` is stored on the profile and is not read by the drive loop. | Read the notice, then still use the hardware stop. The sentence about holding a grip is not enforced by that commit. |

[`RealRobotSafety.TryDisableDeadman`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/Control/Session/RealRobotSafety.cs) returns false. That blocks a UI opt-out. It does not add the grip check.

## What the robot software does when it is actually running

These nodes are in OmniBot, not in this repository. They only help if that process is up and is the process commanding the hardware.

- [`yahboom_controller_node.py`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_driver/scripts/yahboom_controller_node.py) sets `_emergency_stop` from `/emergency_stop`. While the flag is true, `send_motion_command` sends a zero-velocity serial packet. When the bool goes false, the flag clears and the node resumes from `current_twist`, which `cmd_vel_callback` keeps updating during the stop. A grip release can therefore send the last commanded velocity on the next cycle.
- [`arm_safety.py`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_arm/scripts/arm_safety.py) drops follower torque on `/emergency_stop` true (`Torque_Enable` 0) and leaves torque off when the stop clears, until a later `/arm/enable` true arrives while the stop is false. An enable during the stop does not restore torque. Accepted joint targets are clamped to the configured limits and to `MAX_JOINT_DELTA_RAD` (0.15 rad) per cycle. If `/arm/joint_commands/out` is silent for `COMMAND_TIMEOUT_SEC` (0.2 s) the driver holds the last accepted command. Position servos only. There is no force limit. The same description is in [OmniBot `docs/safety.md`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/docs/safety.md).

`sim.launch.py` does not start the Yahboom driver or the arm driver. A Gazebo world that only integrates `/cmd_vel` will not see `/emergency_stop` at all. Killing the launch is the stop for that sim. Do not practice a real-robot stop on that sim and treat a pass as evidence.

`simulation.launch.py` does start `arm_driver_node.py`. The base in that launch is still the Gazebo bridge, not the Yahboom serial node.

## What you do before Drive real

1. Hardware e-stop tested, second person at it, wheels clear of the floor for the first connection.
2. Workspace clear of people, cables, and anything the arm can reach (SO-101 reach used by the solver is 0.33 m from the three link lengths in [`RobotConfig`](../Assets/Scripts/Core/RobotConfig.cs)).
3. Know which binary you are running. `4ebc555` and `cb6a6b3` both drive from the sticks with no grip, and both clear `/emergency_stop` when the grips release.
4. Connect, confirm the status dot, then command a small motion only after the hardware stop has been demonstrated.
5. To end: hardware stop, then disconnect. Do not rely on closing the app. The last `Twist` may still be the last thing the driver stored.

Simulation still uses the same publisher. A sim session is not permission to skip the hardware stop on a later real session. The blue SIM / orange REAL strip exists only on `ohh-212-session-picker`. It is a label. It does not change the drive loop.
