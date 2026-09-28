## Cursor Cloud specific instructions

This checkout is the OhhO Quest VR client only (Unity project at the repo root). There is no web server, ROS workspace, or `./launch_vr_teleop.sh` here, so nothing needs to be started before editing.

- The Editor is pinned in `ProjectSettings/ProjectVersion.txt` to **6000.5.2f1** (changeset `eb73d3b415a1`). The Unity 2023.3.0f1 line in `README.md` is stale. The Linux editor on this machine is `/opt/unity/6000.5.2f1/Editor/Unity`, also on `PATH` as `unity`. Unity Hub 3.21.3 is installed as `unityhub`. Do not run `unityhub --version` in a headless shell; that process does not exit.
- The Editor will not open the project until a Unity license is activated. Batchmode otherwise stops with `No valid Unity Editor license found` (missing entitlement `com.unity.editor.headless`). Sign in through Unity Hub for a Personal license. A paid seat can be activated with `unity -batchmode -quit -nographics -serial <serial> -username <email> -password <password>`.
- There is no lint config and no CI. Edit-mode tests are the NUnit fixtures in `Assets/Scripts/Editor/Tests/`. After a license is active, import once, then run:
  `unity -batchmode -nographics -projectPath /workspace -runTests -testPlatform editmode -testResults /tmp/unity-editmode.xml -logFile /tmp/unity-editmode.log`
  The first open resolves packages from `Packages/manifest.json` (Unity registry, OpenUPM, the Meta registry, and the NativeWebSocket git URL). Meta XR Core, Interaction, and `com.coplaydev.unity-mcp` are already vendored under `Packages/`.
- This VM has no GPU and no Quest headset, so Play Mode, Meta XR Simulator, and an APK deploy cannot be exercised here. Android Build Support is not installed. The editor install does include the Linux standalone player at `Editor/Data/PlaybackEngines/LinuxStandaloneSupport`.
- `CalibrationManager` (PlayerPrefs) and `HandIK6DofScheme.Configure` (`new GameObject`) call native Editor APIs. Those tests need the Unity Test Runner; a plain `dotnet` host cannot execute them.
