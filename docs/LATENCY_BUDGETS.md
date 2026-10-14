# Latency budgets

p95 ceilings for the Quest teleop client. These are the targets the harness scores against. This repository does not contain a hardware measurement. The measured columns below stay empty until a person records a session.

The numbers in code are `LatencyBudget` (`Assets/Scripts/Latency/LatencyBudget.cs`). `docs/latency-budgets.json` is the same table. A unit test fails if they diverge.

## Profiles

| Profile | Path |
|---|---|
| Local | Headset and robot on the same LAN. Direct ROSBridge (`ws://robot:9090`). No cloud relay. |
| Cloud | Commands, telemetry, and video cross a cloud relay. |
| Real | The physical robot: actuators, camera, and radio. Not a simulator. |

## What each segment measures

Both ends of input→command and command→motion use the headset clock. Telemetry age, heartbeat age, and glass-to-glass compare a producer stamp with the headset clock, so those stamps have to be Unix epoch milliseconds (ROS `secs`/`nsecs` or `sec`/`nanosec`). A negative age fails the budget. It means the clocks disagree.

| Segment | Start | End |
|---|---|---|
| `input_to_command` | `TeleopController` (or the legacy `BaseController`) reads the sticks | The command JSON is handed to the socket. Cloud and real ignore that local handoff and wait for a stamped `/latency/command_ack`. |
| `command_to_motion` | That same handoff | The next `/odom` or joint-state message that shows motion |
| `glass_to_glass` | `OHHO_CAP:<unix_ms>` on the JPEG | The frame is assigned to the camera `RawImage` |
| `telemetry_age` | `header.stamp` on odom, joint state, or IMU | The headset receives the message |
| `heartbeat_age` | `header.stamp` on `/diagnostics` or any topic whose name contains `heartbeat` | The headset receives the message |

`input_to_command` is the read-to-publish interval for the sample inside that command. The 20 Hz publish period is the spacing between commands, not extra latency on the sample that was sent. A stall between the stick read and `Publish` is what this segment catches.

Motion is a twist component or a joint velocity above `0.005`, or a joint-position change above `0.01` rad from the previous joint state. A stationary odom whose pose is away from the origin is not motion. Covariance is ignored. The first joint state after subscribe only sets a baseline.

The headset does not publish `/latency/command_ack`. While capture is on it may subscribe, so a robot or relay can echo accept time. Until that echo exists, cloud and real input→command stay `not_measured`. That is not a pass.

Glass-to-glass without `OHHO_CAP:` is `incomplete`, not a pass. `scripts/jpeg_capture_stamp.py` builds a JPEG COM segment (marker `FF FE`) carrying the marker. The reader only searches for the ASCII text.

MJPEG in this client is documented at about 200–500 ms. The local glass budget is 120 ms, which matches the WebRTC note in `MjpegVideoSource` (sub-100 ms plus compose). An MJPEG frame is expected to miss that budget. WebRTC signaling in this repo is still a roadmap stub, so a headset session today records MJPEG.

Heartbeat age uses the same ceilings as telemetry age. Recording it does not add a deadman, does not change the e-stop latch, and does not publish a keepalive.

## Budgets (p95, milliseconds)

| Profile | Segment | Budget p95 (ms) | Measured p50 | Measured p95 | n | Verdict |
|---|---|---:|---:|---:|---:|---|
| Local | `input_to_command` | 25 | | | | not_measured |
| Local | `command_to_motion` | 100 | | | | not_measured |
| Local | `glass_to_glass` | 120 | | | | not_measured |
| Local | `telemetry_age` | 50 | | | | not_measured |
| Local | `heartbeat_age` | 50 | | | | not_measured |
| Cloud | `input_to_command` | 80 | | | | not_measured |
| Cloud | `command_to_motion` | 250 | | | | not_measured |
| Cloud | `glass_to_glass` | 300 | | | | not_measured |
| Cloud | `telemetry_age` | 200 | | | | not_measured |
| Cloud | `heartbeat_age` | 200 | | | | not_measured |
| Real | `input_to_command` | 40 | | | | not_measured |
| Real | `command_to_motion` | 200 | | | | not_measured |
| Real | `glass_to_glass` | 180 | | | | not_measured |
| Real | `telemetry_age` | 80 | | | | not_measured |
| Real | `heartbeat_age` | 80 | | | | not_measured |

