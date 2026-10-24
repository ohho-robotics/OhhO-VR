using System;
using Newtonsoft.Json;
using OmniBot.VR.Control;
using UnityEngine;

namespace OmniBot.VR.Comfort
{
    /// <summary>
    /// Headset comfort choices. The app never moves the user: these values
    /// resize and place the floating screen, scale text, and swap stick roles.
    /// They are stored on the device in PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// TODO(OHH-219): Assets/Scripts/UI/Operate/OperateComfort.cs and
    /// Assets/Scripts/UI/Operate/OperateDashboard.cs should subscribe to
    /// <see cref="ComfortSettingsStore.Changed"/> for screen distance, screen
    /// size, and text scale. This branch does not edit those operate-layout files.
    /// TODO(OHH-222): Assets/Scripts/UI/TeleopHudController.cs,
    /// Assets/Scripts/UI/HUDManager.cs, Assets/Scripts/UI/SmoothHUD.cs, and
    /// Assets/Scripts/Telemetry/TelemetryHudComposer.cs should apply
    /// <see cref="ComfortSettings.TextScale"/>. This branch does not edit those HUD files.
    /// TODO(OHH-238): Assets/Scripts/Video/DetectionOverlay.cs and
    /// Assets/Scripts/UI/Session/SessionPickerPanel.cs should apply text scale
    /// when they draw labels. This branch does not edit those perception panels.
    /// Screen distance and size are applied by Assets/Scripts/MR/WorldSpaceUiPlacer.cs
    /// when a saved value exists. Left-handed sticks are applied by
    /// Assets/Scripts/Control/TeleopController.cs. Arm hands stay on their own
    /// sides; this mapping does not swap inverse kinematics.
    /// </remarks>
    [Serializable]
    public sealed class ComfortSettings
    {
        public const float DefaultScreenDistanceMeters = 1.2f;
        public const float DefaultScreenSize = 1f;
        public const float DefaultTextScale = 1f;
        public const float MinScreenDistanceMeters = 0.5f;
        public const float MaxScreenDistanceMeters = 3f;
        public const float MinScreenSize = 0.5f;
        public const float MaxScreenSize = 2f;
        public const float MinTextScale = 0.75f;
        public const float MaxTextScale = 2f;

        /// <summary>
        /// The headset rig is not driven. There is no teleport, continuous move,
        /// or character controller in this app. This cannot be turned on.
        /// </summary>
        public const bool ForcesUserLocomotion = false;

        public float screenDistanceMeters = DefaultScreenDistanceMeters;
        public float screenSize = DefaultScreenSize;
        public float textScale = DefaultTextScale;
        public bool leftHandedControls;

        public float ScreenDistanceMeters => screenDistanceMeters;
        public float ScreenSize => screenSize;
        public float TextScale => textScale;
        public bool LeftHandedControls => leftHandedControls;

        public static ComfortSettings CreateDefault()
        {
            return new ComfortSettings();
        }

        public ComfortSettings Clone()
        {
            return new ComfortSettings
            {
                screenDistanceMeters = screenDistanceMeters,
                screenSize = screenSize,
                textScale = textScale,
                leftHandedControls = leftHandedControls,
            };
        }

        public void Normalize()
        {
            screenDistanceMeters = Mathf.Clamp(
                screenDistanceMeters, MinScreenDistanceMeters, MaxScreenDistanceMeters);
            screenSize = Mathf.Clamp(screenSize, MinScreenSize, MaxScreenSize);
            textScale = Mathf.Clamp(textScale, MinTextScale, MaxTextScale);
        }
    }

    /// <summary>Listener for a saved comfort change. HUD and operate layout can implement this later.</summary>
    public interface IComfortSettingsConsumer
    {
        void ApplyComfort(ComfortSettings settings);
    }

    /// <summary>PlayerPrefs store for <see cref="ComfortSettings"/>.</summary>
    public static class ComfortSettingsStore
    {
        public const string PrefKey = "ohho.comfort.settings";

        private static ComfortSettings _current;

        public static event Action<ComfortSettings> Changed;

        public static bool HasSaved => PlayerPrefs.HasKey(PrefKey);

        public static ComfortSettings Current => _current ?? (_current = Load());

        public static ComfortSettings Load()
        {
            if (!PlayerPrefs.HasKey(PrefKey))
            {
                _current = ComfortSettings.CreateDefault();
                return _current;
            }

            try
            {
                var json = PlayerPrefs.GetString(PrefKey, "");
                var loaded = JsonConvert.DeserializeObject<ComfortSettings>(json) ?? ComfortSettings.CreateDefault();
                loaded.Normalize();
                _current = loaded;
                return _current;
            }
            catch
            {
                _current = ComfortSettings.CreateDefault();
                return _current;
            }
        }

        public static void Save(ComfortSettings settings)
        {
            var copy = settings != null ? settings.Clone() : ComfortSettings.CreateDefault();
            copy.Normalize();
            _current = copy;
            PlayerPrefs.SetString(PrefKey, JsonConvert.SerializeObject(copy));
            PlayerPrefs.Save();
            Changed?.Invoke(copy);
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
            _current = ComfortSettings.CreateDefault();
            Changed?.Invoke(_current);
        }
    }

    /// <summary>
    /// Swaps thumbstick and grip roles for a left-handed driver.
    /// Both-grips emergency-stop edges are copied unchanged.
    /// </summary>
    public static class ComfortControlMapping
    {
        public static TeleopInput Apply(TeleopInput input, bool leftHanded)
        {
            if (!leftHanded)
            {
                return input;
            }

            return new TeleopInput
            {
                LeftStick = input.RightStick,
                RightStick = input.LeftStick,
                LeftGrip = input.RightGrip,
                RightGrip = input.LeftGrip,
                BothGrips = input.BothGrips,
                BothGripsPressed = input.BothGripsPressed,
                BothGripsReleased = input.BothGripsReleased,
                DeltaTime = input.DeltaTime,
            };
        }
    }
}
