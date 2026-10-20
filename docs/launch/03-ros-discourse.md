# ROS Discourse draft

**DRAFT — needs Varun approval before posting.**

Do not submit this to Discourse. Suggested forum: https://discourse.ros.org/ — category General, unless a moderator asks for a projects category. Suggested title:

**OhhO: public Quest teleop client and ROS 2 Jazzy mecanum manipulator (source, no release tag)**

Flair / tags if the composer offers them: `ros2`, `jazzy`. Do not mark it as an announcement of a distro package. These repos are not bloomed into rosdistro here.

---

Hello,

I am Varun Vaidhiya. I have put two public repositories up for a mecanum mobile manipulator I am building in the UK, plus a small Python simulator.

**Robot workspace.** [OmniBot](https://github.com/ohho-robotics/OmniBot) is ROS 2 Jazzy: Yahboom mecanum base, SO-101 arm (Feetech STS3215), Gazebo Harmonic sim, and a Nav2 / slam_toolbox sim write-up. Apache-2.0. The README quickstart that does not need ROS is:

```bash
pip install ohho-os
ohho doctor
ohho sim --robot omnibot --seconds 2
```

`ohho-os` 1.1.2 is on PyPI. That sim is in-process and does not talk to a robot. The ROS workspace build (Ubuntu 24.04, Jazzy, colcon) is the next block in the same README. Sim:

```bash
ros2 launch omnibot_bringup sim.launch.py world:=flat
```

Hardware launch files are documented (`robot_with_joy`, perception, arm, `omnibot_lerobot` teleop record) and the README says docs-test skips them. There is no GitHub Release and no v0.1.0 tag yet. The README still lists that tag as open work.

Nav2 numbers in the README are 15 local Gazebo runs in a `ros:jazzy-ros-base-noble` container (Mesa, 16 CPUs, real-time factor 1.0), not a GitHub Actions run and not the physical robot. All 15 reached the waypoint within 60 s of sim time, with the final pose inside 0.30 m and 0.50 rad. Min / median / max seconds: kitchen 15.7 / 17.0 / 17.5, desk 18.8 / 20.0 / 21.6, door 8.2 / 12.8 / 13.4. I would rather you re-run the script than quote me.

**Headset client.** [OhhO-VR](https://github.com/ohho-robotics/OhhO-VR) is the Quest 3 / 3S Unity project (editor 6000.5.2f1). It is a ROSBridge WebSocket client, default port 9090. It does not contain the robot workspace. You build the APK locally. There is no store listing and no GitHub Release.

Topics the client is written against:

| Direction | Topic | Type |
|---|---|---|
| publish | `/cmd_vel/teleop` | `geometry_msgs/Twist` |
| publish | `/arm/joint_commands` | `sensor_msgs/JointState` |
| publish | `/arm/enable` | `std_msgs/Bool` |
| publish | `/emergency_stop` | `std_msgs/Bool` |
| subscribe | `/arm/joint_states` | `sensor_msgs/JointState` |
| subscribe | `/odom` | `nav_msgs/Odometry` |

Base commands are clamped to 0.2 m/s and 1.0 rad/s. Both grips held is the client e-stop. SO-101 joint names in the client: `arm_shoulder_pan`, `arm_shoulder_lift`, `arm_elbow_flex`, `arm_wrist_flex`, `arm_wrist_roll`, `arm_gripper`.

Video is MJPEG from `web_video_server` on port 8080. WebRTC is not in the Unity manifest. Recording export expects an HTTP service on port 8765 that is also outside this repo. The troubleshooting note in the OhhO-VR README mentions `ROS_DOMAIN_ID=30` when the status dot stays red.

Edit-mode tests live in the Unity repo. The README says CI skips them until Unity licence secrets are set, so I am not pointing at a green GameCI run.

**What I would like from this forum**

A look at that topic list from people who already run rosbridge to a mobile base. In particular: whether `/cmd_vel/teleop` plus a separate `/emergency_stop` is a contract you would actually bind, or whether you would rather the client publish on `/cmd_vel` and let a mux on the robot own the priority. I am one person with one bench robot. I do not have a user count to offer, and I am not asking for a dependency.

Email if Discourse is the wrong place for the thread: varun@ohho-robotics.com.

---

## Notes for Varun (not part of the post)

The Quest client advertises `/arm/enable` and `/emergency_stop` as `std_msgs/Bool` (`ConnectionManager` / `TeleopController` on this branch, which matches `origin/main`). Confirm the robot side still subscribes to those types before posting. The Nav2 table is copied from OmniBot `main` README as fetched on 14 October 2026.
