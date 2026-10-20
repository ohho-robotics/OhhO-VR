# Show HN draft

**DRAFT — needs Varun approval before posting.**

Do not submit this to Hacker News. Submitter: Varun's own HN account, after he approves the text. URL field: `https://github.com/ohho-robotics/OmniBot` (the repo a cold reader can build from). The text also links the Quest client and PyPI. Do not put the marketing homepage in the URL field. Do not put a concept render in the URL field.

## Title

```
Show HN: OhhO – Quest teleop client, ROS 2 mecanum arm, pip-installable sim
```

75 characters. HN truncates titles near 80. Fallback if you want a shorter one (71 characters):

```
Show HN: OhhO source – Quest teleop, ROS 2 mecanum arm, and a local sim
```

## Body

I am one person in the UK, building a mecanum mobile manipulator called OmniBot and the software around it. I am posting the source. There is no release tag, no Quest Store app, and no hosted service you can point a robot at.

The part you can try with no robot and no GPU:

```
pip install ohho-os
ohho doctor
ohho sim --robot omnibot --seconds 2
```

`ohho-os` 1.1.2 is on PyPI, Apache-2.0: https://pypi.org/project/ohho-os/1.1.2/

The ROS 2 Jazzy workspace is https://github.com/ohho-robotics/OmniBot — Yahboom mecanum base, SO-101 arm, Gazebo sim, Nav2 in sim. Hardware launch files are in the README and are not what CI runs. The README has a 15-run Gazebo Nav2 table from a local container. That table is not a physical-robot result, and I would like it treated as a sim log.

The Quest 3 / 3S client is a separate Unity 6 repo: https://github.com/ohho-robotics/OhhO-VR — ROSBridge on port 9090, stick drive, hand IK for the arm, both-grips e-stop, MJPEG video. WebRTC is not built. You produce the APK yourself. First-party code is Apache-2.0. The vendored Meta XR SDK keeps the Oculus SDK licence.

The website cockpit is a simulated demo: https://ohho-robotics.com/products/pilot

I have no users, customers, or partners to list. The about page is the whole company description: https://ohho-robotics.com/about

Comments I can answer: the rosbridge topic choices, why the Quest recorder writes JSONL instead of a LeRobot dataset (the robot-side node is the one that tries LeRobot, and I have not published a dataset), and what is honestly missing for a tagged v0.1.0.

Email: varun@ohho-robotics.com

---

## Notes for Varun (not part of the post)

Post on a weekday morning US time if you want comments, and then stay on the thread. HN will ask "how is this different from X" — the honest answer is: public Jazzy workspace plus a Quest client you compile, early, one robot. Do not reply with a latency number, a dataset link, or a store link. If someone asks for a video, say you do not have a public teleop video yet.

Do not edit the title after submit. Do not ask friends to upvote.
