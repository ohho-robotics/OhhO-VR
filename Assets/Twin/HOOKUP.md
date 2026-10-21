# OmniBot digital twin (OHH-211)

Display only. The twin subscribes to odometry, tf, joint states, and joint
commands. It never advertises, publishes, or unsubscribes (unsubscribe would
drop the recorder and HUD from those shared topics).

## Scene hookup

Leave `Assets/scene1.unity` alone. Either drop
`Assets/Twin/Prefabs/OmniBotTwin.prefab` under the teleop root, or after the
robot link exists:

```csharp
gameObject.AddComponent<OmniBot.VR.Twin.TwinView>().Configure(link, OmniBot.VR.Twin.TwinProfiles.OmniBot());
```

Put that object where the desk robot should sit. The prefab defaults to
tabletop scale (0.25). For the 1:1 AR overlay call
`SetScale(OmniBot.VR.Twin.TwinScaleMode.OneToOne)`.

## What it mirrors

- Base pose from `/odom` or `/tf` (`odom` → `base_footprint`), converted from
  ROS FLU into Unity.
- Measured joints from `/arm/joint_states` and wheel joints from `/joint_states`.
- Ghost arm from `/arm/joint_commands` (the teleop target on this branch).
- Names are the URDF names. The SO-101 sim joints are the OHH-199 set:
  `arm_shoulder_pan`, `arm_shoulder_lift`, `arm_elbow_flex`, `arm_wrist_flex`,
  `arm_wrist_roll`, `arm_gripper`.

If a stack emits a different command topic, set `TwinTopics.JointCommands` on
the profile. Do not add another literal in the animator.

## Descriptor hook (OHH-225)

`TwinProfile.UrdfPath` and `TwinProfile.JointMap` are the RobotDescriptor
inputs. `TwinProfiles.ForRobot(profile, urdfPath)` copies that robot's odom,
joint-state, and arm-command topics. `UrdfPath` may be a project path or an
http(s) URL. Topic strings for this client live only in `TwinProfiles.cs`.

## Regenerate the URDF and meshes

From the repo root, with the OmniBot checkout available (or `OMNIBOT_XACRO` /
`OMNIBOT_SO101_MESHES` set):

```text
python Assets/Twin/Tools/expand_omnibot_urdf.py
python Assets/Twin/Tools/decimate_so101_meshes.py
```

Default xacro args are `drive=mecanum`, `lidar=false`, `minimal_sensors=false`.
The expanded file is `Assets/Twin/URDF/omnibot.urdf.xml` (plain URDF; the `.xml`
suffix is what Unity imports as text).

## Follow-ups

- Scene hookup in `scene1` (the one line above). Do not edit the scene in this change.
- Point `UrdfPath` and `JointMap` at the RobotDescriptor when OHH-225 lands.
- AR overlay (VR-8): parent the twin to the tracked desk anchor and call `SetScale(OneToOne)`.
- OHH-207 can pass a `SimulatedRobotLink` into `Configure` when that link exists.

## Human TODO

Record a side-by-side video against Gazebo. The twin should match the sim base
pose within 2 cm and the joint angles within 2 degrees.
