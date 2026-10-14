# Operator rules for OhhO VR

Rules version: 1

These rules are the text behind the in-app acknowledgement (`OperatorRulesAcknowledgement.RulesVersion`). A change to the obligations below is a new rules version: bump the version line here and the constant in `Assets/Scripts/Safety/OperatorRulesAcknowledgement.cs` in the same change. The headset then asks again. Accepting an older version does not cover the new one.

This page is the operator procedure. It is not a claim that the software enforces every line. What the code actually does is in [safety-case.md](safety-case.md). The release gate is [release-signoff-checklist.md](release-signoff-checklist.md), which is unsigned.

## Who may start a REAL robot

Varun must be present for every real robot. A REAL session means the headset can move a physical robot, including wheels or an arm. A simulator does not count as REAL.

Hardware e-stop is mandatory and must be in reach before a REAL session. The headset gesture (both grips) is not that e-stop. On the current client, releasing the grips clears the software stop.

Keep line of sight to the robot and everyone near it. The camera picture can be late. Do not drive from the picture alone.

Speed cap for OmniBot is 0.2 m/s linear and 1.0 rad/s angular; do not raise it in calibration. The current headset can still command more than 0.2 m/s: turbo multiplies the profile cap by 2, and a saved calibration override replaces `MaxLinVel`. Treat any command above 0.2 m/s as a broken session and use the hardware e-stop.

Clear the area of people, animals, and loose objects inside the robot's reach, including the arm's sweep and the base's stopping distance. The software does not detect a pinch and does not read battery or temperature.

Stop the session when the picture freezes, the robot is the wrong machine, the link drops, or anyone else enters the area.

## Acknowledgement

A REAL session must not start until the operator has accepted rules version 1. The call is `RealSessionGate.TryBegin(SessionTarget.Real)`. It returns false until acceptance is stored on the headset. Sim sessions use `SessionTarget.Sim` and are allowed without acceptance. A missing target is `SessionTarget.Unspecified` and is blocked.

`OhhoVrApp.OnRobotPicked` does not yet know sim from real, so the gate is not called from that method. TODO(OHH-245): the caller that can tell the targets apart must call the gate before `TeleopController.StartTeleop` for a real robot.

The in-app summary, which the edit-mode tests require to stay in this file, is:

> Hardware e-stop is mandatory and must be in reach before a REAL session. Keep line of sight to the robot and everyone near it. Speed cap for OmniBot is 0.2 m/s linear and 1.0 rad/s angular; do not raise it in calibration. Clear the area of people, animals, and loose objects inside the robot's reach. Varun must be present for every real robot.

## Sim sessions

A sim session is not blocked by this acknowledgement. Starting a sim session does not accept the rules for a later real robot. The same person still follows the REAL section above when the next session is real.

## What is still a human action

- Confirm the IP is the robot in front of you. The connection panel accepts any IPv4 address.
- Confirm a hardware e-stop cuts power to the motors, on the real robot, before the first drive. That check is OHH-234 and is not done.
- Do not publish a build while [release-signoff-checklist.md](release-signoff-checklist.md) is unsigned.
