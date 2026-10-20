# Blog draft — launch post

**DRAFT — needs Varun approval before posting.**

Do not publish this page. Suggested slug: `the-ohho-source-is-public`. Suggested meta description: "Public source for a Meta Quest teleop client, the OmniBot ROS 2 Jazzy mobile manipulator, and ohho-os 1.1.2 on PyPI. No release tag and no store listing."

Headline options, if the first one is too plain:

1. The OhhO source is public
2. A Quest client, a ROS 2 mobile manipulator, and a simulator you can pip-install
3. Opening the OhhO repositories

---

# The OhhO source is public

I am Varun Vaidhiya. I build OhhO Robotics in the UK, on my own. The robot on the bench is OmniBot: a Yahboom mecanum base, an SO-101 arm with Feetech STS3215 servos, a Raspberry Pi 5, and a Meta Quest 3. There is no staff, no customer list, and no fleet. This post is the public introduction of the source. It is not a tagged release, and it is not a Quest Store launch.

Three things are actually public today.

The first is a simulator you can run with no robot and no GPU. `ohho-os` 1.1.2 is on PyPI under Apache-2.0. On a machine with Python 3.10 or newer:

```bash
pip install ohho-os
ohho doctor
ohho sim --robot omnibot --seconds 2
```

`ohho doctor` prints the interpreter and which runtimes it can import. `ohho sim` drives an in-process pattern for two seconds. The base install does not open a serial port, a camera, or a GPU. Hardware adapters, including the LeRobot arm extra, are optional packages. I am not quoting a download count.

The second is [OmniBot](https://github.com/ohho-robotics/OmniBot), the ROS 2 Jazzy workspace for that mecanum base and SO-101 arm. The README's laptop path is the Gazebo sim. The same README documents bring-up for the physical robot — base, cameras, arm, and a teleop recorder — and says the docs-test job skips those hardware commands. Nav2 and slam_toolbox are covered as simulation. The README publishes one measured table: 15 local runs in a `ros:jazzy-ros-base-noble` container with Mesa software rendering and 16 CPUs, real-time factor 1.0. They are not a GitHub Actions run, and they were not run on the robot. Each run starts at the spawn pose and has 60 seconds of sim time. A run passes when Nav2 succeeds and the final ground-truth pose is within 0.30 m and 0.50 rad of the waypoint.

| Waypoint | Runs passed | Time to goal, min / median / max (s) |
|---|---|---|
| kitchen | 5 / 5 | 15.7 / 17.0 / 17.5 |
| desk | 5 / 5 | 18.8 / 20.0 / 21.6 |
| door | 5 / 5 | 8.2 / 12.8 / 13.4 |

Those numbers live in the OmniBot README, next to the command that regenerates them. Read them as a sim log from one container, not as a robot benchmark.

The third is [OhhO-VR](https://github.com/ohho-robotics/OhhO-VR), the Meta Quest client. It is a Unity project pinned to **6000.5.2f1**. The primary target in that README is Quest 3 / 3S. First-party code in the repo is Apache-2.0. The Meta XR SDK is vendored with it and stays under the Oculus SDK licence; cloning the repo does not relicense that SDK.

The client is one world-space screen in passthrough: sign in, pick a robot from the same account as the website, then teleop. Stick axes drive the mecanum base. The right hand, inside a 0.40 m workspace, is the IK target for the SO-101. A pinch closes the gripper. The code clamps the base to 0.2 m/s and 1.0 rad/s, and both grips held is the emergency stop. Those match the velocity caps documented on the robot side.

The headset talks ROSBridge WebSocket on port 9090. It expects MJPEG from `web_video_server` on port 8080. Recording on the headset is 30 Hz JSONL, stored on the device, with an upload button that POSTs the file to an HTTP service on port 8765. That service, rosbridge, and the video server are robot-side processes. They are not in the OhhO-VR repository. WebRTC is still roadmap. A visionOS app is described as future work and is not in the tree. There is no GitHub Release on OhhO-VR, and there is no binary attached for sideload. Building a Quest APK means opening the Unity project and building it yourself. The README also says the edit-mode tests are skipped in CI until Unity licence secrets are set, so I am not claiming a green headset test run on GitHub.

The website is a separate layer. [ohho-robotics.com](https://ohho-robotics.com/) can run the three commands above and can open a Pilot cockpit. That cockpit is a prototype with simulated data. Hosted cloud is a waitlist. There is no paid Pilot plan. I am not stating a video latency, because I have not published one. Dual-arm humanoid control, drone schemes, and underwater-vehicle schemes are not built. The picture at the top of the homepage is labelled as a concept render plus a photo from the OmniBot repo. It is not a teleop reel.

On data: the robot repo contains a teleop recorder that tries to write a local LeRobot dataset (six arm joints plus base vx, vy, and yaw rate, plus wrist and bird's-eye images, at 30 Hz) and falls back to a numpy archive if the LeRobot import fails. The default id in that node is a local repo id, not a public Hugging Face dataset. The Quest client's JSONL is a different file and is not a LeRobot dataset. I do not have episode counts or training scores to report. Training stacks in the OmniBot README that need a desktop NVIDIA GPU are labelled that way; they are not something the Pi 5 runs.

If you want to look, start here:

- Simulator: `pip install ohho-os`, then the two commands above. Package: [ohho-os 1.1.2](https://pypi.org/project/ohho-os/1.1.2/). Source: [ohho-sdk](https://github.com/ohho-robotics/ohho-sdk).
- Robot workspace: [OmniBot](https://github.com/ohho-robotics/OmniBot). Ubuntu 24.04 and ROS 2 Jazzy, then the build block in the README. The sim launch line there is `ros2 launch omnibot_bringup sim.launch.py world:=flat`.
- Headset client: [OhhO-VR](https://github.com/ohho-robotics/OhhO-VR). Unity 6000.5.2f1, the only enabled scene `Assets/scene1.unity`, OpenXR, and a robot on the same network that is already running rosbridge.
- The bench, in one paragraph: [About](https://ohho-robotics.com/about). The simulated cockpit: [OhhO Pilot](https://ohho-robotics.com/products/pilot).

I would like review on two specific things. The first is the ROSBridge contract the Quest client uses: `/cmd_vel/teleop`, `/arm/joint_commands`, `/arm/joint_states`, `/arm/enable`, `/odom`, and `/emergency_stop`. The second is whether the robot-side recorder's dataset features still match current LeRobot, given that the import path in the public node is the older `lerobot.common.datasets` layout. I will not ask anyone to download a dataset, because I have not published one.

Questions and build notes: varun@ohho-robotics.com. Code is the source of truth when this post and the README disagree.

---

## Evidence footer (keep with the draft; optional on the live post)

Checked 14 October 2026 against OhhO-VR `main` README and `RobotConfig.cs` / `ProfileDrivenRecorder.cs`, OmniBot `main` README and `teleop_recorder_node.py`, PyPI `ohho-os` 1.1.2, and the live About, Pilot, and homepage. Both GitHub release lists were empty. See [docs/launch/README.md](README.md) for the claim ledger, including the Pilot-docs sentence that does not name a headset.
