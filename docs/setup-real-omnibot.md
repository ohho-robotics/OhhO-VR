# Real OmniBot

Drive a physical robot only with a tested hardware emergency stop and a second person who can reach it. The headset does not cut motor power. Read [safety.md](safety.md) before this page. The software stop in OhhO-VR commit [`4ebc555`](https://github.com/ohho-robotics/OhhO-VR/commit/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b) publishes `/emergency_stop` and then stops sending new twists. It does not send a zero twist, and releasing both grips publishes `false`.

The robot stack is OmniBot, not this repo. The launch lines below are the hardware block in the [OmniBot README](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/README.md). That README marks them `docs-test: skip hardware`. They were not run for this page. There is no photo of a wired robot in OhhO-VR.

## Launch

On the robot PC (the README's block is Ubuntu with ROS 2 Jazzy, after `source install/setup.bash`):

```bash
ros2 launch omnibot_bringup robot_with_joy.launch.py
ros2 launch omnibot_bringup perception.launch.py
ros2 launch omnibot_arm arm.launch.py
```

`robot.launch.py` is the bringup those files sit on. It starts the Yahboom driver on `/dev/ttyUSB0` at 115200, and by default starts rosbridge on port 9090 and Foxglove on port 8765 ([`robot.launch.py`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_bringup/launch/robot.launch.py)). The rosbridge node in that file sets `port` and does not set `address`. Before the Quest connects, confirm with `ss` that 9090 is on `0.0.0.0` or `*`. If it is on `127.0.0.1`, the headset on Wi-Fi cannot open it. Do not publish that port to the internet. rosbridge in this setup has no authentication.

`teleop_record.launch.py` is the next line in the OmniBot README. It is a LeRobot record launch, not the headset recorder. Leave it out until you mean to run that stack.

The headset default IP is `192.168.1.101` port `9090` ([`ConnectionManager`](../Assets/Scripts/Core/ConnectionManager.cs)). Replace the IP with the robot PC's LAN address. On a build from this commit, type it on the teleop page and press Connect. On a build from `ohh-212-session-picker` ([PR #9](https://github.com/ohho-robotics/OhhO-VR/pull/9)), choose **Drive real**, read the notice, then Connect. The notice is not a substitute for the hardware stop, and the deadman sentence in it is not enforced by that commit. See [safety.md](safety.md).

Camera MJPEG is port 8080, topic path `/stream?topic=...`. `perception.launch.py` has to be publishing the image topics or the HUD shows "No Signal". The OmniBot flagship profile asks for `/camera/front/image_raw`, `/camera/wrist/image_raw`, and `/camera/base/bev/image_raw`.

Foxglove's WebSocket on 8765 is not the headset's recording upload. The upload URL in [`TeleopHudController`](../Assets/Scripts/UI/TeleopHudController.cs) is `http://<ip>:8765/upload_episode`. No handler for that path was found in OhhO-VR or in the OmniBot tree read for this page. Export will fail until a service exists. Local JSONL on the headset still saves. See the [user guide](user-guide.md).

## Tags

AprilTag tracking is not shipped.

[OHH-213](https://linear.app/ohho-robotics/issue/OHH-213) on branch `ohh-213-apriltag-spike` commit `c5774ac` records a spike for family `tag36h11` ([`AprilTagSpikeConfig`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-213-apriltag-spike/Assets/Ohho/AR/AprilTagSpike/AprilTagSpikeConfig.cs)). The detector wired in that commit is `StubAprilTagDetector` (no detections). The frame source reports unavailable. The host is not added to `Assets/scene1.unity`, does not request the headset camera permission, and does not run unless `runOnStart` is enabled. The native AprilTag library and Meta MRUK are not in that commit. The ADR on that branch says the fallback until a Quest measurement passes is manual placement, and that a missing detector must not be wired into teleop.

No AprilTag package, family, or tag size showed up in a search of OmniBot `ohh-205-wsl2-network`. Do not print tags and expect this headset build to find the robot. There is no tag layout, tag size, or mounting photo to follow. That is a human TODO after a detector exists.

Passthrough (the room visible behind the panels) is a different feature and is in this commit. It does not locate the robot. See the [user guide](user-guide.md).

## Checklist before the wheels touch the floor

1. Hardware e-stop fitted, tested with the wheels off the ground, and a second person standing at it.
2. Clear the floor and the arm's reach. The solver uses link lengths 0.117 m, 0.133 m, and 0.080 m ([`RobotConfig`](../Assets/Scripts/Core/RobotConfig.cs)).
3. Serial cable on `/dev/ttyUSB0` (or you have changed the launch argument). Robot PC and Quest on the same private LAN. Guest Wi-Fi will not work.
4. `ss` shows rosbridge on `0.0.0.0:9090`, not only loopback.
5. You know the binary. This commit and the session-picker commit both send velocity from the thumbsticks with no grip held, and both clear `/emergency_stop` when the grips are released.
6. Connect. Green dot means the socket opened, not that the stop works. Command a small motion only after the hardware stop has been shown to kill motion.
7. Arm torque stays off until `/arm/enable` while `/emergency_stop` is false, if `arm_driver_node` from [`arm_safety.py`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_arm/scripts/arm_safety.py) is the node on the bus. The Yahboom base node zeros serial velocity only while its `/emergency_stop` flag is true, then resumes the stored twist ([`yahboom_controller_node.py`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_driver/scripts/yahboom_controller_node.py)).
8. End with the hardware stop, then disconnect in the headset.

Photos of the e-stop, the serial port, and a tag mount are not in this repo. Do not invent them.
