using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// One <c>nav_msgs/OccupancyGrid</c>. Cell (0, 0) is the origin corner and
    /// row 0 is the minimum Y, matching ROS and a Unity texture (Y up). The
    /// Android <c>SlamMapView</c> flips Y because a Bitmap's row 0 is the top
    /// of the canvas; this panel does not, so the map reads the same way up as
    /// RViz.
    /// </summary>
    public sealed class OccupancyGrid
    {
        public int Width;
        public int Height;
        public float Resolution = 0.05f;
        public float OriginX;
        public float OriginY;
        public float OriginYaw;
        public string FrameId = MapTopics.DefaultMapFrame;
        public int[] Cells = System.Array.Empty<int>();

        public void SetSize(int width, int height)
        {
            int n = width * height;
            if (Cells == null || Cells.Length < n) Cells = new int[n];
            Width = width;
            Height = height;
        }

        public int CellAt(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || Cells == null) return -1;
            return Cells[y * Width + x];
        }

        public void WorldToPixel(float worldX, float worldY, out float px, out float py)
        {
            float dx = worldX - OriginX;
            float dy = worldY - OriginY;
            float c = (float)Math.Cos(OriginYaw);
            float s = (float)Math.Sin(OriginYaw);
            float lx = c * dx + s * dy;
            float ly = -s * dx + c * dy;
            float res = Resolution <= 0f ? 0.05f : Resolution;
            px = lx / res;
            py = ly / res;
        }

        public void PixelToWorld(float px, float py, out float worldX, out float worldY)
        {
            float res = Resolution <= 0f ? 0.05f : Resolution;
            float lx = px * res;
            float ly = py * res;
            float c = (float)Math.Cos(OriginYaw);
            float s = (float)Math.Sin(OriginYaw);
            worldX = OriginX + c * lx - s * ly;
            worldY = OriginY + s * lx + c * ly;
        }
    }

    public static class OccupancyColors
    {
        public static readonly Color32 Unknown = new Color32(128, 128, 128, 255);
        public static readonly Color32 Free = new Color32(255, 255, 255, 255);
        public static readonly Color32 Occupied = new Color32(0, 0, 0, 255);
        public static readonly Color32 Lethal = new Color32(220, 40, 40, 255);
        public static readonly Color32 Inflation = new Color32(255, 140, 0, 255);
        public static readonly Color32 ScanPoint = new Color32(0, 220, 255, 255);

        public static Color32 ForOccupancy(int value)
        {
            if (value < 0 || value >= 255) return Unknown;
            if (value == 0) return Free;
            if (value >= 100) return Occupied;
            byte g = (byte)(255 - (value * 255) / 100);
            return new Color32(g, g, g, 255);
        }
    }

    /// <summary>
    /// Decodes an occupancy grid from a raw int8 JSON array or from a rosbridge
    /// png envelope, and paints it. Also projects a laser scan and a tap into
    /// the same pixel space.
    /// </summary>
    public static class OccupancyGridCodec
    {
        public const int MaxCells = 4_000_000;

        public static bool TryDecode(string json, OccupancyGrid into)
        {
            if (into == null || string.IsNullOrEmpty(json)) return false;
            try
            {
                JObject root = JObject.Parse(json);
                string op = root["op"]?.ToString();
                if (op == "png")
                {
                    if (!RosbridgePng.TryDecodeToJson(root["data"]?.ToString(), out string inner))
                        return false;
                    root = JObject.Parse(inner);
                    op = root["op"]?.ToString();
                }
                if (op == "publish")
                {
                    if (!(root["msg"] is JObject msg)) return false;
                    root = msg;
                }
                return TryReadGrid(root, into);
            }
            catch (JsonException) { return false; }
            catch (System.Exception) { return false; }
        }

        public static int Colorize(OccupancyGrid map, ref Color32[] dest)
        {
            if (map == null || map.Width <= 0 || map.Height <= 0) return 0;
            int n = map.Width * map.Height;
            if (dest == null || dest.Length != n) dest = new Color32[n];
            int known = 0;
            var cells = map.Cells;
            for (int i = 0; i < n; i++)
            {
                int value = cells != null && i < cells.Length ? cells[i] : -1;
                if (value >= 0 && value < 255) known++;
                dest[i] = OccupancyColors.ForOccupancy(value);
            }
            return known;
        }

        /// <summary>
        /// Paints a Nav2 costmap over an already-colorized map. Free and unknown
        /// cost cells leave the SLAM colour alone. <paramref name="costFromMap"/>
        /// maps a point in the map frame into the costmap frame; pass identity
        /// when both grids share the map frame.
        /// </summary>
        public static void BlendCostmap(OccupancyGrid map, OccupancyGrid cost, bool sameFrame, Se2 costFromMap, Color32[] dest)
        {
            if (map == null || cost == null || dest == null) return;
            if (map.Width <= 0 || cost.Width <= 0) return;
            int n = map.Width * map.Height;
            if (dest.Length < n) return;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.PixelToWorld(x + 0.5f, y + 0.5f, out float wx, out float wy);
                    if (!sameFrame)
                        Se2.Transform(costFromMap, wx, wy, out wx, out wy);
                    cost.WorldToPixel(wx, wy, out float px, out float py);
                    int ix = (int)Math.Floor(px);
                    int iy = (int)Math.Floor(py);
                    int value = cost.CellAt(ix, iy);
                    if (value <= 0 || value >= 255) continue;
                    int i = y * map.Width + x;
                    if (value >= 100)
                    {
                        dest[i] = OccupancyColors.Lethal;
                        continue;
                    }
                    int a = 60 + value * 2;
                    if (a > 220) a = 220;
                    Blend(ref dest[i], OccupancyColors.Inflation, (byte)a);
                }
            }
        }

        public static ArrowPlacement PlaceArrow(OccupancyGrid grid, float worldX, float worldY, float worldYaw, float nosePixels)
        {
            var arrow = new ArrowPlacement();
            if (grid == null) return arrow;
            grid.WorldToPixel(worldX, worldY, out arrow.PixelX, out arrow.PixelY);
            arrow.PixelYaw = worldYaw - grid.OriginYaw;
            arrow.NoseX = arrow.PixelX + nosePixels * (float)Math.Cos(arrow.PixelYaw);
            arrow.NoseY = arrow.PixelY + nosePixels * (float)Math.Sin(arrow.PixelYaw);
            return arrow;
        }

        public static bool TryLocalPointToPose(Rect rect, Vector2 local, OccupancyGrid grid, out MapFramePose pose)
        {
            pose = default;
            if (grid == null || grid.Width <= 0 || grid.Height <= 0) return false;
            if (rect.width <= 0f || rect.height <= 0f) return false;
            float u = (local.x - rect.x) / rect.width;
            float v = (local.y - rect.y) / rect.height;
            if (u < 0f || v < 0f || u > 1f || v > 1f) return false;
            grid.PixelToWorld(u * grid.Width, v * grid.Height, out float x, out float y);
            pose = new MapFramePose(x, y, 0f, grid.FrameId);
            return true;
        }

        /// <summary>
        /// Projects scan returns into map pixels. Invalid, out-of-range, and
        /// off-grid returns are skipped. <paramref name="xs"/> and
        /// <paramref name="ys"/> must be at least <c>scan.Count</c> long.
        /// </summary>
        public static int ProjectScan(LaserScanView scan, Se2 sensorInMap, OccupancyGrid grid, int[] xs, int[] ys)
        {
            if (scan == null || grid == null || grid.Width <= 0 || xs == null || ys == null) return 0;
            int n = scan.Count;
            if (n > xs.Length) n = xs.Length;
            if (n > ys.Length) n = ys.Length;
            float cos = (float)Math.Cos(sensorInMap.Yaw);
            float sin = (float)Math.Sin(sensorInMap.Yaw);
            int written = 0;
            var ranges = scan.Ranges;
            for (int i = 0; i < n; i++)
            {
                float range = ranges != null && i < ranges.Length ? ranges[i] : float.NaN;
                if (float.IsNaN(range) || float.IsInfinity(range)) continue;
                if (range < scan.RangeMin || range > scan.RangeMax) continue;
                float angle = scan.AngleMin + i * scan.AngleIncrement;
                float lx = range * (float)Math.Cos(angle);
                float ly = range * (float)Math.Sin(angle);
                float wx = sensorInMap.X + cos * lx - sin * ly;
                float wy = sensorInMap.Y + sin * lx + cos * ly;
                grid.WorldToPixel(wx, wy, out float px, out float py);
                int ix = (int)Math.Round(px);
                int iy = (int)Math.Round(py);
                if ((uint)ix >= (uint)grid.Width || (uint)iy >= (uint)grid.Height) continue;
                xs[written] = ix;
                ys[written] = iy;
                written++;
            }
            return written;
        }

        public static float YawFromQuaternion(float x, float y, float z, float w)
        {
            return (float)Math.Atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z));
        }

        static bool TryReadGrid(JObject root, OccupancyGrid into)
        {
            if (!(root["info"] is JObject info)) return false;
            int width = info["width"] != null ? info["width"].Value<int>() : 0;
            int height = info["height"] != null ? info["height"].Value<int>() : 0;
            if (width <= 0 || height <= 0) return false;
            long cells = (long)width * height;
            if (cells > MaxCells) return false;
            float resolution = info["resolution"] != null ? info["resolution"].Value<float>() : 0.05f;
            if (resolution <= 0f) return false;

            float ox = 0f, oy = 0f, oyaw = 0f;
            if (info["origin"] is JObject origin)
            {
                if (origin["position"] is JObject pos)
                {
                    ox = Value(pos, "x");
                    oy = Value(pos, "y");
                }
                if (origin["orientation"] is JObject q)
                {
                    oyaw = YawFromQuaternion(Value(q, "x"), Value(q, "y"), Value(q, "z"),
                        q["w"] != null ? q["w"].Value<float>() : 1f);
                }
            }

            string frame = MapTopics.DefaultMapFrame;
            if (root["header"] is JObject header && header["frame_id"] != null)
                frame = TfBuffer.Frame(header["frame_id"].ToString());
            if (frame.Length == 0) frame = MapTopics.DefaultMapFrame;

            into.SetSize(width, height);
            into.Resolution = resolution;
            into.OriginX = ox;
            into.OriginY = oy;
            into.OriginYaw = oyaw;
            into.FrameId = frame;

            int n = width * height;
            JToken data = root["data"];
            if (data is JArray array)
            {
                for (int i = 0; i < n; i++)
                    into.Cells[i] = i < array.Count ? ReadNumber(array[i]) : -1;
                return true;
            }
            if (data != null && data.Type == JTokenType.String)
                return ReadRawBytes(data.ToString(), into, n);
            for (int i = 0; i < n; i++) into.Cells[i] = -1;
            return data == null;
        }

        static bool ReadRawBytes(string encoded, OccupancyGrid into, int n)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException) { return false; }
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50) return false;
            if (bytes.Length < n) return false;
            for (int i = 0; i < n; i++)
            {
                int v = bytes[i];
                into.Cells[i] = v > 127 ? v - 256 : v;
            }
            return true;
        }

        static int ReadNumber(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return -1;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<int>();
            return -1;
        }

        static float Value(JObject obj, string name)
        {
            return obj[name] != null ? obj[name].Value<float>() : 0f;
        }

        static void Blend(ref Color32 dst, Color32 src, byte a)
        {
            int inv = 255 - a;
            dst.r = (byte)((dst.r * inv + src.r * a) / 255);
            dst.g = (byte)((dst.g * inv + src.g * a) / 255);
            dst.b = (byte)((dst.b * inv + src.b * a) / 255);
            dst.a = 255;
        }
    }

    /// <summary>A <c>sensor_msgs/LaserScan</c> kept in a reused range buffer.</summary>
    public sealed class LaserScanView
    {
        public float[] Ranges = System.Array.Empty<float>();
        public int Count;
        public float AngleMin;
        public float AngleIncrement;
        public float RangeMin;
        public float RangeMax = 100f;
        public string FrameId = "";

        public bool TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                JObject root = JObject.Parse(json);
                if (!(root["ranges"] is JArray ranges)) return false;
                int n = ranges.Count;
                if (n > 20000) n = 20000;
                if (Ranges == null || Ranges.Length < n) Ranges = new float[n];
                for (int i = 0; i < n; i++)
                {
                    JToken token = ranges[i];
                    if (token == null || token.Type == JTokenType.Null) Ranges[i] = float.NaN;
                    else Ranges[i] = token.Value<float>();
                }
                Count = n;
                AngleMin = Num(root, "angle_min");
                AngleIncrement = Num(root, "angle_increment");
                RangeMin = Num(root, "range_min");
                RangeMax = root["range_max"] != null ? root["range_max"].Value<float>() : 100f;
                FrameId = "";
                if (root["header"] is JObject header && header["frame_id"] != null)
                    FrameId = TfBuffer.Frame(header["frame_id"].ToString());
                return true;
            }
            catch (JsonException) { return false; }
            catch (System.Exception) { return false; }
        }

        static float Num(JObject obj, string name)
        {
            return obj[name] != null ? obj[name].Value<float>() : 0f;
        }
    }

    /// <summary>Pulls 2D poses out of <c>/tf</c> and <c>/odom</c> without building the full message types.</summary>
    public static class PoseMessages
    {
        public static bool TryApplyTf(string json, TfBuffer buffer)
        {
            if (buffer == null || string.IsNullOrEmpty(json)) return false;
            try
            {
                JObject root = JObject.Parse(json);
                if (!(root["transforms"] is JArray list) || list.Count == 0) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is JObject tf)) continue;
                    string parent = "";
                    if (tf["header"] is JObject header && header["frame_id"] != null)
                        parent = header["frame_id"].ToString();
                    string child = tf["child_frame_id"] != null ? tf["child_frame_id"].ToString() : "";
                    if (!(tf["transform"] is JObject tr)) continue;
                    float x = 0f, y = 0f, yaw = 0f;
                    if (tr["translation"] is JObject p)
                    {
                        x = Num(p, "x");
                        y = Num(p, "y");
                    }
                    if (tr["rotation"] is JObject q)
                    {
                        yaw = OccupancyGridCodec.YawFromQuaternion(
                            Num(q, "x"), Num(q, "y"), Num(q, "z"),
                            q["w"] != null ? q["w"].Value<float>() : 1f);
                    }
                    buffer.Set(parent, child, new Se2(x, y, yaw));
                }
                return true;
            }
            catch (JsonException) { return false; }
            catch (System.Exception) { return false; }
        }

        public static bool TryApplyOdom(string json, TfBuffer buffer, out Se2 pose)
        {
            pose = Se2.Identity;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                JObject root = JObject.Parse(json);
                if (!(root["pose"] is JObject poseWrap)) return false;
                if (!(poseWrap["pose"] is JObject poseMsg)) return false;
                if (!(poseMsg["position"] is JObject position)) return false;
                float x = Num(position, "x");
                float y = Num(position, "y");
                float yaw = 0f;
                if (poseMsg["orientation"] is JObject q)
                {
                    yaw = OccupancyGridCodec.YawFromQuaternion(
                        Num(q, "x"), Num(q, "y"), Num(q, "z"),
                        q["w"] != null ? q["w"].Value<float>() : 1f);
                }
                pose = new Se2(x, y, yaw);
                string parent = "odom";
                if (root["header"] is JObject header && header["frame_id"] != null)
                    parent = header["frame_id"].ToString();
                string child = root["child_frame_id"] != null ? root["child_frame_id"].ToString() : "base_link";
                if (buffer != null) buffer.Set(parent, child, pose);
                return true;
            }
            catch (JsonException) { return false; }
            catch (System.Exception) { return false; }
        }

        static float Num(JObject obj, string name)
        {
            return obj[name] != null ? obj[name].Value<float>() : 0f;
        }
    }
}
