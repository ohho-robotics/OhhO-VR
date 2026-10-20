# OHH-251 — public announcement drafts

**DRAFT — needs Varun approval before posting.**

Do not publish, email, submit, or cross-post any file in this folder. Do not merge this branch in order to "put the drafts on main." These pages are copy for a human to approve. They were not posted to a blog, LinkedIn, X, ROS Discourse, the LeRobot community, or Hacker News.

Evidence date: 14 October 2026. If a link below has changed, rewrite the claim before posting.

## Files

| Draft | File | Use |
|---|---|---|
| Launch post (blog-length) | [01-blog-launch-post.md](01-blog-launch-post.md) | Site blog, after approval |
| LinkedIn / X thread outline | [02-linkedin-x-thread.md](02-linkedin-x-thread.md) | Outline plus draft posts |
| ROS Discourse | [03-ros-discourse.md](03-ros-discourse.md) | Short technical post |
| LeRobot community | [04-lerobot-community.md](04-lerobot-community.md) | Short technical post |
| Show HN | [05-show-hn.md](05-show-hn.md) | Title plus body |

## What this drop can say

A reader can check each of these without trusting the draft:

- [OhhO-VR](https://github.com/ohho-robotics/OhhO-VR) is a public Unity project. The README pins Unity **6000.5.2f1**, targets **Meta Quest 3 / 3S**, and describes a headset client for an OmniBot-style mecanum base and SO-101 arm over ROSBridge (WebSocket, default port **9090**). First-party code is Apache-2.0. The vendored Meta XR SDK stays under the Oculus SDK licence. The [releases page](https://github.com/ohho-robotics/OhhO-VR/releases) has no releases. There is no Quest Store package in the repo.
- The same README says the camera path is **MJPEG** from `web_video_server` on port **8080**. WebRTC is roadmap: `com.unity.webrtc` is not in `Packages/manifest.json`. A visionOS client is described as future work. The robot-side ROS workspace, rosbridge process, and the HTTP bridge on port **8765** are not in this repository.
- The headset recorder in `Assets/Scripts/Recording/ProfileDrivenRecorder.cs` writes **30 Hz JSONL** on the device (`t`, `robot`, `obs.arm_pos`, `obs.base_vel`, `obs.odom`, `action.arm_cmd`, `action.base_cmd`). That file is not a LeRobot dataset.
- Client limits in `Assets/Scripts/Core/RobotConfig.cs`: linear **0.2 m/s**, angular **1.0 rad/s**, command publish **20 Hz**. The README documents both grips held as the emergency stop.
- The README says the repo contains edit-mode tests (it counts 39) and that CI skips the Unity steps until repository licence secrets are set. These drafts do not claim a green Unity test run.
- [OmniBot](https://github.com/ohho-robotics/OmniBot) on `main` is the public ROS 2 Jazzy reference repo (mecanum base, SO-101, Gazebo, Nav2/SLAM in simulation, LeRobot capture code). Its [releases page](https://github.com/ohho-robotics/OmniBot/releases) has no releases. The README roadmap still lists a first tag, v0.1.0, as open. Physical-robot launch commands are documented there and skipped by the docs-test job.
- That README's Nav2 section is simulation only. It reports 15 local runs in a `ros:jazzy-ros-base-noble` container (Mesa, 16 CPUs), not a GitHub Actions run and not the physical robot. All 15 reached the waypoint inside 60 s. Min / median / max seconds: kitchen 15.7 / 17.0 / 17.5, desk 18.8 / 20.0 / 21.6, door 8.2 / 12.8 / 13.4. Pass condition in that write-up: Nav2 success and final `/odom` pose within 0.30 m and 0.50 rad.
- [`ohho-os` 1.1.2](https://pypi.org/project/ohho-os/1.1.2/) is on PyPI (uploaded 2026-10-01, Apache-2.0). The documented no-hardware commands, also on the [homepage](https://ohho-robotics.com/) and the OmniBot README, are `pip install ohho-os`, `ohho doctor`, and `ohho sim --robot omnibot --seconds 2`. The packaged project says the base install does not open a serial port, camera, or GPU. Download counts are not cited here.
- [About](https://ohho-robotics.com/about): OhhO Robotics is Varun Vaidhiya, building in the UK. The bench robot named there is a Yahboom mecanum base, an SO-101 with Feetech STS3215 servos, a Raspberry Pi 5, and a Meta Quest 3. The page says the engine can talk to that base, run a simulator with no hardware attached, and record arm and base motion for later training. It also says there is no customer list, no staff, and no fleet. Email on that page: varun.vaidhiya@gmail.com.
- [Pilot](https://ohho-robotics.com/products/pilot) and its [docs](https://ohho-robotics.com/docs/products/pilot): prototype console. The on-site cockpit is an interactive demo with simulated data. The docs say there is no paid plan, hosted cloud is a waitlist, no video-latency figure is stated, and dual-arm humanoid, drone, and underwater-vehicle schemes are not built.
- The homepage says the hero image area includes a concept render that is not footage of OmniBot, and a photo that comes from the public OmniBot repository. Connect, Data, Train, and Serve consoles use simulated data. Web Bluetooth on the site is a demo whose source is not public.

## What these drafts leave out

Partners, customers, revenue, users, stars, waitlist size, latency milliseconds, episode counts, task success rates, and "tested on Quest" as a benchmark. No Quest Store link, no APK URL, no demo-video link, no Hub dataset, no claim that Unity CI is green, no claim that humanoids, quadrupeds, drones, or marine vehicles run. No claim that the Quest JSONL is a LeRobot dataset.

The robot-side recorder on OmniBot `main` (`omnibot_lerobot/teleop_recorder_node.py`) imports `lerobot.common.datasets.lerobot_dataset.LeRobotDataset` and, if that import fails, writes a numpy archive. The default `repo_id` in that file is `local/mobile_manipulation`. The feature map names the third base component `base_vz`; the value assigned from `/odom` is `angular.z` (yaw rate). Do not describe that field as vertical velocity. Do not claim the import has been run against a current LeRobot release.

## Reconcile before posting

The Pilot docs page says OpenXR is the intended interface and that a specific headset is not claimed. The OhhO-VR README, the GitHub description, and the about page name Meta Quest 3 / 3S. These drafts follow the README and the about page. If the docs-page wording should win, edit the drafts first.

## Posting

Varun copies approved text out of these files and posts it himself. This branch does not submit anything.
