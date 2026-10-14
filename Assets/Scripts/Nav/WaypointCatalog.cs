using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Nav
{
    /// <summary>One named pose the headset can send as a NavigateToPose goal.</summary>
    public sealed class NamedWaypoint
    {
        public string Name;
        public string FrameId = "map";
        public double X;
        public double Y;
        public double Z;
        public double Qx;
        public double Qy;
        public double Qz;
        public double Qw = 1.0;
    }

    /// <summary>
    /// Reads named waypoints from the robot (ROS param or service payload) and
    /// holds the apartment poses from the Nav2 contract used until that payload
    /// arrives. Poses match omnibot_navigation <c>waypoints.yaml</c> (kitchen,
    /// desk, door) and omnibot_hybrid <c>named_locations.yaml</c> (home).
    /// </summary>
    public static class WaypointCatalog
    {
        public static List<NamedWaypoint> ApartmentContract()
        {
            return new List<NamedWaypoint>
            {
                Pose("kitchen", -1.9, 0.7, 0, 0, 0, 1, 0),
                Pose("desk", 2.55, -0.45, 0, 0, 0, 0.7071068, 0.7071068),
                Pose("door", -0.3, -1.45, 0, 0, 0, -0.7071068, 0.7071068),
                Pose("home", 0, 0, 0, 0, 0, 0, 1),
            };
        }

        public static void QuaternionFromYaw(double yaw, out double qx, out double qy, out double qz, out double qw)
        {
            qx = 0;
            qy = 0;
            qz = Math.Sin(yaw / 2.0);
            qw = Math.Cos(yaw / 2.0);
        }

        /// <summary>
        /// Accepts JSON (waypoints list, locations map, or a service/param wrapper)
        /// and the YAML subset used by the navigation configs.
        /// Returns an empty list when the text is not a waypoint catalog.
        /// </summary>
        public static List<NamedWaypoint> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<NamedWaypoint>();
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                try
                {
                    var inner = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(text);
                    if (!string.IsNullOrWhiteSpace(inner)) text = inner.Trim();
                }
                catch
                {
                    // not a JSON string — keep the original
                }
            }

            if (text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal))
            {
                try { return ParseJson(JToken.Parse(text)); }
                catch { return new List<NamedWaypoint>(); }
            }
            return ParseYaml(text);
        }

        public static List<NamedWaypoint> ParseServiceValues(JToken values)
        {
            if (values == null || values.Type == JTokenType.Null) return new List<NamedWaypoint>();
            if (values.Type == JTokenType.Object)
            {
                var obj = (JObject)values;
                if (obj["waypoints"] != null || obj["locations"] != null || obj["name"] != null)
                    return ParseJson(obj);
                string wrapped = obj["message"]?.Type == JTokenType.String ? obj["message"].ToString() : null;
                if (string.IsNullOrWhiteSpace(wrapped))
                    wrapped = obj["value"]?.Type == JTokenType.String ? obj["value"].ToString() : null;
                if (!string.IsNullOrWhiteSpace(wrapped))
                    return Parse(wrapped);
                if (obj["message"] != null && obj["message"].Type == JTokenType.Object)
                    return ParseJson(obj["message"]);
                if (obj["value"] != null && obj["value"].Type != JTokenType.String)
                    return ParseJson(obj["value"]);
            }
            if (values.Type == JTokenType.String)
                return Parse(values.ToString());
            if (values.Type == JTokenType.Array)
                return ParseJson(values);
            return new List<NamedWaypoint>();
        }

        private static List<NamedWaypoint> ParseJson(JToken token)
        {
            var list = new List<NamedWaypoint>();
            if (token == null) return list;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                    AddJsonWaypoint(list, item, "map");
                return list;
            }
            if (token.Type != JTokenType.Object) return list;
            var obj = (JObject)token;
            string frame = obj["frame_id"]?.ToString();
            if (string.IsNullOrEmpty(frame)) frame = "map";

            if (obj["waypoints"] is JArray waypoints)
            {
                foreach (var item in waypoints)
                    AddJsonWaypoint(list, item, frame);
                return list;
            }
            if (obj["locations"] is JObject locations)
            {
                foreach (var prop in locations.Properties())
                    AddLocation(list, prop.Name, prop.Value, frame);
                return list;
            }
            AddJsonWaypoint(list, obj, frame);
            return list;
        }

        private static void AddJsonWaypoint(List<NamedWaypoint> list, JToken item, string frame)
        {
            if (item == null || item.Type != JTokenType.Object) return;
            var obj = (JObject)item;
            string name = obj["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) return;
            var pos = obj["position"] as JObject;
            var ori = obj["orientation"] as JObject;
            double x = Num(pos?["x"] ?? obj["x"]);
            double y = Num(pos?["y"] ?? obj["y"]);
            double z = Num(pos?["z"] ?? obj["z"]);
            double qx, qy, qz, qw;
            if (ori != null)
            {
                qx = Num(ori["x"]);
                qy = Num(ori["y"]);
                qz = Num(ori["z"]);
                qw = ori["w"] != null ? Num(ori["w"]) : 1.0;
            }
            else if (obj["yaw"] != null)
            {
                QuaternionFromYaw(Num(obj["yaw"]), out qx, out qy, out qz, out qw);
            }
            else if (obj["qz"] != null || obj["qw"] != null)
            {
                qx = Num(obj["qx"]);
                qy = Num(obj["qy"]);
                qz = Num(obj["qz"]);
                qw = obj["qw"] != null ? Num(obj["qw"]) : 1.0;
            }
            else
            {
                qx = 0; qy = 0; qz = 0; qw = 1;
            }
            string itemFrame = obj["frame_id"]?.ToString();
            list.Add(new NamedWaypoint
            {
                Name = name.Trim(),
                FrameId = string.IsNullOrEmpty(itemFrame) ? frame : itemFrame,
                X = x, Y = y, Z = z,
                Qx = qx, Qy = qy, Qz = qz, Qw = qw,
            });
        }

        private static void AddLocation(List<NamedWaypoint> list, string name, JToken body, string frame)
        {
            if (string.IsNullOrWhiteSpace(name) || body == null || body.Type != JTokenType.Object) return;
            var obj = (JObject)body;
            QuaternionFromYaw(Num(obj["yaw"]), out var qx, out var qy, out var qz, out var qw);
            if (obj["orientation"] is JObject ori)
            {
                qx = Num(ori["x"]);
                qy = Num(ori["y"]);
                qz = Num(ori["z"]);
                qw = ori["w"] != null ? Num(ori["w"]) : 1.0;
            }
            list.Add(new NamedWaypoint
            {
                Name = name.Trim(),
                FrameId = frame,
                X = Num(obj["x"]),
                Y = Num(obj["y"]),
                Z = Num(obj["z"]),
                Qx = qx, Qy = qy, Qz = qz, Qw = qw,
            });
        }

        private static List<NamedWaypoint> ParseYaml(string text)
        {
            if (Regex.IsMatch(text, @"(?m)^waypoints:\s*$"))
                return ParseWaypointYaml(text);
            if (Regex.IsMatch(text, @"(?m)^locations:\s*$"))
                return ParseLocationYaml(text);
            return new List<NamedWaypoint>();
        }

        private static List<NamedWaypoint> ParseWaypointYaml(string text)
        {
            string frame = "map";
            var frameMatch = Regex.Match(text, @"(?m)^frame_id:\s*[""']?([A-Za-z0-9_/\-]+)[""']?");
            if (frameMatch.Success) frame = frameMatch.Groups[1].Value;

            var list = new List<NamedWaypoint>();
            var chunks = Regex.Split(text, @"(?m)^\s*-\s*name:\s*");
            for (int i = 1; i < chunks.Length; i++)
            {
                string chunk = chunks[i];
                var nameMatch = Regex.Match(chunk, @"^[""']?([A-Za-z0-9_\-]+)[""']?");
                if (!nameMatch.Success) continue;
                double x = FirstNumber(chunk, "x");
                double y = FirstNumber(chunk, "y");
                double z = FirstNumber(chunk, "z");
                double qx, qy, qz, qw;
                int orientationAt = chunk.IndexOf("orientation:", StringComparison.Ordinal);
                if (orientationAt >= 0 && chunk.IndexOf("w:", orientationAt, StringComparison.Ordinal) >= 0)
                {
                    string block = chunk.Substring(orientationAt);
                    qx = FirstNumber(block, "x");
                    qy = FirstNumber(block, "y");
                    qz = FirstNumber(block, "z");
                    qw = FirstNumber(block, "w");
                }
                else if (Regex.IsMatch(chunk, @"(?m)^\s*yaw_deg:\s*"))
                {
                    QuaternionFromYaw(FirstNumber(chunk, "yaw_deg") * Math.PI / 180.0, out qx, out qy, out qz, out qw);
                }
                else
                {
                    qx = 0; qy = 0; qz = 0; qw = 1;
                }
                list.Add(new NamedWaypoint
                {
                    Name = nameMatch.Groups[1].Value,
                    FrameId = frame,
                    X = x, Y = y, Z = z,
                    Qx = qx, Qy = qy, Qz = qz, Qw = qw,
                });
            }
            return list;
        }

        private static List<NamedWaypoint> ParseLocationYaml(string text)
        {
            var list = new List<NamedWaypoint>();
            var chunks = Regex.Split(text, @"(?m)^  ([A-Za-z0-9_\-]+):\s*$");
            // Split keeps the captured name in odd groups: preamble, name, body, name, body...
            for (int i = 1; i + 1 < chunks.Length; i += 2)
            {
                string name = chunks[i];
                string body = chunks[i + 1];
                if (name == "locations") continue;
                QuaternionFromYaw(FirstNumber(body, "yaw"), out var qx, out var qy, out var qz, out var qw);
                list.Add(new NamedWaypoint
                {
                    Name = name,
                    FrameId = "map",
                    X = FirstNumber(body, "x"),
                    Y = FirstNumber(body, "y"),
                    Z = FirstNumber(body, "z"),
                    Qx = qx, Qy = qy, Qz = qz, Qw = qw,
                });
            }
            return list;
        }

        private static double FirstNumber(string text, string key)
        {
            var match = Regex.Match(text, @"(?m)^\s*" + key + @":\s*([-+0-9.eE]+)");
            if (!match.Success) return 0;
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            return 0;
        }

        private static double Num(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<double>();
            if (double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            return 0;
        }

        private static NamedWaypoint Pose(string name, double x, double y, double z, double qx, double qy, double qz, double qw)
        {
            return new NamedWaypoint
            {
                Name = name, FrameId = "map",
                X = x, Y = y, Z = z, Qx = qx, Qy = qy, Qz = qz, Qw = qw,
            };
        }
    }
}
