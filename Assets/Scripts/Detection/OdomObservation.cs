using System;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// One nav_msgs/Odometry sample, already converted into the Unity base frame.
    /// The absolute pose stays in the odom frame. The pipeline uses deltas only.
    /// </summary>
    public readonly struct OdomObservation
    {
        public OdomObservation(bool hasStamp, double stampSeconds, RigidTransform baseInOdomUnity)
        {
            HasStamp = hasStamp;
            StampSeconds = stampSeconds;
            BaseInOdomUnity = baseInOdomUnity;
        }

        public bool HasStamp { get; }
        public double StampSeconds { get; }
        public RigidTransform BaseInOdomUnity { get; }
    }

    public static class OdomObservationParser
    {
        public static bool TryParse(string json, out OdomObservation observation)
        {
            observation = default;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                return false;
            }

            var msg = root["pose"] != null ? root : root["msg"] as JObject;
            if (msg == null)
            {
                return false;
            }

            var pose = msg["pose"]?["pose"] ?? msg["pose"];
            var position = pose?["position"];
            var orientation = pose?["orientation"];
            if (position == null || orientation == null)
            {
                return false;
            }

            if (!TryDouble(position["x"], out var x)
                || !TryDouble(position["y"], out var y)
                || !TryDouble(position["z"], out var z)
                || !TryDouble(orientation["x"], out var qx)
                || !TryDouble(orientation["y"], out var qy)
                || !TryDouble(orientation["z"], out var qz)
                || !TryDouble(orientation["w"], out var qw))
            {
                return false;
            }

            var ros = RigidTransform.FromQuaternion(x, y, z, qx, qy, qz, qw);
            var unity = DetectionFrames.OdomPoseToUnity(ros);
            if (!unity.IsFinite)
            {
                return false;
            }

            var hasStamp = false;
            var stamp = 0.0;
            var stampToken = msg["header"]?["stamp"];
            if (stampToken != null)
            {
                TryDouble(stampToken["secs"] ?? stampToken["sec"], out var secs);
                TryDouble(stampToken["nsecs"] ?? stampToken["nanosec"], out var nsecs);
                if (secs != 0.0 || nsecs != 0.0)
                {
                    hasStamp = true;
                    stamp = secs + nsecs * 1e-9;
                }
            }

            observation = new OdomObservation(hasStamp, stamp, unity);
            return true;
        }

        private static bool TryDouble(JToken token, out double value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            try
            {
                value = token.Value<double>();
            }
            catch (Exception)
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
