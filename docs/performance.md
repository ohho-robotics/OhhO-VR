# Quest performance budget (OHH-231)

Passthrough, the sim video screen, the HUD, the robot twin, and AprilTag detection together are the heavy case on Quest 3 and Quest 3S. This note is the budget, the runtime knobs, and the capture procedure. It does not contain headset measurements. Those cells stay empty until a Quest is attached.

The helper script under `scripts/collect-ovr-metrics.ps1` was not run for this change. There is no headset on this job.

## Budget

Frame time is the slower of the CPU frame and the GPU frame. 72 Hz is the floor (13.8 ms). 90 Hz is the target (11.1 ms). The app asks the display for 90 Hz; 72 Hz is the failure line, not the requested refresh.

| Metric | Budget | Quest 3 | Quest 3S |
|---|---|---|---|
| Frame, worst case | <= 13.8 ms (72 Hz) | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| Frame, target | <= 11.1 ms (90 Hz) | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| App CPU (main thread) | <= 8 ms | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| GPU | <= 9 ms | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| Draw calls | <= 150 | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| Triangles | <= 300k | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| Texture memory | <= 400 MB | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| GC alloc, steady state | 0 bytes / frame | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| Detector thread | <= 20% of one core | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |
| 30-min soak | no thermal throttling | TODO measure on Quest 3 / 3S (Varun) | TODO measure on Quest 3 / 3S (Varun) |

Do not copy a number into this table from the editor, from a previous branch, or from a guess. A development-build log line is a hint, not a measurement.

## What the player sets

`VRGraphicsBoost` on the bootstrap object applies this at startup. The scene values in `Assets/scene1.unity` are the ones that ship:

| Setting | Value | Why |
|---|---|---|
| Eye texture scale | 1.5× | Already on the scene component. Left as-is. |
| MSAA | 4× | Already on the scene component. Left as-is. |
| Refresh request | 90 Hz | Target rate. The system picks the closest supported frequency. |
| CPU level | SustainedHigh | Keeps the clocks up for passthrough + video + HUD. |
| GPU level | SustainedHigh | Same. |
| Fixed foveation | Medium, dynamic off | Peripheral fill drops. The center, where the video and panels are, stays full resolution. |
| Mipmap limit | 0 | Full-resolution textures, unchanged. |

Boost is not used. It holds higher clocks and is the level most likely to trip a thermal warning during the 30-minute soak. SustainedHigh can still be lowered by the OS when the headset is hot; that drop is a soak failure, not a success.

`ffrEnabled` on the old component is ignored while **Apply Fixed Foveation** is on. Turn that off to get the old behavior (legacy checkbox: Low, otherwise Off).

A development build adds `QuestPerfSampler` next to `VRGraphicsBoost`. A release build does not. Uncheck **Run In Development Builds** on the sampler to silence it without removing the component. The sampler reads `FrameTimingManager` (main-thread time, CPU frame time, GPU time) and, when Unity exposes them, the profiler counters `Draw Calls Count`, `Triangles Count`, `GC Allocated In Frame`, and `Texture Memory`. It logs at most once a second, and only when a budget is missed. A zero timing is treated as "not sampled", because the editor and the first device frames report zeros. The detector-thread budget is not sampled in the player; use the per-core columns in the OVR Metrics CSV.

## Capture procedure

Build a **Development Build** of `Assets/scene1.unity` for Quest. Leave Deep Profiling off. Deep Profiling inflates main-thread time and is not a budget measurement. Script Debugging can stay off for the same reason.

