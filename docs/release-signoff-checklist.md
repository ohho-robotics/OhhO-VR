# Release sign-off checklist

Linear: [OHH-245](https://linear.app/ohho-robotics/issue/OHH-245)

This checklist gates a public release of a VR build that can drive a real robot. The safety case is [safety-case.md](safety-case.md). The operator procedure is [operator-rules.md](operator-rules.md) (rules version 1).

**Status: UNSIGNED.** No row below is accepted. Do not tag, publish, or ship a Quest build from this branch.

## Build the signer is looking at

| Item | Value at review (2026-10-14) |
|---|---|
| Headset repo | `ohho-robotics/OhhO-VR` |
| Headset `origin/main` | `4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b` |
| This review branch | `ohh-245-safety-case` |
| Robot `origin/main` | `ohho-robotics/OmniBot` `d296c9be6cc898e11e8f671c2457292b30cec905` |
| ohho-os `origin/main` | `85db848268168fa27b4ebb1b5992220b3bfacfcd` (SafetyGate, not on the VR path) |
| Unity | 6000.5.2f1, changeset `eb73d3b415a1` |
| Operator rules | version 1 |

The signer writes the full SHAs they actually ran, including the `ohh-245-safety-case` commit. This table is the review baseline, not a signature.

## Documents

- [x] `docs/safety-case.md` written, with each control pointing at a file and a test name or an explicit gap
- [x] `docs/operator-rules.md` rules version 1 (hardware e-stop, line of sight, speed cap, area clearance, Varun present)
- [x] In-app acknowledgement code and edit-mode tests added (`RealSessionGate`, `OperatorRulesAcknowledgementTests`)
- [ ] Varun has read the safety case and the operator rules

The checked boxes are "the file exists on this branch". They are not acceptance of residual risk.

## FMEA — each row closed or explicitly accepted

Closed means: merged on the commits that run, headset publishing the topic the control reads, and an OHH-234 record. Accepted means: Varun writes the residual he is willing to ship. Blank means open.

| Row | Hazard | Residual on the merged stack | Closed or accepted |
|---|---|---|---|
| H1 | Unexpected base motion | High | |
| H2 | Runaway on link loss | High | |
| H3 | Wrong robot | High | |
| H4 | Stale video | High | |
| H5 | Arm pinch | High once the arm moves from VR | |
| H6 | Policy takeover | Medium | |
| H7 | Unauthorised remote access | High | |
| H8 | Battery / thermal | High (no inhibit found) | |
| H9 | REAL session with no rules acceptance | High (gate not wired) | |

## Draft controls that are not merged

Leave unchecked until the PR is on `main` and the headset uses it.

- [ ] OmniBot #57 `teleop_safety` (OHH-194) merged, and the headset publishes `/cmd_vel/teleop_raw` and `/arm/joint_commands/teleop_raw`
- [ ] OmniBot #46 mux staleness (OHH-195) merged
- [ ] OmniBot #47 gateway (OHH-196) merged, and the headset uses `wss` plus the token
- [ ] OmniBot #53 arm teleop topic (OHH-197) merged
- [ ] OhhO-VR #26 heartbeat (OHH-202) merged
- [ ] OhhO-VR #25 latency budgets (OHH-240) have a real measured table, not `not_measured`
- [ ] OHH-193 / OhhO-VR #6 decided (currently skipped; not on headset `origin/main`)
- [ ] OmniBot #64 real VR profile (OHH-220) and #65 sim contract (OHH-203) merged if this release depends on them

## Hardware (OHH-234)

Not done. This review has no bag, no video, and no robot log.

- [ ] Hardware e-stop cuts motor power, demonstrated on the real OmniBot, with Varun present
- [ ] Link unplug stops the base within the budget the merged control claims
- [ ] Latched e-stop stays stopped until the documented reset
- [ ] Speed on the real base stays inside the signed cap
- [ ] Arm does not close on a hand; the torque-off behaviour matches `arm_safety` on the real servo
- [ ] Video used for driving is either fresh enough to sign, or the operator rule is "line of sight only" and that is the accepted residual
- [ ] Battery and temperature either trip motion or are an accepted gap with the sensor named
- [ ] The session was the robot named on the headset, not a second machine on the LAN

## In-app acknowledgement

- [ ] `RealSessionGate.TryBegin(SessionTarget.Real)` is called from the real-session start path
- [ ] A sim start still calls `TryBegin(SessionTarget.Sim)` and is not blocked
- [ ] A rules-version bump asks again on a headset that already accepted version 1
- [ ] Headset pass recorded (not done here; no Quest was used)

## External reviewer

TODO (human): nobody is assigned.

| Name | Date | Scope | Comments |
|---|---|---|---|
| | | | |

## Sign-off

| Role | Name | Date | Decision |
|---|---|---|---|
| Release owner | Varun | | **UNSIGNED** |

Signature line (leave blank):

```
Signed: ________________________________
Date:   ________________________________
Decision: UNSIGNED — do not release
Commit: ________________________________
```

A signature here means every FMEA row is either closed or has a written acceptance in the table above, OHH-234 is done, and the version row matches the build that was tested. Until those lines are filled, this checklist does not authorise a public release.
