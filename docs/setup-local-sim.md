# Local sim on Windows (WSL2 + Docker)

This is the path a beta tester uses to drive Gazebo from the Quest. The simulator is not in OhhO-VR. It is the OmniBot checkout named below. The headset only opens a WebSocket and an MJPEG URL.

Evidence for the commands: OmniBot branch `ohh-205-wsl2-network` commit `e617ef6` ([OHH-205](https://linear.app/ohho-robotics/issue/OHH-205/quest-wsl2-container-networking-on-windows-mirrored-networking-or), [network doc](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/docs/vr-sim-network-windows.md)). That branch is in review. These steps were not executed on a Quest while writing this page, and they do not change Windows firewall or WSL settings by themselves. The dry run below is safe to run. The apply commands are not.

Headset behaviour is OhhO-VR commit [`4ebc555`](https://github.com/ohho-robotics/OhhO-VR/commit/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b) unless a step names `ohh-212-session-picker`.

## What "working" means

From a phone on the same LAN as the PC, `http://<pc-lan-ip>:8080` returns the `web_video_server` page. The headset connects to `ws://<pc-lan-ip>:9090`. The front camera the profile asks for is `/camera/front/image_raw` ([`RobotProfileFactory`](../Assets/Scripts/Control/RobotProfileFactory.cs)), requested as `http://<pc-lan-ip>:8080/stream?topic=/camera/front/image_raw` ([`MjpegVideoSource`](../Assets/Scripts/Video/MjpegVideoSource.cs)).

`ohho sim --robot omnibot --seconds 2` in the OmniBot README is a short CPU check of the `ohho-os` package. It is not this Gazebo stack. Do not stop after that command.

## 1. Tools

- Windows 11. Mirrored WSL networking needs build 22621 or newer and WSL 2.0 (`wsl --version`). Portproxy does not.
- A WSL2 distro with Docker. `scripts/windows/vr-sim-network.ps1` defaults `-WslDistro` to `navdev`. If `wsl -l -v` has no `navdev`, pass the distro you actually have (often `Ubuntu`). `navdev` in older notes is sometimes a container name (`docker exec navdev`), which is not a distro.
- Meta Quest 3 / 3S on the same subnet as the PC. Guest Wi-Fi and AP isolation block this even when the internet works. Use 5 GHz, or 6 GHz if that SSID is bridged to the same subnet as the PC. Details and the failure modes are in the [OmniBot network doc](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/docs/vr-sim-network-windows.md).
- The headset app from this repo. Build steps are in [the README](../README.md). Unity **6000.5.2f1**, scene `Assets/scene1.unity`.

## 2. Get the sim repo

In the WSL distro (a Linux filesystem path, not `/mnt/c`, so Docker binds stay fast):

```bash
git clone https://github.com/ohho-robotics/OmniBot.git
cd OmniBot
git checkout ohh-205-wsl2-network
```

If that branch has since merged and the files linked above still match `main`, `main` is fine. If they do not, stay on the commit that contains `scripts/windows/vr-sim-network.ps1` and `sim.launch.py`.

You also need the same checkout visible to Windows, because the network script is PowerShell. Cloning on the Windows side and using that path from WSL (`/mnt/c/...`) works; it is slower.

## 3. Build the image and the workspace

From the OmniBot root, on the Linux side:

```bash
docker compose -f omnibot-digital-twin/docker/docker-compose.yml build
```

The image is `omnibot-digital-twin/docker/Dockerfile.sim` (ROS 2 Jazzy, Gazebo Harmonic, `rosbridge-suite`). Its `apt-get` list does not install `ros-jazzy-web-video-server`. The compose file's `sim` service uses `network_mode: host`, sets `ROS_DOMAIN_ID=30`, and mounts the repo at `/workspaces/OmniBot`. The `ports:` map is ignored while host networking is on. The process bind address is what the Quest can reach.

The image does not run `colcon build`. `install/setup.bash` has to exist on the mount or `ros2 launch` will not find `omnibot_bringup`. Build it once inside the image:

```bash
docker compose -f omnibot-digital-twin/docker/docker-compose.yml run --rm sim bash -lc '
  source /opt/ros/jazzy/setup.bash
  sudo apt-get update
  sudo apt-get install -y ros-jazzy-web-video-server
  rosdep update
  rosdep install --from-paths src --ignore-src -y --rosdistro jazzy
  colcon build --symlink-install --base-paths src
'
```

`rosdep install` can take a long time and can fail on a missing key. If it fails, fix that package and rerun. Do not point the Quest at a container whose launch exited.

NVIDIA GPUs can add `-f omnibot-digital-twin/docker/docker-compose.gpu.yml` on the compose commands. That file was not run for this page.

## 4. Start rosbridge on all interfaces, and start video

`sim.launch.py` starts rosbridge on port 9090 bound to `127.0.0.1` unless you pass `rosbridge_address:=0.0.0.0`. A loopback listener is invisible to the Quest in both mirrored mode and portproxy mode. That launch bridges `/camera/front/image_raw` and does not start `web_video_server` or the arm driver. Wrist and BEV topics the garage profile also lists will show no signal on this launch. Base drive and the front camera are the acceptance check.

`simulation.launch.py` (what `docker compose up` runs) starts `web_video_server` on 8080 and rosbridge on 9090 without setting `address`. Do not assume the omitted address is `0.0.0.0`.

Use the launch that sets the address, then start video in a second shell with the same `ROS_DOMAIN_ID`.

Shell A:

```bash
docker compose -f omnibot-digital-twin/docker/docker-compose.yml run --rm sim bash -lc '
  source /opt/ros/jazzy/setup.bash
  source install/setup.bash
  export ROS_DOMAIN_ID=30
  ros2 launch omnibot_bringup sim.launch.py world:=apartment rosbridge_address:=0.0.0.0
'
```

Shell B, after shell A is up:

```bash
docker compose -f omnibot-digital-twin/docker/docker-compose.yml run --rm sim bash -lc '
  source /opt/ros/jazzy/setup.bash
  source install/setup.bash
  export ROS_DOMAIN_ID=30
  ros2 run web_video_server web_video_server --ros-args -p port:=8080
'
```

`world:=flat` is the other built-in world. Both SDF files are in `omnibot-ros2/omnibot_bringup/worlds/`.

Confirm the binds from a third shell in the same distro. Continue only when 9090 and 8080 are on `0.0.0.0` or `*`, not on `127.0.0.1`:

```bash
ss -ltnp | grep -E ':9090|:8080' || netstat -ltnp | grep -E ':9090|:8080'
```

`rosbridge_address:=0.0.0.0` has no authentication. Keep it on a private LAN. Do not forward 9090 or 8080 to the internet.

The headset does not use DDS. `ROS_DOMAIN_ID=30` only has to match between shell A, shell B, and any other ROS process in that distro. The compose file and the image both export 30. A mismatch makes rosbridge stay up with no topics, and the headset status dot can still turn green.

## 5. Let the Quest reach the Windows LAN address

`network_mode: host` binds inside the Linux network where Docker is running (the WSL distro, or Docker Desktop's utility VM). That address is not the Windows Wi-Fi address the Quest uses. Two ways across that gap are implemented in [`scripts/windows/vr-sim-network.ps1`](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/scripts/windows/vr-sim-network.ps1). This page does not choose one. [OHH-205](https://linear.app/ohho-robotics/issue/OHH-205/quest-wsl2-container-networking-on-windows-mirrored-networking-or) left that as a decision after trying both on the PC. Neither mode was applied while writing this guide.

From the Windows copy of the same OmniBot checkout:

```powershell
powershell -NoProfile -File .\scripts\windows\vr-sim-network.ps1 -WhatIf
powershell -NoProfile -File .\scripts\windows\vr-sim-network.ps1 -Mode Mirrored -WhatIf
powershell -NoProfile -File .\scripts\windows\vr-sim-network.ps1 -Mode PortProxy -DryRun
```

`-WhatIf` and `-DryRun` change nothing and do not need an administrator. Exit code 2 means you did not pass `-Mode`. The script prints a LAN IPv4 (Wi-Fi preferred), `http://<ip>:8080`, `ws://<ip>:9090`, a QR code of the HTTP URL, and a pairing code `OHHO-XXXX`. The pairing code is a CRC-16 of those ports. It is not a login.

| | Mirrored | Portproxy |
|---|---|---|
| When | Windows 11 build 22621+ and current WSL 2 | Any WSL2 NAT install |
| TCP 9090 and 8080 | Reachable on the PC LAN IP if the process listens on `0.0.0.0` | `netsh interface portproxy` forwards to the distro eth0 address |
| After reboot | `.wslconfig` keeps `networkingMode=mirrored`. It applies after `wsl --shutdown` | Proxy entries stay, but the WSL IPv4 usually changes. Run the script again |
| UDP 50000-50100 | Firewall rule only. Nothing in the sim binds this range yet | Portproxy does not forward UDP |

The headset camera path today is MJPEG on TCP 8080. WebRTC signaling is not implemented ([`WebRtcVideoSource`](../Assets/Scripts/Video/WebRtcVideoSource.cs), and `com.unity.webrtc` is not in `Packages/manifest.json`). Either mode can carry 9090 and 8080. Use Mirrored only when you later need UDP. Applying Mirrored with `-RestartWsl` runs `wsl --shutdown` if the config text changes, which stops every distro.

Apply only after the dry run looks right, in an elevated PowerShell, and only for the mode you picked:

```powershell
powershell -NoProfile -File .\scripts\windows\vr-sim-network.ps1 -Mode Mirrored -RestartWsl
powershell -NoProfile -File .\scripts\windows\vr-sim-network.ps1 -Mode PortProxy
```

The rules are inbound Allow, profile Private, remote address LocalSubnet, group `OhhO VR sim`:

| Name | Protocol | Port |
|---|---|---|
| OhhO VR sim rosbridge (TCP 9090) | TCP | 9090 |
| OhhO VR sim video (TCP 8080) | TCP | 8080 |
| OhhO VR sim WebRTC (UDP 50000-50100) | UDP | 50000-50100 |

Set the Wi-Fi or Ethernet adapter that faces the Quest to Private. A Public category ignores these rules. Disconnect a VPN that swallows the LAN route, or the printed IP will be wrong.

## 6. Connect the headset

1. On a phone on the same SSID, open `http://<ip>:8080` from the script output. If the phone fails, the Quest will fail. Fix isolation, the bind address, or the firewall before using the headset.
2. Build from this commit: put on the headset, sign in, open OhhO Pilot, pick the robot, type that IP and port `9090`, press Connect. The status dot turns green when the WebSocket opens ([`TeleopHudController`](../Assets/Scripts/UI/TeleopHudController.cs)).
3. Build from `ohh-212-session-picker`: pick the robot, **Simulate**, leave the prefilled IP (default `192.168.1.101` until you replace it with the script's LAN IP), press **Local desktop**. That is the three-click path in [`SessionPickerPanel`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/UI/Session/SessionPickerPanel.cs). The IP field is the whole point of this page. The default `192.168.1.101` is a robot-LAN guess, not your PC.
4. Cycle the camera with a right-thumbstick press. The front topic should show the Gazebo image. Wrist and BEV stay on "No Signal" on `sim.launch.py`.
5. Drive with the sticks. Read [safety.md](safety.md) first. This sim does not run the Yahboom driver, so `/emergency_stop` does not stop the base. Stop the launch if the robot in Gazebo keeps moving.

## If it fails

| What you see | What the code is doing |
|---|---|
| Phone cannot open port 8080 | Bind is still loopback, firewall profile is Public, VPN, guest SSID, or AP isolation. `sim.launch.py` without `rosbridge_address:=0.0.0.0` never leaves the PC. |
| Port 8080 works, headset stays red | Headset IP or port is wrong. The app wants `ws://<ip>:9090`, not the video port. |
| Green dot, camera says No Signal | `web_video_server` is not running, or it is in another `ROS_DOMAIN_ID`. |
| Green dot, robot never moves | Gazebo still starting, or the sticks are inside the 0.1 dead zone. The drive loop publishes at 20 Hz only while connected. |
| Worked until a sleep or `wsl --shutdown` | Portproxy's WSL address went stale. Run `-Mode PortProxy` again. Mirrored mode needs the shutdown once, then stays. |

More cases (Wi-Fi 6E isolation, stale portproxy, third-party firewalls) are in the [OmniBot network doc](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/docs/vr-sim-network-windows.md). WSL itself is [OHH-114](https://linear.app/ohho-robotics/issue/OHH-114/unitree-dev-environment-on-windows-wsl2-ubuntu-2204-cyclonedds-0102).
