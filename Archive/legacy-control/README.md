# Quarantined legacy control scripts (OHH-192)

These scripts are not in the player or editor build. Unity does not import `Archive/`. Each file is also wrapped in `OHHO_INCLUDE_LEGACY`, which is not a scripting define, so copying a file back under `Assets/` does not compile it. The classes are marked `[Obsolete]`.

The live publishers of `/cmd_vel/teleop`, `/emergency_stop`, `/arm/joint_commands`, and `/arm/enable` are `TeleopController` plus `Control/Drive/*` and `Control/Manip/*`.

## GUID check

`scene1.unity` (the only enabled build scene) and the project prefabs do not reference these scripts. `m_Script` GUIDs:

| Script | GUID | scene1 / prefabs | Other scenes |
| --- | --- | --- | --- |
| `BaseController.cs` | `cc1ed069165bb41f580cac765ea53ae5` | none | none |
| `HandTrackingArmController.cs` | `642623da5ce974079aeafafef8f3ab82` | none | `Archive/scenes/omnibotVR.unity`, `Archive/scenes/OmniBotVR_Main.unity` |
| `HUDManager.cs` | `6591b2d86d04f48479d3479fd59591e6` | none | none |
| `TelemetryPanel.cs` | `0e969d6366f634b6a9cc7f77f9ac5b93` | none | none |
| `DatasetRecorder.cs` | `67dd3049fe0704dc190225c8fafba8f5` | none | none |
| `RecordingPanel.cs` | `79f1ba93e73174a278fd8e14de1f48e8` | none | none. Calls `AddComponent<DatasetRecorder>()`, so it is archived with the recorder. |
| `ControlPanel.cs` | `e05bba5106b0e47e7b431c61def7cff0` | none | none. Published `/emergency_stop` and `/arm/cmd_mode`. |

Live scripts that scene1 does reference:

| Script | GUID | Referenced by |
| --- | --- | --- |
| `TeleopController.cs` | `cb1af96ccfa84451e852606215aa9b88` | `Assets/scene1.unity`, `Archive/scenes/Recovery/0.unity`, `Archive/scenes/OmniBotVR_Main.unity` |
| `ConnectionManager.cs` | `f883e35edbf094fd1aaf1f1e505c8212` | `Assets/scene1.unity`, `Archive/scenes/Recovery/0.unity`, `Archive/scenes/OmniBotVR_Main.unity` |

`ArmIKSolver.cs` (`7cf9c973c58024d1fa6dd3acca5b5b8a`) stays in `Assets/Scripts/Input/`. It is not a MonoBehaviour and does not publish. `HandIK6DofScheme` still constructs it.

`SceneBuilderWizard`, `MRSceneBuilder`, and `OhhoUnifiedAppBuilder` attach `TeleopController`, `GestureDetector`, and `ProfileDrivenRecorder`. They do not add these archived components.
