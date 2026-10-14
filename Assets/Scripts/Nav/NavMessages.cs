using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Core;

namespace OmniBot.VR.Nav
{
    public sealed class GlobalPlan
    {
        public readonly List<PlanPoint> Points = new List<PlanPoint>();
        public float LengthMeters { get; private set; }

        public void RecomputeLength()
        {
            float length = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                float dx = Points[i].X - Points[i - 1].X;
                float dy = Points[i].Y - Points[i - 1].Y;
                length += (float)Math.Sqrt(dx * dx + dy * dy);
            }
            LengthMeters = length;
        }
    }

    public struct PlanPoint
    {
        public float X;
        public float Y;
    }

    public sealed class OccupancyMap
    {
        public int Width;
        public int Height;
        public float Resolution;
        public float OriginX;
        public float OriginY;
        public int[] Cells;
    }

    /// <summary>
    /// rosbridge JSON for <c>navigate_to_pose</c>, <c>/plan</c>, <c>/map</c>, and
    /// the waypoint param/service calls. Field names follow ROS 2 (sec/nanosec).
    /// </summary>
    public static class NavMessages
    {
        public const string ListServiceId = "ohho-wp-list";
        public const string ParamOhhoId = "ohho-wp-param-ohho";
        public const string ParamRootId = "ohho-wp-param-root";
        public const string ParamOhhoName = "/ohho/waypoints";
        public const string ParamRootName = "/waypoints";

        public static string SendGoalJson(string id, NamedWaypoint wp)
        {
            var root = new JObject
            {
                ["op"] = "send_action_goal",
                ["id"] = id,
                ["action"] = RobotConfig.ACTION_NAVIGATE_TO_POSE,
                ["action_type"] = RobotConfig.ACTION_NAVIGATE_TO_POSE_TYPE,
                ["feedback"] = true,
                ["args"] = new JObject
                {
                    ["pose"] = PoseStamped(wp),
                },
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string CancelGoalJson(string id)
        {
            var root = new JObject
            {
                ["op"] = "cancel_action_goal",
                ["id"] = id ?? "",
                ["action"] = RobotConfig.ACTION_NAVIGATE_TO_POSE,
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ListWaypointsCallJson()
        {
            return CallServiceJson(
                ListServiceId,
                RobotConfig.SERVICE_LIST_WAYPOINTS,
                "std_srvs/srv/Trigger",
                new JObject());
        }

        public static string GetParamCallJson(string id, string paramName)
        {
            return CallServiceJson(
                id,
                RobotConfig.SERVICE_GET_PARAM,
                "rosapi/srv/GetParam",
                new JObject
                {
                    ["name"] = paramName,
                    ["default"] = "",
                });
        }

        public static string CallServiceJson(string id, string service, string type, JObject args)
        {
            var root = new JObject
            {
                ["op"] = "call_service",
                ["id"] = id,
                ["service"] = service,
                ["type"] = type,
                ["args"] = args ?? new JObject(),
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static JObject PoseStamped(NamedWaypoint wp)
        {
            return new JObject
            {
                ["header"] = new JObject
                {
                    ["frame_id"] = string.IsNullOrEmpty(wp.FrameId) ? "map" : wp.FrameId,
                    ["stamp"] = new JObject { ["sec"] = 0, ["nanosec"] = 0 },
                },
                ["pose"] = new JObject
                {
                    ["position"] = new JObject { ["x"] = wp.X, ["y"] = wp.Y, ["z"] = wp.Z },
                    ["orientation"] = new JObject
                    {
                        ["x"] = wp.Qx, ["y"] = wp.Qy, ["z"] = wp.Qz, ["w"] = wp.Qw,
                    },
                },
            };
        }

        public static string GoalStatusName(int status)
        {
            switch (status)
            {
                case 1: return "accepted";
                case 2: return "executing";
                case 3: return "canceling";
                case 4: return "succeeded";
                case 5: return "canceled";
                case 6: return "aborted";
                default: return "unknown";
            }
        }

        public static bool IsTerminalStatus(int status) => status >= 4 && status <= 6;

        public static bool TryDurationSeconds(JToken token, out float seconds)
        {
            seconds = 0f;
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                seconds = token.Value<float>();
                return true;
            }
            if (token.Type != JTokenType.Object) return false;
            int sec = IntOf(token["sec"]) ?? IntOf(token["secs"]) ?? 0;
            int nsec = IntOf(token["nanosec"]) ?? IntOf(token["nsecs"]) ?? 0;
            seconds = sec + nsec * 1e-9f;
            return true;
        }

        public static GlobalPlan ParsePlan(string msgJson)
        {
            if (string.IsNullOrWhiteSpace(msgJson)) return null;
            JObject root;
            try { root = JObject.Parse(msgJson); }
            catch { return null; }
            if (!(root["poses"] is JArray poses)) return null;
            var plan = new GlobalPlan();
            foreach (var pose in poses)
            {
                var position = pose["pose"]?["position"];
                if (position == null) continue;
                plan.Points.Add(new PlanPoint
                {
                    X = (float)(position["x"]?.Value<double>() ?? 0),
                    Y = (float)(position["y"]?.Value<double>() ?? 0),
                });
            }
            plan.RecomputeLength();
            return plan;
        }

        public static OccupancyMap ParseMap(string msgJson)
        {
            if (string.IsNullOrWhiteSpace(msgJson)) return null;
            JObject root;
            try { root = JObject.Parse(msgJson); }
            catch { return null; }
            var info = root["info"] as JObject;
            if (info == null) return null;
            int width = info["width"]?.Value<int>() ?? 0;
            int height = info["height"]?.Value<int>() ?? 0;
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048) return null;
            if (!(root["data"] is JArray data) || data.Count < width * height) return null;
            var map = new OccupancyMap
            {
                Width = width,
                Height = height,
                Resolution = info["resolution"]?.Value<float>() ?? 0.05f,
                OriginX = (float)(info["origin"]?["position"]?["x"]?.Value<double>() ?? 0),
                OriginY = (float)(info["origin"]?["position"]?["y"]?.Value<double>() ?? 0),
                Cells = new int[width * height],
            };
            if (map.Resolution <= 0f) return null;
            for (int i = 0; i < map.Cells.Length; i++)
                map.Cells[i] = data[i]?.Value<int>() ?? -1;
            return map;
        }

        /// <summary>
        /// Map a point inside the map image (origin at the bottom-left, Y up,
        /// matching the occupancy grid) to a pose in the map frame.
        /// </summary>
        public static bool TryMapClick(
            float localX, float localY, float rectWidth, float rectHeight,
            OccupancyMap map, out float worldX, out float worldY)
        {
            worldX = 0f;
            worldY = 0f;
            if (map == null || rectWidth <= 0f || rectHeight <= 0f) return false;
            if (localX < 0f || localY < 0f || localX > rectWidth || localY > rectHeight) return false;
            float u = localX / rectWidth;
            float v = localY / rectHeight;
            int px = (int)(u * map.Width);
            int py = (int)(v * map.Height);
            if (px >= map.Width) px = map.Width - 1;
            if (py >= map.Height) py = map.Height - 1;
            if (px < 0 || py < 0) return false;
            worldX = map.OriginX + (px + 0.5f) * map.Resolution;
            worldY = map.OriginY + (py + 0.5f) * map.Resolution;
            return true;
        }

        public static bool TryWorldToCell(float worldX, float worldY, OccupancyMap map, out int px, out int py)
        {
            px = 0;
            py = 0;
            if (map == null || map.Resolution <= 0f) return false;
            px = (int)Math.Round((worldX - map.OriginX) / map.Resolution - 0.5, MidpointRounding.AwayFromZero);
            py = (int)Math.Round((worldY - map.OriginY) / map.Resolution - 0.5, MidpointRounding.AwayFromZero);
            return px >= 0 && py >= 0 && px < map.Width && py < map.Height;
        }

        private static int? IntOf(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<int>();
            if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            return null;
        }
    }
}
