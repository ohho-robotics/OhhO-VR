# LinkedIn / X thread outline

**DRAFT — needs Varun approval before posting.**

Do not post this thread. Account for X, already linked from the OmniBot README: [@varunvaidhiya](https://x.com/varunvaidhiya). No LinkedIn URL is assumed here. Post as Varun, first person. There is no staff account to tag.

## Job of the thread

Tell a technical reader that the source is public, how to run the simulator in one minute, and what is still missing (no release tag, no store build, no hosted product, no dataset). Seven posts. Same order on LinkedIn and on X. Stop at seven. Do not add a metrics post, a "we're hiring" post, or a partner logo.

## Images

Do not attach the homepage concept render. The homepage says that render is not footage of OmniBot. A photo is only fair if it is the photo already in the public OmniBot repo, with a caption that says so. Do not caption any still as a VR teleop demo. There is no demo video to upload.

## Draft posts

Character counts are the post text only, not the URL. X still fits under 280 with the URL on its own line. LinkedIn can use the same text; the blog link belongs on post 7, not on every post.

### 1 — What this is

Intent: introduce the source drop in one breath. No adjective pile.

> I'm opening the OhhO source. I'm one person, with one bench robot, in the UK. There is no release tag and no Quest Store build.
>
> What you can run today is a simulator, a ROS 2 repo, and a Unity client you build yourself.

### 2 — The only zero-hardware command

Intent: give a copy-paste that matches the homepage and PyPI. Do not add flags that are not in that quickstart.

> No robot and no GPU:
>
> pip install ohho-os
> ohho doctor
> ohho sim --robot omnibot --seconds 2
>
> ohho-os 1.1.2 is on PyPI (Apache-2.0). The sim is in-process. It does not open a serial port, a camera, or a GPU.
>
> https://pypi.org/project/ohho-os/1.1.2/

### 3 — The robot repo

Intent: name the hardware that is actually documented. Leave humanoids, drones, and fleets out.

> OmniBot is the reference robot in the public ROS 2 Jazzy repo: a Yahboom mecanum base and an SO-101 arm (Feetech STS3215), with a Raspberry Pi 5 on the bench.
>
> Sim launch is in the README. The hardware launch commands are documented and skipped by the docs test. There is no GitHub Release yet.
>
> https://github.com/ohho-robotics/OmniBot

### 4 — The Quest client

Intent: point at OhhO-VR with the limits that stop a false "download the app" reading.

> The Quest client is a separate repo. Unity 6000.5.2f1, aimed at Quest 3 / 3S. It speaks ROSBridge on port 9090. Sticks drive the base. The right hand is the arm target. Both grips held is the e-stop. Base commands are clamped to 0.2 m/s.
>
> Camera in the client is MJPEG. WebRTC is still roadmap. You build the APK yourself. First-party code is Apache-2.0. The vendored Meta XR SDK keeps its own licence.
>
> https://github.com/ohho-robotics/OhhO-VR

### 5 — What the website is

Intent: stop people treating the marketing site as a live robot console.

> The site cockpit is a prototype with simulated data. Hosted cloud is a waitlist. There is no paid plan.
>
> I am not quoting a video latency, a user count, or a customer. Dual-arm humanoids, drones, and underwater vehicles are not built.
>
> https://ohho-robotics.com/products/pilot

### 6 — Data, said narrowly

Intent: invite LeRobot people without inventing a dataset. Link nothing that 404s.

> The robot repo has a teleop recorder that tries to write a local LeRobot dataset and otherwise writes numpy. The Quest app writes its own 30 Hz JSONL, which is not a LeRobot dataset.
>
> I have not published a dataset, and I do not have training scores to share.

### 7 — Ask, then stop

Intent: one concrete review ask and a human email. Link the blog only after the blog is actually up. Until then, use the three repo links already posted and do not invent a blog URL.

> If you read ROS 2 or LeRobot code for a living, the review I want is the rosbridge topic list and whether that recorder's schema still matches current LeRobot.
>
> varun.vaidhiya@gmail.com
>
> Longer write-up, once it is published: the OhhO blog post "The OhhO source is public".

## LinkedIn-only alternative

If a thread is awkward on LinkedIn, publish posts 1 through 4 as one article-style post and stop. Paste post 2's commands as a code block. Attach nothing. The first comment can hold the three URLs from posts 2, 3, and 4. Skip posts 5 and 6 only if the combined post already says "simulated cockpit, no dataset, no store build."

## Do not add

- Star counts, waitlist size, "thousands of developers," or a funding line.
- A tag of Hugging Face, Meta, ROS, LeRobot, or a robot vendor as if they were involved.
- A poll.
- A claim that Quest teleop has a measured latency.
- The word "launch" in the sense of a product that customers can buy or download. "Source is public" is the line.