Install OVR Metrics Tool on the headset **before** launching the app. Installing it while the app is running force-closes the app. The store build is the one Meta documents: [OVR Metrics Tool](https://developers.meta.com/horizon/documentation/native/android/ts-ovrmetricstool/).

Do not enable the on-device performance HUD overlay for the numbers that fill the table. The overlay costs GPU time and would bias the result. The script below does not turn the overlay on.

### Scenarios

Run each scenario long enough to get a steady minute after the first frames, on both Quest 3 and Quest 3S. Passthrough stays on for all three. Note the headset model, OS build, whether the headset is on a charger, and the room. Charging changes thermals.

1. **Sim MJPEG.** Simulate session, chase camera on the curved screen, wrist picture-in-picture if the profile has a wrist camera. This is the path in `MjpegVideoSource` (off-thread baseline JPEG, main thread uploads RGB24).
2. **WebRTC.** Not measurable in this tree. `WebRtcVideoSource` does not finish signaling, and `com.unity.webrtc` is not in `Packages/manifest.json`. Leave the WebRTC row as TODO until that path actually connects. Do not record an MJPEG session and label it WebRTC.
3. **AR / AprilTag detection on.** The perception panels for that mode are OHH-238 (`DetectionOverlay`, `BevFloorPanel`) and are not in this branch. When they land, repeat the sim-MJPEG capture with detection enabled and read the busiest `cpu_utilization_percentage_coreN` column against the 20% budget.

### OVR Metrics Tool

Meta's documented report-mode commands (package `com.oculus.ovrmonitormetricsservice`):

```text
adb shell am start omms://app
adb shell am broadcast -n com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver -a com.oculus.ovrmonitormetricsservice.ENABLE_CSV
adb shell am broadcast -n com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver -a com.oculus.ovrmonitormetricsservice.DISABLE_CSV
```

CSV files are written under:

```text
/sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics/
```

Columns to read for this budget, using the names Meta lists for the tool:

- `average_frame_rate`, `display_refresh_rate` — refresh and delivered rate
- `app_gpu_time_microseconds` — GPU frame time (divide by 1000 for milliseconds)
- `cpu_level`, `gpu_level`, `foveation_level` — confirm SustainedHigh and Medium actually stuck
- `stale_frame_count`, `early_frame_count` — compositor misses
- `cpu_utilization_percentage`, `cpu_utilization_percentage_core0` … `core7` — detector core
- `gpu_utilization_percentage`
- `battery_temperature_celcius`, `sensor_temperature_celcius`, `power_level_state` — heat and throttling
- `eye_buffer_width`, `eye_buffer_height` — confirms the 1.5× eye buffer
- `app_pss_MB` — process memory, not texture memory

Texture memory, draw calls, triangle count, and per-frame GC are Unity counters. Take those from the Profiler, not from this CSV.

`power_level_state`, `cpu_level`, and `gpu_level` are the throttling signals Meta's tool exposes. A drop in CPU or GPU level during the soak, or a power-state change, fails the soak. Confirm the current meaning of `power_level_state` in Meta's stats guide before calling a file a pass. If a column is absent, leave the cell TODO. Do not invent a zero.

### Unity Profiler

1. Development Build, Autoconnect Profiler on, Deep Profiling off.
2. Install and launch on the headset with the scenario already configured (IP, sim session, cameras).
3. In the Editor, **Window > Analysis > Profiler**, attach to the Quest player.
4. Record about a minute of steady state. Do not include the app launch spike in the summary.
5. Read main-thread time (CPU), GPU time if the GPU module is present, Rendering draw calls and triangles, and the GC Alloc column. Memory module for texture memory against 400 MB.
6. Save the capture next to the CSV. The table still needs a human to type the numbers; a saved capture is not a filled cell until someone does that.

### 30-minute soak

Do this once per headset, after the short captures, with the heavy mix running: passthrough, sim MJPEG on the curved screen, HUD visible, twin in view, and AR detection on if that build has it. If detection is not in the build, write that down and do not claim the detector budget.

1. Headset at room temperature, not already warm from a previous session. Record whether it is charging.
2. Enable CSV capture with the script (below) or the broadcast above. Leave the metrics overlay off.
3. Launch the app, start the scenario, and leave it for 30 minutes. Move your head occasionally the way a teleop session would. Do not let the headset sleep.
4. Stop CSV, pull the files, and read the columns in the thermal list across the whole file, not just the first minute.
5. Fail the soak if the CPU or GPU level drops, `power_level_state` changes, stale frames climb and stay high, or the frame time goes over 13.8 ms and stays there. Pass/fail is Varun's call from the CSV. This document does not record a result.

## Helper script

`scripts/collect-ovr-metrics.ps1` wraps the broadcasts and the `adb pull`. It does not launch the player, does not enable the overlay, and does not parse the CSV into a pass/fail.

From the repo root, with one Quest in `adb devices` and USB debugging allowed:

```powershell
# Before the session. Enables CSV recording and selects the budget stats.
powershell -ExecutionPolicy Bypass -File .\scripts\collect-ovr-metrics.ps1 -Action Enable

# Run the scenario on the headset yourself. The script does not wait unless -Seconds is set.

# After the session. Pulls into the logs folder and then stops recording.
powershell -ExecutionPolicy Bypass -File .\scripts\collect-ovr-metrics.ps1 -Action Pull -OutDir C:\Users\varun\ohho\logs\ohh-231-metrics
powershell -ExecutionPolicy Bypass -File .\scripts\collect-ovr-metrics.ps1 -Action Stop
```

A 30-minute wait inside the script is optional and off by default:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\collect-ovr-metrics.ps1 -Action Enable -Seconds 1800 -OutDir C:\Users\varun\ohho\logs\ohh-231-metrics
```

That sleeps, pulls, and stops. You still have to start the scenario on the headset yourself before the sleep matters. If more than one device is connected, pass `-Serial`.

## Code changes and expected impact

Nothing in this section was timed on a Quest. Impact is from reading the code.

### MJPEG decode and upload

`MjpegFrameParser` used to allocate a header string and a `Content-Length` substring on every chunk. It now matches those bytes in place. `MjpegVideoSource` used to allocate a JPEG copy and a thread-pool closure every accepted frame. The JPEG bytes now go into one reused slot, and the worker is a cached callback. The slot stays busy until the worker finishes, including across `Stop`, so the next stream cannot overwrite it mid-decode. `BaselineJpegDecoder` reuses quant tables, Huffman tables, component objects, plane buffers, and the RGB buffer. `GrowthCount` stays flat on a second decode of the same size; the edit-mode test covers that. The upload buffer is still reallocated when the frame dimensions change, because `LoadRawTextureData(byte[])` requires the array length to match the texture. After the first frame of a given size, that path should not allocate the JPEG, the decoder tables, or the RGB scratch.

`CameraFeedViewer` (the older panel) was doing the string parse plus `ImageConversion.LoadImage` on a new `byte[]` every frame, and `DownloadHandlerBuffer.data` copies the whole download on every poll. It now uses `MjpegVideoSource`. Streams request the same quality 80 / width 960 cap as the sim screen. The camera-name label is set when the stream starts, and only if the text changed.

Expected impact: the steady-state MJPEG path should drop its managed allocations to the occasional dimension change and to whatever Unity does inside `LoadRawTextureData` / `Texture2D.Apply`. That last part is not visible from C# and is not claimed to be zero. The first frame of a stream still allocates.

### TMP rebuilds outside the HUD

The sim-screen stats label was rewritten on every presented frame and on a 10 Hz timer. `SimScreenPresenter` now keeps the last string and publishes a change at most every 0.2 s (`OverlayTextGate`). New labels are created with rich text off, so a publish does not parse tags. `StreamOverlay.Format` returns the previous string when the fps and age integers have not changed.

Expected impact: the stats mesh rebuilds at about 5 Hz instead of on every video frame, and identical overlay text does not allocate a new string. The age readout lags by up to 200 ms. `CameraFeedController` still builds the overlay string on every presented frame; that file is owned by other jobs (see TODOs).

### Glass shader overdraw

`Shaders/OhhoGlass.shader` included a URP header and tagged the pass `RenderPipeline=UniversalPipeline`. This project is Built-in (`m_CustomRenderPipeline` is unset, and the URP package is not in `Packages/manifest.json`), so that pass could not compile here. The pass now uses `UnityCG.cginc`. It draws one face (`Cull Back`), depth-tests (`ZTest LEqual`), and discards fragments outside the rounded rect so the transparent corners do not blend. The interior is still a translucent blend over passthrough. That cost is the glass look.

Expected impact: back faces are not rasterized, and the four corners of each glass panel no longer touch the framebuffer. The center of each panel still overdraws passthrough. Not measured.

## TODOs in files this job did not edit

Other jobs own these. The offenders are listed so the soak does not ignore them.

- `Assets/Scripts/UI/TeleopHudController.cs` (OHH-222 and OHH-219). `Update` calls `RefreshTelemetryUi` and `RefreshRecordingUi` at 10 Hz. Those assign `TMP_Text.text` with new interpolated strings, and the arm line allocates a `StringBuilder`, even when the numbers have not changed.
- `Assets/Scripts/UI/TelemetryPanel.cs` (HUD). `RefreshAllUI` at 10 Hz assigns every odometry, IMU, joint, and status label with a new string and no equality check.
- `Assets/Scripts/UI/RecordingPanel.cs` (HUD). `Update` calls `UpdateLiveStats` every frame. While a recorder exists, `elapsedTimeLabel` and `frameCountLabel` are assigned every frame. During a recording the tenth-of-a-second clock changes every frame, so TMP rebuilds at frame rate.
- `Assets/Scripts/Video/CameraFeedController.cs` (OHH-219, OHH-222, and OHH-238). `OnMainFrame` and the 0.1 s timer call `StreamOverlay.Format` and `SetStats`. The presenter now drops duplicate publishes and throttles the mesh, but the call still happens on every presented frame.
- OHH-238 perception files are not in this branch. That job is adding `Assets/Scripts/Video/DetectionOverlay.cs` and `Assets/Scripts/Video/BevFloorPanel.cs`. Re-check both for per-frame text and per-frame overlay allocations before measuring the detector budget.
- OHH-219 operate-layout files are not in this branch. That job is adding `Assets/Scripts/UI/Operate/`. Re-check those panels for per-frame TMP before measuring.

## Human TODOs

- Varun: run the three capture scenarios and the 30-minute soak on a Quest 3 and a Quest 3S. Fill the table from the CSV and the Profiler capture. Do not mark a cell done from this document.
- Varun: WebRTC stays TODO until signaling exists.
- Varun: detector-core percentage stays TODO until the OHH-238 detection path is in the build under test.
- If `Texture Memory` or `Draw Calls Count` does not show up as a profiler counter on device, say so in the table notes instead of substituting another counter.
