using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Detection;
using UnityEngine;

namespace OmniBot.VR.AR
{
    public readonly struct ArJointReadout
    {
        public readonly string Name;
        public readonly float Radians;

        public ArJointReadout(string name, float radians)
        {
            Name = name ?? "";
            Radians = radians;
        }

        public float Degrees => Radians * Mathf.Rad2Deg;
    }

    public sealed class ArHudModel
    {
        public ArPlacementMode Placement;
        public RobotTrackState Track;
        public float Confidence;
        public int InlierCount;
        public bool HasMotion;
        public float LinearSpeedMetersPerSecond;
        public float AngularSpeedRadiansPerSecond;
        public ArJointReadout[] Joints = Array.Empty<ArJointReadout>();
    }

    public static class ArHudText
    {
        public static string Label(ArPlacementMode mode)
        {
            switch (mode)
            {
                case ArPlacementMode.Tracking:
                    return "TRACKING";
                case ArPlacementMode.Manual:
                    return "MANUAL";
                default:
                    return "UNPLACED";
            }
        }

        public static string Format(ArHudModel model)
        {
            if (model == null)
            {
                return "";
            }

            var culture = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.Append(Label(model.Placement));
            if (model.Placement == ArPlacementMode.Tracking)
            {
                text.Append("  conf ");
                text.Append(model.Confidence.ToString("0.00", culture));
                text.Append("  tags ");
                text.Append(model.InlierCount.ToString(culture));
            }
            else if (model.Placement == ArPlacementMode.Manual)
            {
                text.Append("  detection ");
                text.Append(model.Track.ToString().ToLowerInvariant());
            }

            text.Append('\n');
            if (model.HasMotion)
            {
                text.Append("lin ");
                text.Append(model.LinearSpeedMetersPerSecond.ToString("0.000", culture));
                text.Append(" m/s   ang ");
                text.Append(model.AngularSpeedRadiansPerSecond.ToString("0.000", culture));
                text.Append(" rad/s");
            }
            else
            {
                text.Append("lin —   ang —");
            }

            var joints = model.Joints;
            if (joints != null)
            {
                for (var i = 0; i < joints.Length; i++)
                {
                    text.Append('\n');
                    text.Append(joints[i].Name);
                    text.Append("  ");
                    text.Append(joints[i].Degrees.ToString("0.0", culture));
                    text.Append('°');
                }
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Display-only telemetry for the robot HUD. Subscribes through
    /// <see cref="IRobotLink"/> and never advertises, publishes, or unsubscribes.
    /// Unsubscribe would drop every other listener on that topic.
    /// </summary>
    public sealed class ArTelemetrySource
    {
        private int _generation;
        private bool _accept;

        public ArHudModel Model { get; } = new ArHudModel();
        public bool IsAttached { get; private set; }
        public string JointTopic { get; private set; } = "";
        public string OdomTopic { get; private set; } = "";

        public void SetTrack(ArModeSession session)
        {
            if (session == null)
            {
                return;
            }

            Model.Placement = session.Mode;
            Model.Track = session.Snapshot.State;
            Model.Confidence = session.Snapshot.Confidence;
            Model.InlierCount = session.Snapshot.InlierCount;
        }

        public void Attach(IRobotLink link, RobotProfile profile)
        {
            Detach();
            var topics = profile != null ? profile.Topics : null;
            if (link == null || topics == null)
            {
                return;
            }

            var generation = _generation;
            _accept = true;
            if (!string.IsNullOrEmpty(topics.JointStates))
            {
                JointTopic = topics.JointStates;
                link.Subscribe(topics.JointStates, "sensor_msgs/JointState", json =>
                {
                    if (!_accept || generation != _generation)
                    {
                        return;
                    }

                    IngestJointJson(json);
                });
            }

            if (!string.IsNullOrEmpty(topics.Odom))
            {
                OdomTopic = topics.Odom;
                link.Subscribe(topics.Odom, "nav_msgs/Odometry", json =>
                {
                    if (!_accept || generation != _generation)
                    {
                        return;
                    }

                    IngestOdomJson(json);
                });
            }

            IsAttached = true;
        }

        public void Detach()
        {
            _accept = false;
            _generation++;
            IsAttached = false;
            JointTopic = "";
            OdomTopic = "";
        }

        public void IngestJointJson(string json)
        {
            if (TryParseJoints(json, out var joints))
            {
                Model.Joints = joints;
            }
        }

        public void IngestOdomJson(string json)
        {
            if (TryParseMotion(json, out var linear, out var angular))
            {
                Model.HasMotion = true;
                Model.LinearSpeedMetersPerSecond = linear;
                Model.AngularSpeedRadiansPerSecond = angular;
            }
        }

        public static bool TryParseJoints(string json, out ArJointReadout[] joints)
        {
            joints = Array.Empty<ArJointReadout>();
            var root = ParseBody(json);
            if (root == null)
            {
                return false;
            }

            var positions = root["position"] as JArray;
            if (positions == null || positions.Count == 0)
            {
                return false;
            }

            var names = root["name"] as JArray;
            var count = positions.Count;
            if (names != null && names.Count < count)
            {
                count = names.Count;
            }

            if (count == 0)
            {
                return false;
            }

            var parsed = new ArJointReadout[count];
            for (var i = 0; i < count; i++)
            {
                var name = names != null && i < names.Count ? (string)names[i] : FallbackName(i);
                parsed[i] = new ArJointReadout(name, Float(positions[i]));
            }

            joints = parsed;
            return true;
        }

        public static bool TryParseMotion(string json, out float linearMetersPerSecond, out float angularRadiansPerSecond)
        {
            linearMetersPerSecond = 0f;
            angularRadiansPerSecond = 0f;
            var root = ParseBody(json);
            if (root == null)
            {
                return false;
            }

            var twist = root["twist"]?["twist"] ?? root["twist"];
            if (twist == null || twist["linear"] == null && twist["angular"] == null)
            {
                return false;
            }

            var linear = twist["linear"];
            var vx = Float(linear?["x"]);
            var vy = Float(linear?["y"]);
            linearMetersPerSecond = Mathf.Sqrt(vx * vx + vy * vy);
            angularRadiansPerSecond = Float(twist["angular"]?["z"]);
            return true;
        }

        private static string FallbackName(int index)
        {
            var names = RobotConfig.ARM_JOINT_NAMES;
            if (index >= 0 && index < names.Length)
            {
                return names[index];
            }

            return "joint_" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static JObject ParseBody(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var root = JObject.Parse(json);
                var hasBody = root["name"] != null || root["position"] != null || root["twist"] != null || root["pose"] != null;
                if (!hasBody && root["msg"] is JObject wrapped)
                {
                    return wrapped;
                }

                return root;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static float Float(JToken token)
        {
            if (token == null)
            {
                return 0f;
            }

            try
            {
                return token.Value<float>();
            }
            catch (Exception)
            {
                return 0f;
            }
        }
    }
}
