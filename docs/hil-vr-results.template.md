# OHH-234 timing template

Copy this file to `evidence/ohh-234/<YYYYMMDD>/timing.md` and fill the copy after the session. Leave this file as it is. Every measured cell stays empty, and every verdict stays `not_measured`, until a person has timed a real run from the MCAP stamps. A dry-run log is not a measurement.

Budgets:

| Budget cell | Meaning |
|---|---|
| `same tick` | The zero command is published on the control frame where the operator released the deadman, clutch, or hit e-stop. A later zero from the 300 ms watchdog fails this row. |
| `350 ms` | Last `/ohho/heartbeat` to the first zero `/cmd_vel/teleop`. Source: OHH-194 watchdog `0.300` s plus one `0.010` s gate tick. OHH-202 names that ceiling `StopBudgetSec`. |
| `200 ms` | Design ceiling for real-profile `command_to_motion` p95 from OHH-240. It is a ceiling to score, not a number this repository measured. |
| `0` | Any motion fails. Write the duration of unexpected motion in the copy. An empty cell means the run has not happened. |

Clocks: stamp the safety rows from the bag (message time on `/cmd_vel/teleop` and `/ohho/heartbeat`). Stamp the OHH-240 rows with the headset latency log for profile `real`, and only from a log whose `hardware_run` field is true.

## Safety stops

| Stage | Event | Budget | Measured (ms) | n | Verdict |
|---|---|---|---|---|---|
| blocks | uncommanded wheel motion after bring-up | 0 | | | not_measured |
| deadman | left-grip release to zero `/cmd_vel/teleop` | same tick | | | not_measured |
| estop | A+B or HUD e-stop to zero `/cmd_vel/teleop` | same tick | | | not_measured |
| estop | motion after the button is released, before an explicit reset | 0 | | | not_measured |
| link-loss | last `/ohho/heartbeat` to zero `/cmd_vel/teleop` | 350 ms | | | not_measured |
| link-loss | motion after Wi-Fi returns, before deadman release and re-press | 0 | | | not_measured |
| hmd-remove | last `/ohho/heartbeat` to zero `/cmd_vel/teleop` | 350 ms | | | not_measured |
| hmd-remove | motion after the headset is back on, before deadman release and re-press | 0 | | | not_measured |
| floor-drive | deadman release to wheels stopped | same tick | | | not_measured |
| floor-drive | stick to wheel motion, real profile p95 | 200 ms | | | not_measured |
| arm | joint motion on enable, before the clutch is held | 0 | | | not_measured |
| arm | clutch release to a held joint command | same tick | | | not_measured |

## Real-profile latency (OHH-240 design ceilings)

Score these only from a hardware latency log. The ceilings are the OHH-240 real profile: input 40 ms, motion 200 ms, glass 180 ms, telemetry 80 ms, heartbeat age 80 ms. This repository does not ship that harness on this branch.

| Profile | Segment | Budget p95 (ms) | Measured p50 (ms) | Measured p95 (ms) | n | Verdict |
|---|---|---|---|---|---|---|
| Real | `input_to_command` | 40 | | | | not_measured |
| Real | `command_to_motion` | 200 | | | | not_measured |
| Real | `glass_to_glass` | 180 | | | | not_measured |
| Real | `telemetry_age` | 80 | | | | not_measured |
| Real | `heartbeat_age` | 80 | | | | not_measured |
