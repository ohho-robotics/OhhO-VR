# AR mode (OHH-235)

Display only. The twin and the robot HUD read the detected base pose and the
profile's joint-state and odometry topics. They do not advertise, publish, or
unsubscribe. Unsubscribe would drop the recorder and the teleop HUD from those
shared topics.

Leave `Assets/scene1.unity` alone. After the detection service and the robot
link exist, from the caller's `Awake`:

```csharp
var ar = gameObject.AddComponent<OmniBot.VR.AR.ArModeController>();
ar.Bind(detectionService, link, profile);
```

`Start` will find a `RobotDetectionService` and, when teleop already has a
profile, subscribe through `RosBridgeLink` if `Bind` was not called.

## What tracks

- Locked AprilTag pose (`RobotDetectionSnapshot.HasPose`) moves the twin root
  in headset space at 1:1 scale.
- The HUD is parented to that root (0.55 m up, 0.12 m forward) and yaws toward
  the headset. It shows track confidence, tag inliers, planar speed, yaw rate,
  and live joint angles.
- A positive `arm_shoulder_lift` raises the kinematic arm. Lengths come from
  the profile when set, otherwise the SO-101 lengths in `RobotConfig`.

## Manual placement

When the snapshot is not locked (searching, lost, camera permission denied, or
the editor), `PlaceInFrontOf(headPose)` or `PlaceManually(worldPose)` shows
the twin at that pose. A later lock hides the manual pose until tracking drops
again. `ClearManualPlacement()` returns to a hidden twin.

Assign `SetExternalTwin` if a URDF body should ride the same pose. The
procedural chassis and arm stay either way.

## Human TODO

On a Quest with tags in view, confirm the twin sits on the robot within 3 cm
and the HUD stays on the base while the robot drives. Confirm manual placement
with the headset camera permission denied. Passthrough camera capture is still
the OHH-224 follow-up (`PassthroughCameraAccess` is not in this project), so
tag hits in tests come from a fake detector.
