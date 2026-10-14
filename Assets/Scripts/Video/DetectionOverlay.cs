using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// One <c>vision_msgs/Detection2D</c> box. Coordinates are either pixels
    /// (top-left origin, +y down) or fractions of the image when
    /// <see cref="Normalized"/> is set.
    /// </summary>
    public struct DetectionBox
    {
        public float CenterX;
        public float CenterY;
        public float SizeX;
        public float SizeY;
        public bool Normalized;
        public string Label;

        public bool TryPixelBounds(int width, int height, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (width <= 0 || height <= 0) return false;
            if (SizeX <= 0f || SizeY <= 0f) return false;
            if (float.IsNaN(CenterX) || float.IsNaN(CenterY) || float.IsNaN(SizeX) || float.IsNaN(SizeY))
                return false;
            float cx = CenterX;
            float cy = CenterY;
            float sx = SizeX;
            float sy = SizeY;
            if (Normalized)
            {
                cx *= width;
                cy *= height;
                sx *= width;
                sy *= height;
            }
            x0 = Clamp((int)Math.Floor(cx - sx * 0.5f), 0, width - 1);
            y0 = Clamp((int)Math.Floor(cy - sy * 0.5f), 0, height - 1);
            x1 = Clamp((int)Math.Ceiling(cx + sx * 0.5f) - 1, 0, width - 1);
            y1 = Clamp((int)Math.Ceiling(cy + sy * 0.5f) - 1, 0, height - 1);
            return x1 >= x0 && y1 >= y0;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    /// <summary>
    /// OmniBot does not publish <c>vision_msgs/Detection2DArray</c>.
    /// <c>perception.launch.py</c> starts <c>object_perception_node</c>, which
    /// reports metric detections on <c>/perception/objects</c>,
    /// <c>/perception/object_info</c>, <c>/perception/nearest_distance</c>, and
    /// <c>/perception/query_result</c>. Those are not 2D boxes, so the front
    /// camera texture stays unmarked.
    /// </summary>
    public static class DetectionTopics
    {
        public const string MessageType = "vision_msgs/Detection2DArray";

        public const string UnavailableReason =
            "No vision_msgs/Detection2DArray topic is published. " +
            "perception.launch.py starts object_perception_node, which reports metric detections " +
            "(/perception/objects, /perception/object_info, /perception/nearest_distance, /perception/query_result), " +
            "not 2D boxes. The front camera texture is left unmarked.";

        public static bool IsAvailable => TryResolve(out _);

        public static bool TryResolve(out string topic)
        {
            topic = null;
            return false;
        }
    }

    public static class Detection2DParser
    {
        public static bool TryParse(string json, List<DetectionBox> boxes, out string error)
        {
            if (boxes == null)
            {
                error = "no list";
                return false;
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty";
                return false;
            }
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            JToken body = root["msg"] is JObject wrapped ? wrapped : root;
            if (!(body["detections"] is JArray detections))
            {
                error = "no detections";
                return false;
            }
            var parsed = new List<DetectionBox>(detections.Count);
            for (int i = 0; i < detections.Count; i++)
            {
                if (detections[i] is JObject item && TryBox(item, out DetectionBox box))
                    parsed.Add(box);
            }
            boxes.Clear();
            boxes.AddRange(parsed);
            error = null;
            return true;
        }

        private static bool TryBox(JObject item, out DetectionBox box)
        {
            box = new DetectionBox();
            if (!(item["bbox"] is JObject bbox)) return false;
            JObject center = bbox["center"] as JObject;
            if (!TryFloat(center, "x", out float cx) || !TryFloat(center, "y", out float cy)) return false;
            if (!TryFloat(bbox, "size_x", out float sx) || !TryFloat(bbox, "size_y", out float sy)) return false;
            if (sx <= 0f || sy <= 0f) return false;
            box.CenterX = cx;
            box.CenterY = cy;
            box.SizeX = sx;
            box.SizeY = sy;
            box.Normalized = cx >= 0f && cy >= 0f && cx <= 1f && cy <= 1f && sx <= 1f && sy <= 1f;
            box.Label = LabelOf(item);
            return true;
        }

        private static string LabelOf(JObject item)
        {
            if (item["results"] is JArray results && results.Count > 0 && results[0] is JObject first)
            {
                if (first["hypothesis"] is JObject hypothesis)
                {
                    string id = hypothesis["class_id"]?.ToString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                string direct = first["id"]?.ToString();
                if (!string.IsNullOrEmpty(direct)) return direct;
            }
            string own = item["id"]?.ToString();
            return string.IsNullOrEmpty(own) ? "" : own;
        }

        private static bool TryFloat(JObject obj, string name, out float value)
        {
            value = 0f;
            if (obj == null) return false;
            JToken token = obj[name];
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                value = token.Value<float>();
                return !float.IsNaN(value);
            }
            return float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// Draws box borders into a bottom-up RGB24 buffer (Unity
    /// <c>LoadRawTextureData</c> layout). Box y is top-down, matching a ROS image.
    /// </summary>
    public static class DetectionPainter
    {
        public const int Thickness = 2;
        public const byte CyanR = 0;
        public const byte CyanG = 212;
        public const byte CyanB = 255;

        public static int Paint(byte[] rawBottomUp, int width, int height, IList<DetectionBox> boxes)
        {
            if (rawBottomUp == null || width <= 0 || height <= 0) return 0;
            if (rawBottomUp.Length < width * height * 3) return 0;
            if (boxes == null || boxes.Count == 0) return 0;
            int painted = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (!boxes[i].TryPixelBounds(width, height, out int x0, out int y0, out int x1, out int y1))
                    continue;
                PaintBorder(rawBottomUp, width, height, x0, y0, x1, y1);
                painted++;
            }
            return painted;
        }

        public static bool TryPaint(Texture2D source, IList<DetectionBox> boxes, ref Texture2D scratch)
        {
            if (source == null || boxes == null || boxes.Count == 0) return false;
            if (source.format != TextureFormat.RGB24) return false;
            int width = source.width;
            int height = source.height;
            if (width <= 0 || height <= 0) return false;
            byte[] raw;
            try
            {
                byte[] from = source.GetRawTextureData();
                if (from == null) return false;
                raw = new byte[from.Length];
                Buffer.BlockCopy(from, 0, raw, 0, from.Length);
            }
            catch (Exception)
            {
                return false;
            }
            if (Paint(raw, width, height, boxes) == 0) return false;
            if (scratch == null || scratch.width != width || scratch.height != height || scratch.format != TextureFormat.RGB24)
            {
                if (scratch != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(scratch);
                    else UnityEngine.Object.DestroyImmediate(scratch);
                }
                scratch = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                scratch.wrapMode = TextureWrapMode.Clamp;
                scratch.filterMode = FilterMode.Bilinear;
                scratch.name = "FrontDetections";
            }
            scratch.LoadRawTextureData(raw);
            scratch.Apply(false, false);
            return true;
        }

        private static void PaintBorder(byte[] raw, int width, int height, int x0, int y0, int x1, int y1)
        {
            int thick = Thickness;
            if (thick < 1) thick = 1;
            for (int y = y0; y <= y1; y++)
            {
                bool yEdge = y < y0 + thick || y > y1 - thick;
                int rawY = height - 1 - y;
                for (int x = x0; x <= x1; x++)
                {
                    bool xEdge = x < x0 + thick || x > x1 - thick;
                    if (!xEdge && !yEdge) continue;
                    int i = (rawY * width + x) * 3;
                    raw[i] = CyanR;
                    raw[i + 1] = CyanG;
                    raw[i + 2] = CyanB;
                }
            }
        }
    }

    /// <summary>
    /// Puts detection boxes on the front camera texture. While
    /// <see cref="DetectionTopics.IsAvailable"/> is false this returns the
    /// source texture unchanged.
    /// </summary>
    public sealed class FrontDetectionCompositor
    {
        private Texture2D _scratch;

        public Texture2D Present(Texture2D source, string topic, IList<DetectionBox> boxes)
        {
            if (source == null) return null;
            if (!DetectionTopics.IsAvailable) return source;
            if (topic != SimScreenCameras.FrontTopic) return source;
            if (boxes == null || boxes.Count == 0) return source;
            if (!DetectionPainter.TryPaint(source, boxes, ref _scratch)) return source;
            return _scratch;
        }

        public void Release()
        {
            if (_scratch == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_scratch);
            else UnityEngine.Object.DestroyImmediate(_scratch);
            _scratch = null;
        }
    }
}
