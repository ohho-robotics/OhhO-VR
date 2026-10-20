# LeRobot community draft

**DRAFT — needs Varun approval before posting.**

Do not post this to Discord, the Hugging Face forum, or GitHub Discussions. Destination, when you approve it: LeRobot GitHub Discussions ([huggingface/lerobot](https://github.com/huggingface/lerobot/discussions)) or the community Discord. Pick one. Do not cross-post the same day.

Suggested title:

**Local SO-101 + mecanum recorder uses LeRobotDataset.create; no Hub dataset, and the import path looks old**

---

Hello,

I am Varun Vaidhiya. I maintain OmniBot, a small ROS 2 Jazzy mobile manipulator (Yahboom mecanum base, SO-101 / Feetech STS3215, Raspberry Pi 5 on the bench). The public repo is https://github.com/ohho-robotics/OmniBot. Apache-2.0. There is no release tag yet.

I am posting because the teleop recorder tries to speak LeRobot and I would like a correction before anyone depends on it.

The node is `omnibot_lerobot/teleop_recorder_node.py` on `main`. It records at 30 Hz while an Xbox RB button is held (LB discards). Each frame is a 9-vector state and a 9-vector action:

- six SO-101 joints: `arm_shoulder_pan`, `arm_shoulder_lift`, `arm_elbow_flex`, `arm_wrist_flex`, `arm_wrist_roll`, `arm_gripper`
- base `vx`, `vy`, and yaw rate

Wrist image and a bird's-eye image are stored as video features at 240×320. State comes from `/arm/joint_states` and `/odom`. Arm action comes from `/arm/leader_states`. Base action comes from `/joy`. The default `repo_id` parameter is `local/mobile_manipulation`. Nothing in that node pushes to the Hub. I have not published a dataset, and I do not have episode counts.

When `from lerobot.common.datasets.lerobot_dataset import LeRobotDataset` succeeds, the node calls `LeRobotDataset.create(...)`, `add_frame`, and `save_episode`. When the import fails, it writes a compressed numpy archive instead and logs that. I have not shown that this import succeeds on the current LeRobot release. `lerobot.common` looks like the older layout. If the supported path is different now, I would rather change the node than document a dead import.

One naming bug I should not paper over: the feature names call the third base component `base_vz`. The value written from `/odom` is `twist.twist.angular.z`, and the joystick axis is scaled by rad/s, not m/s. Please read that column as yaw rate until I rename it.

A separate Quest app, https://github.com/ohho-robotics/OhhO-VR, records its own 30 Hz JSONL on the headset (`obs.arm_pos`, `obs.base_vel`, `obs.odom`, `action.arm_cmd`, `action.base_cmd`). That file is not a LeRobot dataset. Please do not point anyone at it as one. The optional `arm` extra of the PyPI package `ohho-os` depends on `lerobot`; the base `pip install ohho-os` does not install it, and the two-second sim command does not record a dataset.

What I would find useful:

1. Whether `LeRobotDataset.create` with those four features is still a valid local dataset on the release you actually install.
2. Whether a mobile base should be `vx, vy, wz` inside `action`, or a separate action key, in datasets you train on today.
3. A pointer to a small public mobile-manip dataset whose schema is a better target than what this node invented, if you know one.

I am not announcing a trained policy. GPU training notes in the OmniBot tree are desktop-NVIDIA instructions, and I have no success rate to report. Email is fine if a public thread is too noisy: varun@ohho-robotics.com.

---

## Notes for Varun (not part of the post)

This draft describes OmniBot `main` as fetched on 14 October 2026. Re-read `teleop_recorder_node.py` on the day you post. If the import path or the `base_vz` name has been fixed, delete the matching paragraph. Do not attach screenshots of empty datasets. Do not claim SmolVLA, ACT, or diffusion checkpoints have been fine-tuned.
