# OhhO VR privacy

This note is the data inventory for the Quest client in this repository. It was written by reading the C# under `Assets/` (not the vendored Meta packages, except where a manifest flag is applied at build time). It describes what this code sends and stores. A lawyer has not reviewed it.

TODO: Varun should confirm this notice before it is copied into a Quest store data-safety form. It was not checked on a headset, against a live Supabase project, or against Meta's own device telemetry.

## Account email (Supabase)

`SupabaseAuthService` (`Assets/Scripts/Core/Platform/SupabaseAuthService.cs`) signs in with the same Supabase project as the website. The project URL and the public anon key come from the VR manifest (`VrAuthConfig` in `Assets/Scripts/Core/Platform/PlatformManifest.cs`).

`OhhoPlatform` (`Assets/Scripts/Core/Platform/OhhoPlatform.cs`) GETs `{platformBaseUrl}/vr/manifest.json`. The default base URL is `https://ohho-robotics.com`. If that fetch fails, it reads `Assets/Resources/vr_manifest.json`. That bundled file points auth at `https://ucnyuappgtcpolqdaovv.supabase.co`, sign-in method `email_otp`, garage table `user_robots`. The anon key in that file is not repeated here.

What is posted:

| Call | Body | Where |
|---|---|---|
| `POST {url}/auth/v1/otp` | email, `create_user: true` | Supabase Auth |
| `POST {url}/auth/v1/verify` | type `email`, email, one-time code | Supabase Auth |
| `POST {url}/auth/v1/token?grant_type=refresh_token` | refresh token | Supabase Auth |

Each request sends the anon key as the `apikey` header and as the bearer until a user access token exists.

On success the client keeps the access token, refresh token, user id, and email in memory. The refresh token is the only auth secret written to disk, under PlayerPrefs key `ohho.supabase.refresh_token`. `SignOut` deletes that key. The access token and email are not written to PlayerPrefs.

## Garage robot list

`GarageClient` (`Assets/Scripts/Core/Platform/GarageClient.cs`) calls PostgREST on the same project, with the user access token:

- `GET {url}/rest/v1/user_robots?select=*&order=created_at.desc`
- `POST {url}/rest/v1/user_robots` with `user_id`, `name`, `robot_type_id`, `hardware_model_id`, and a status string
- `DELETE {url}/rest/v1/user_robots?id=eq.{id}`

The row type is `UserRobot` (`Assets/Scripts/Core/Platform/GarageModels.cs`): `id`, `user_id`, `name`, `robot_type_id`, `hardware_model_id`, `status`, `config`, `created_at`, `updated_at`. The headset does not add a separate profile, photo, or location column.

`OhhoCatalog` (`Assets/Scripts/Core/Platform/OhhoCatalog.cs`) GETs `{site}/vr/catalog.json` (fallback `Resources/vr_catalog`) so those ids can be shown. That request has no user body.

## Session records

This client does not write a session-log table to Supabase or to ohho-robotics.com.

What "session" means in this code:

- The Supabase auth session above (access token in memory, refresh token in PlayerPrefs).
- `LastRobotStore` (`Assets/Scripts/Control/LastRobotStore.cs`) saves the last robot id, display name, IP, and ROSBridge port in PlayerPrefs (`ohho.last_robot_*`, `ohho.has_last_robot`). `OhhoVrApp` writes that when a robot is picked. It is not uploaded.
- `ConnectionManager` (`Assets/Scripts/Core/ConnectionManager.cs`) saves `robot_ip`, `robot_port`, and `auto_connect` in PlayerPrefs.
- `CalibrationManager` (`Assets/Scripts/Control/CalibrationManager.cs`) saves per-robot JSON in PlayerPrefs under `ohho.calibration.{robotId}` (arm height offset, workspace scale, velocity overrides, IK location).
- `OnboardingController` saves `ohho.onboarding_seen`.
- `ComfortSettingsStore` (`Assets/Scripts/Comfort/ComfortSettings.cs`) saves screen distance, screen size, text scale, and left-handed sticks under `ohho.comfort.settings`.
- Robot detection may create a local `OVRSpatialAnchor` (`Assets/Scripts/Detection/SessionOriginAnchor.cs`, Android player only). `sharedAnchorSupport` in `Assets/Oculus/OculusProjectConfig.asset` is 0. This code does not call a share or upload API for that anchor.

## Telemetry

Robot telemetry is the ROSBridge stream to the robot address the user types (default port 9090). It is not forwarded to Supabase or to ohho-robotics.com.

`ConnectionManager` subscribes to odometry, IMU, joint states, mission status, AI status, and the active control-mode topics, and publishes drive commands, control mode, arm commands, arm enable, emergency stop, mission command, AI command, and record start/stop. Topic names are the constants in `RobotConfig` plus, for the profile-driven path, the topics on `RobotProfile`.

`TelemetryPanel` and `TeleopHudController` display those messages. They do not post them.

Recordings (`DatasetRecorder` and `ProfileDrivenRecorder`, both under `Assets/Scripts/Recording/`) write JSON lines to `Application.persistentDataPath/recordings/{episode}.jsonl`. A line has time, robot observations (arm position, base velocity, odometry), and the commands that were sent. There is no image field.

Export is optional and user-started. `RecordingPanel` and `TeleopHudController` POST the jsonl file to `http://{robotIp}:8765/upload_episode` on the robot the user connected. Default `bridgeHttpPort` is 8765. That upload does not go to OhhO.

