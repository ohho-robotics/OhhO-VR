using System;
using System.Globalization;
using UnityEngine;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Local pose and size of the sim screen. Width and height are canvas pixels
    /// (the camera canvas is scaled by 0.001, so 1600 is 1.6 m). Radius is metres.
    /// </summary>
    public struct SimScreenPoseState
    {
        public float LocalX;
        public float LocalY;
        public float LocalZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public float RotW;
        public float Width;
        public float Height;
        public float Radius;
        public bool HasSaved;
    }

    public static class SimScreenPose
    {
        public const float MinWidth = 400f;
        public const float MinHeight = 300f;
        public const float MaxWidth = 4000f;
        public const float MaxHeight = 2500f;
        public const float DefaultWidth = 1600f;
        public const float DefaultHeight = 900f;
        public const float DefaultRadius = 2.2f;
        public const float MinRadius = 0.8f;
        public const float MaxRadius = 6f;
        public const float MaxOffset = 20f;

        public static SimScreenPoseState Default()
        {
            return new SimScreenPoseState
            {
                RotW = 1f,
                Width = DefaultWidth,
                Height = DefaultHeight,
                Radius = DefaultRadius,
                HasSaved = false,
            };
        }

        public static SimScreenPoseState Clamp(SimScreenPoseState state)
        {
            state.LocalX = ClampOffset(state.LocalX);
            state.LocalY = ClampOffset(state.LocalY);
            state.LocalZ = ClampOffset(state.LocalZ);
            state.Width = ClampRange(state.Width, MinWidth, MaxWidth);
            state.Height = ClampRange(state.Height, MinHeight, MaxHeight);
            state.Radius = ClampRange(state.Radius, MinRadius, MaxRadius);
            float mag = (float)Math.Sqrt(state.RotX * state.RotX + state.RotY * state.RotY
                + state.RotZ * state.RotZ + state.RotW * state.RotW);
            if (mag < 1e-4f)
            {
                state.RotX = 0f;
                state.RotY = 0f;
                state.RotZ = 0f;
                state.RotW = 1f;
            }
            else
            {
                state.RotX /= mag;
                state.RotY /= mag;
                state.RotZ /= mag;
                state.RotW /= mag;
            }
            return state;
        }

        public static string Serialize(SimScreenPoseState state)
        {
            state = Clamp(state);
            return string.Join(" ", new[]
            {
                F(state.LocalX), F(state.LocalY), F(state.LocalZ),
                F(state.RotX), F(state.RotY), F(state.RotZ), F(state.RotW),
                F(state.Width), F(state.Height), F(state.Radius),
            });
        }

        public static bool TryParse(string text, out SimScreenPoseState state)
        {
            state = Default();
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 10) return false;
            float[] v = new float[10];
            for (int i = 0; i < 10; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return false;
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) return false;
            }
            state = Clamp(new SimScreenPoseState
            {
                LocalX = v[0], LocalY = v[1], LocalZ = v[2],
                RotX = v[3], RotY = v[4], RotZ = v[5], RotW = v[6],
                Width = v[7], Height = v[8], Radius = v[9],
                HasSaved = true,
            });
            return true;
        }

        private static float ClampOffset(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < -MaxOffset) return -MaxOffset;
            if (value > MaxOffset) return MaxOffset;
            return value;
        }

        private static float ClampRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string F(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }
    }

    public static class SimScreenPoseStore
    {
        public const string Key = "ohho.sim_screen.pose";

        public static bool TryLoad(out SimScreenPoseState state)
        {
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw) || !SimScreenPose.TryParse(raw, out state))
            {
                state = SimScreenPose.Default();
                return false;
            }
            return true;
        }

        public static void Save(SimScreenPoseState state)
        {
            state.HasSaved = true;
            PlayerPrefs.SetString(Key, SimScreenPose.Serialize(state));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
        }
    }
}