Why these ceilings:

- Local input, 25 ms. One 90 Hz frame is about 11 ms. 25 ms allows a hitch and still finishes inside the 50 ms command period.
- Cloud input, 80 ms. The local read-to-publish path plus relay accept.
- Real input, 40 ms. On-device frame time plus the hop to the robot AP, still inside one command period.
- Local motion, 100 ms. Two 20 Hz control cycles.
- Cloud motion, 250 ms. The relay plus that local actuation budget.
- Real motion, 200 ms. Motor and mechanism delay on the physical robot, still under a quarter of a second.
- Local glass, 120 ms. WebRTC under 100 ms plus display compose. MJPEG is expected to miss it.
- Cloud glass, 300 ms. Encode, relay, and decode.
- Real glass, 180 ms. On-robot capture and the LAN, between the lab WebRTC target and the cloud relay.
- Local telemetry and heartbeat, 50 ms. One 20 Hz sample.
- Cloud telemetry and heartbeat, 200 ms. Relay delay.
- Real telemetry and heartbeat, 80 ms. The sensor driver plus the robot network.

A command that is still waiting for motion, or a segment with any incomplete sample, is `incomplete`. Missing data is `not_measured`. Neither one is `pass`. `pass` requires a complete sample set whose p95 is within the ceiling and whose ages are not negative.

`docs/latency-results.template.json` is the same empty table in JSON. Copy it when you fill a hardware run. Do not copy a log whose `hardware_run` field is false into those measured cells.

## Harness

`LatencySession` stores the samples. `LatencyLog` writes `latency-<profile>.csv` and `latency-<profile>.json`. Capture is off unless a session is started, so teleop is unchanged in normal play.

CSV columns: `t_unix_ms,kind,segment,latency_ms,topic,ack,transport,profile`.

JSON fields: `profile`, `clock`, `hardware_run`, `truncated`, `budgets_p95_ms`, `samples`, `summary` (with `n`, `incomplete`, `p50_ms`, `p95_ms`, `budget_p95_ms`, `verdict`).

To record on a machine that can reach a robot, set the environment before launching the player. This does not connect by itself.

```
OHHO_LATENCY_LOG=<directory>
OHHO_LATENCY_PROFILE=local
OHHO_LATENCY_HARDWARE=1
```

`OHHO_LATENCY_PROFILE` is `local`, `cloud`, or `real`. Set `OHHO_LATENCY_HARDWARE=1` only for a real recording. The process writes the files when the teleop object shuts down.

Summarize a log with:

```
python scripts/summarize_latency_log.py latency-local.json
```

The script prints a banner when `hardware_run` is false.

Observation is attached after `ROSBridgeClient` has already handed the payload to the socket, and on the way in before subscriber callbacks. A failure in the harness is logged and does not drop the publish. Emergency-stop payloads are not rewritten and do not start a motion measurement. The fake link used by tests (`FakeRobotLink`) keeps the published JSON byte-for-byte and never opens a socket.

## Tests

No Quest and no robot:

```
dotnet test tests/LatencyHarness.Tests/LatencyHarness.Tests.csproj
python -m unittest discover -s scripts -p "test_*.py"
```

The same NUnit fixtures live under `Assets/Scripts/Editor/Tests/LatencyHarnessTests.cs` and run with the edit-mode filter `OmniBot.VR.Tests` when a Unity licence is available. CI runs the `dotnet` and Python commands in `.github/workflows/latency-harness.yml`.

## Still to measure on hardware

The table above is the results template. A person still has to:

1. Sync the robot clock to Unix time. ROS sim time will not produce a meaningful telemetry age.
2. Record local, cloud, and real with `OHHO_LATENCY_HARDWARE=1`.
3. Publish `/latency/command_ack` (`std_msgs/Header`, Unix stamp) from the relay or the robot if cloud or real input→command should leave `not_measured`.
4. Stamp camera frames with `OHHO_CAP:<unix_ms>` if glass-to-glass should leave `incomplete`.
5. Paste p50, p95, n, and the verdict into the table. Leave a cell blank rather than inventing a number.

Command→motion closes on the first telemetry that looks like motion. If the base is already moving when an arm command is sent, the interval can be one telemetry period rather than the arm's own response.