## Logs

`Debug.Log`, `Debug.LogWarning`, and `Debug.LogError` in `Assets/Scripts` stay in the Unity player log on the device. There is no Sentry, Firebase, or other log-shipping client under `Assets/Scripts`.

`ProjectSettings/ProjectSettings.asset` defines `SENTIS_ANALYTICS_ENABLED` for the Android player. No script under `Assets/` references Sentis or that symbol. This note does not claim what the Unity Inference package does with the define, because this app does not call it.

The Android manifest (`Assets/Plugins/Android/AndroidManifest.xml`) contains `<meta-data android:name="com.oculus.telemetry.project_guid" ... />`. `OVRManifestPreprocessor` writes that value from the Meta XR project settings. Application code does not read the guid or POST camera frames, email, or logs to it. What Quest OS sends to Meta under that id is outside this repository.

## Headset camera frames

Verified in this checkout: passthrough camera frames do not leave the device.

What was searched, under `Assets/` (application code, not `Packages/`):

- `PassthroughFrame`, `GetColors`, `WebCamTexture`, `EncodeToPNG`, `EncodeToJPG`, `SaveImage`
- `UploadHandler` and `UnityWebRequest` call sites

What the code does:

- `PassthroughFrame` (`Assets/Ohho/AR/AprilTagSpike/AprilTagContracts.cs`) holds width, height, pinhole intrinsics, camera pose, and a timestamp. It has no pixel buffer. The comment on that struct says pixel bytes stay in the camera API until a native detector exists. No native detector is in this repo.
- `HeadsetPassthroughCapture` (`Assets/Scripts/Detection/RobotDetectionService.cs`) reports unavailable. `TryGetLatest` always returns false. `com.meta.xr.mrutilitykit` is not installed, so `PassthroughCameraAccess.GetColors()` is not called. `isPassthroughCameraAccessEnabled` in `Assets/Oculus/OculusProjectConfig.asset` is 0, and the checked-in manifest does not declare `horizonos.permission.HEADSET_CAMERA`. The Meta preprocessor adds that permission only when the flag is on (`Packages/com.meta.xr.sdk.core/Editor/OVRManifestPreprocessor.cs`).
- `FakePassthroughFrameSource` is an edit-mode stand-in with no pixels.
- No `EncodeToPNG`, `EncodeToJPG`, `WebCamTexture`, or `GetColors` call exists under `Assets/`.
- The only `UploadHandlerRaw` uses are the Supabase JSON bodies in `SupabaseAuthService` and `GarageClient`.
- The only binary POST is the episode jsonl export to the robot, described above.

The robot camera is a different path. `MjpegVideoSource` and `CameraFeedViewer` GET JPEG frames from the robot (`http://{robotIp}:8080/stream?topic=...` in the MJPEG source; the viewer uses the same pattern). `WebRtcVideoSource` is a receiver and its signaling is not wired. Those frames are the robot's cameras, downloaded to the headset for display. This code does not upload them.

If camera permission is denied, `RobotDetectionService` turns detection off, drops any held frame, refuses new frames, and does not subscribe to odometry. The user can set a manual pose (`SetManualPlacement`). The status string is `HeadsetCameraGate.DeniedReason`.

## Other permissions

`HeadsetCameraGate.Notices` lists the permissions this app explains:

- `horizonos.permission.HEADSET_CAMERA` — requested at runtime on an Android player by `HeadsetCameraGate.RequestAccess`, only when it is not already granted. The rationale and the denial message are in `HeadsetCameraGate` and in `Assets/Plugins/Android/res/values/strings.xml`.
- `com.oculus.permission.HAND_TRACKING` — declared in `AndroidManifest.xml`. Hand tracking support in the Oculus project config is on. App code does not call `OVRPermissionsRequester` for it. The rationale string says hand poses stay on the headset and that the base still drives from the controllers.

`OVRManager` can also request body, face, eye, scene, microphone, and passthrough-camera permission on startup. Those serialized flags are not set in `Assets/scene1.unity` (the fields are absent, so they stay at the script default of false). `microphoneUsageDescription` in `ProjectSettings/ProjectSettings.asset` is empty. Insight passthrough (the see-through background, `PassthroughManager`) is not the RGB headset camera.

## Comfort

The app does not move the user. `Assets/Scripts` has no locomotion provider, teleport interactor, or character controller. `WorldSpaceUiPlacer` and `DraggableScreen` move the floating panel. Drive schemes publish a robot velocity; they do not translate the headset rig.

`ComfortSettings` stores, on the headset only:

- screen distance (0.5 m to 3 m, default 1.2 m, matching `WorldSpaceUiPlacer`)
- screen size (0.5 to 2, default 1)
- text scale (0.75 to 2, default 1)
- left-handed stick and grip mapping

`ForcesUserLocomotion` is a constant false and is not saved. Left-handed mapping swaps thumbstick and grip roles and copies both-grips emergency-stop edges unchanged. It does not swap which hand drives which arm.

## Retention

Auth records stay in Supabase while the account exists. This notice does not set a fixed retention period. Email the address on the website privacy page to ask for deletion.

On the headset, the refresh token is removed on sign-out. The last robot, calibration, comfort settings, onboarding flag, connection address, and recording files stay in app data until that data is cleared.
