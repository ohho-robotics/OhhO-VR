# Beta session notes

Running notes for one Quest session. This file is a scratch log for the person in the session.
`tools/beta_triage.py` does not read it. The scored report is `feedback-form.md`.

Session id:
Tester id:
Date:
APK file name:
Headset (Quest 3 or Quest 3S):
Sim: local Gazebo / host Varun sent / not started
Address typed into the IP field:
Port typed into the port field:

## Timeline

Write clock times. Skip a row if it did not happen.

| Step | Time | What you saw |
|---|---|---|
| APK installed (`adb devices` showed the headset) | | |
| App launched (library name from the build; this checkout's product name is `vr_app`) | | |
| Login status text | | |
| Console ("Your VR consoles") | | |
| Garage ("Your garage") | | |
| Robot card selected | | |
| Connect pressed | | |
| Status text became Connected | | |
| `[FirstDrive] ready` in logcat | | |
| `[FirstDrive] since_connect_s=` in logcat | | |
| Session ended | | |

## Log lines

Paste any `adb logcat` lines that contain `FirstDrive`, `ConnectionManager`, or `ROSBridgeClient`.

```
```

## What differed from docs/beta/README.md

-
-

## Follow-ups for Varun

-
