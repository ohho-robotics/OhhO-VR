# Quest manual checklist

OHH-230. Use one copy of this checklist per release candidate. RC1 is the first candidate that must be completed on a Quest before that build is called tested. CI play-mode tests do not substitute for this page.

Headset client only. The robot stack is not in this repository. Unity **6000.5.2f1**. Scene `Assets/scene1.unity`.

## How to reuse this for a later RC

Copy the RC1 tables. Write the new id in the sign-off (RC2, RC3, …). Keep every safety step. Add rows for behaviour that changed in that candidate. A failed safety step fails the candidate. Do not mark a later RC passed because RC1 passed on an older build.

## Abort rules

Read this section before anyone enables teleop.

The software e-stop is a **deadman**. Both grip buttons together publish `/emergency_stop` true and stop new velocity commands. Releasing either grip publishes `/emergency_stop` false, and the base is allowed to move again. The onboarding line is "Both grips: emergency stop · Release to clear".

Before the first motion:

- You can reach the robot's **hardware** e-stop without crossing the robot's path.
- The floor around the robot is clear. People stand outside its reach, including the arm.
- OmniBot Pro's headset cap is **0.2 m/s** and **1.0 rad/s**. The first motion is a small stick deflection, not a full throw.

If the base is still moving **while both grips are held**:

1. Keep both grips held.
2. Press the robot's hardware e-stop.
3. Only then release the grips and power down the base drivers.

Releasing the grips clears the software stop. It is not the way to recover from a stop that failed.

If the status indicator is red, the headset is not connected. Treat the robot as still live until you have confirmed it is still. A dropped socket cannot deliver `/emergency_stop`.

Left-controller **B** hides the OhhO screen. It is not an e-stop.

## RC1 preconditions

| # | Check | Pass |
|---|---|---|
| P1 | Build is Unity 6000.5.2f1, scene `Assets/scene1.unity`, installed on a Quest 3 or Quest 3S | |
| P2 | Commit recorded in the sign-off block matches the build on the headset | |
| P3 | Robot ROSBridge is on port **9090**. You know its IP. Default the client offers is `192.168.1.101` | |
| P4 | Hardware e-stop location has been shown to everyone in the room | |
| P5 | Tester account is a normal OhhO account. The garage already contains at least one real robot (OmniBot Pro for the motion steps) | |
| P6 | Camera host (MJPEG, port **8080**) and recording export host (port **8765**) are either up, or those rows are marked N/A with a reason | |

Sign-in for RC1 is the email code. Do not use the Google button for the recorded path. That button skips straight to the garage.

## RC1 steps

Fill Pass with P, F, or N/A. A failed safety row (M4, M5, M7, L1, L5) fails RC1.

### Sign-in, console, garage, picker

| # | Action | Expected | Pass | Notes |
|---|---|---|---|---|
| A1 | Launch the app in passthrough | The OhhO screen is in front of you, login page visible | | |
| A2 | Send a code to the RC account email and enter it | Console appears. Login is gone | | |
| A3 | Open **OhhO Pilot** | Garage lists the account's robots. The same robots exist on the website | | |
| A4 | If the garage is empty, add a robot on the website, return, refresh | The new robot appears without retyping its catalog data | | |
| A5 | Open Add Robot. Walk category → type → model → name. Cancel | You land back on the garage. No extra robot was created | | |
| A6 | Pick the OmniBot Pro (or the agreed RC robot) | Teleop page opens. Status shows the robot name | | |
| A7 | If a first-run hint appears | It includes the drive hint and "Both grips: emergency stop · Release to clear". Dismiss with the button or left B | | |

### Connection

| # | Action | Expected | Pass | Notes |
|---|---|---|---|---|
| C1 | Enter the robot IP and port 9090. Connect | Status dot turns green. Wrong IP stays red and the base does not move | | |
| C2 | Disconnect, or stop rosbridge on the robot | Status returns to red. Sticks and both grips do not produce motion | | |
| C3 | Connect again | Green. Later motion steps start from a fresh connection | | |

### Motion and e-stop

Do these only after C3 is green and the abort rules are in place. Use a small deflection first.

| # | Action | Expected | Pass | Notes |
|---|---|---|---|---|
| M1 | Left stick, slight forward, then release | Base creeps forward and stops after release. Speed stays obviously below a fast walk. Cap is 0.2 m/s | | |
| M2 | Left stick, slight strafe | Base strafes and stops on release | | |
| M3 | Right stick X, slight yaw | Base yaws and stops on release | | |
| M4 | While moving slowly, **hold both grips** | Base stops. It stays stopped for as long as both grips are held, even if the sticks are still deflected | | |
| M5 | Release both grips with sticks centered | Stop clears. The base does not lurch. A new small stick command moves it again | | |
| M6 | Hold right grip alone (turbo) and give a full forward stick for a short moment, then release | Motion is still within the 0.2 m/s cap. It is not a sudden doubling past the cap | | |
| M7 | If an on-screen emergency-stop control is visible: press and hold it during a slow move, then release with sticks centered | Hold stops the base. Release clears the stop. If the control is absent, mark N/A | | |
| M8 | Leave teleop with the back-to-garage control | Base is still. Returning to the garage does not leave the robot driving | | |

### Link loss, camera, recording, screen

| # | Action | Expected | Pass | Notes |
|---|---|---|---|---|
| L1 | During a connection, kill rosbridge or walk out of range until the dot is red | Confirm the robot has stopped. If it has not, hardware e-stop | | |
| L2 | Open the camera view (right stick press cycles feeds) | MJPEG image, or a clear no-signal state. N/A if port 8080 is down | | |
| L3 | Start recording, drive a short safe motion, both-grips stop, then stop recording | E-stop still stops the base while a recording is active. The episode file is written or discard works. N/A if recording is not in this RC build | | |
| L4 | Left controller B | Screen hides. Teleop and e-stop still behave. B again shows the screen | | |
| L5 | Hands in view | Hand meshes track. Arm motion, if enabled, stops when M4 is repeated. If the arm does not stop on both grips, fail RC1 and use the hardware e-stop | | |

### Sign-out

| # | Action | Expected | Pass | Notes |
|---|---|---|---|---|
| S1 | End teleop, then sign out | Login page is back. The garage is not usable signed-out | | |
| S2 | Relaunch | A saved session may skip login and land on the console. The robot is not already moving | | |

## RC1 sign-off

| Field | Value |
|---|---|
| Candidate | RC1 |
| Date | |
| Tester | |
| Headset (Quest 3 or 3S) | |
| App commit | |
| Robot name and IP | |
| Result | Pass / Fail |
| Safety notes (empty if none) | |

Fail RC1 if any of M4, M5, M7, L1, or L5 is Fail. Attach this page to the release notes for the candidate.

## Later candidates

| Candidate | Date | Commit | Tester | Result | What changed since the previous RC |
|---|---|---|---|---|---|
| RC2 | | | | | |
| RC3 | | | | | |
