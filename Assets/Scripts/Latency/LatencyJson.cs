using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmniBot.VR.Latency
{
    /// <summary>
    /// Small JSON field readers for ROS message bodies. The harness never
    /// rewrites a payload; these helpers only look at it.
    /// </summary>
    public static class LatencyJson
    {
        /// <summary>Linear/angular/position above this counts as commanded or sensed motion.</summary>
        public const double MotionEpsilon = 0.005;

        /// <summary>Joint-position change, radians, that closes command→motion.</summary>
        public const double JointMotionEpsilon = 0.01;

        public static string MessageBody(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            int op = json.IndexOf("\"op\"", StringComparison.Ordinal);
            int msgKey = json.IndexOf("\"msg\"", StringComparison.Ordinal);
            if (op < 0 || msgKey < 0) return json;
            int colon = json.IndexOf(':', msgKey + 5);
            if (colon < 0) return json;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '{') return json;
            int end = FindMatching(json, i);
            if (end < 0) return json;
            return json.Substring(i, end - i + 1);
        }

        public static bool TryReadStampUnixMs(string json, out long unixMs)
        {
            unixMs = 0;
            if (string.IsNullOrEmpty(json)) return false;
            int stamp = json.IndexOf("\"stamp\"", StringComparison.Ordinal);
            if (stamp < 0) return false;
            int brace = json.IndexOf('{', stamp);
            if (brace < 0) return false;
            int end = FindMatching(json, brace);
            if (end < 0) return false;
            long secs;
            if (!TryReadLongKey(json, brace, end, "secs", out secs)
                && !TryReadLongKey(json, brace, end, "sec", out secs))
                return false;
            long nsec = 0;
            if (!TryReadLongKey(json, brace, end, "nsecs", out nsec))
                TryReadLongKey(json, brace, end, "nanosec", out nsec);
            unixMs = secs * 1000L + nsec / 1000000L;
            return true;
        }

        public static bool IsEmergencyStopTopic(string topic)
        {
            return Contains(topic, "emergency_stop");
        }

        public static bool IsMotionCommandTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            if (IsEmergencyStopTopic(topic)) return false;
            if (Contains(topic, "mission")) return false;
            return Contains(topic, "cmd_vel")
                || Contains(topic, "joint_commands")
                || Contains(topic, "ik_target");
        }

        public static bool IsHeartbeatTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            string t = topic.ToLowerInvariant();
            return t.Contains("heartbeat")
                || t == "/diagnostics"
                || t == "diagnostics"
                || t.EndsWith("/diagnostics", StringComparison.Ordinal);
        }

        public static bool IsTelemetryTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic) || IsHeartbeatTopic(topic)) return false;
            return Contains(topic, "odom")
                || Contains(topic, "joint_state")
                || Contains(topic, "imu");
        }

        public static bool IsCommandAckTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            return string.Equals(topic, LatencyBudget.CommandAckTopic, StringComparison.Ordinal)
                || Contains(topic, "command_ack");
        }

        public static bool CommandHasMotion(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            string stripped = StripNamedArrays(json, "covariance");
            if (ObjectsHaveMotion(stripped, "linear")) return true;
            if (ObjectsHaveMotion(stripped, "angular")) return true;
            return ArrayHasMotion(stripped, "position");
        }

        /// <summary>
        /// Motion evidence on a telemetry body: non-zero twist, non-zero joint
        /// velocity, or a joint-position change from <paramref name="jointBaseline"/>.
        /// Pose position and covariance are ignored so a stationary robot that
        /// has driven away from the origin does not look like new motion.
        /// </summary>
        public static bool TelemetryShowsMotion(string json, double[] jointBaseline)
        {
            if (string.IsNullOrEmpty(json)) return false;
            string stripped = StripNamedArrays(json, "covariance");
            if (ObjectsHaveMotion(stripped, "linear")) return true;
            if (ObjectsHaveMotion(stripped, "angular")) return true;
            if (ArrayHasMotion(stripped, "velocity")) return true;
            double[] pos = ReadNumberArray(stripped, "position");
            if (pos == null || jointBaseline == null || pos.Length != jointBaseline.Length)
                return false;
            for (int i = 0; i < pos.Length; i++)
            {
                if (Math.Abs(pos[i] - jointBaseline[i]) > JointMotionEpsilon)
                    return true;
            }
            return false;
        }

        public static double[] ReadNumberArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string token = "\"" + key + "\"";
            int hit = json.IndexOf(token, StringComparison.Ordinal);
            if (hit < 0) return null;
            int colon = json.IndexOf(':', hit + token.Length);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '[') return null;
            int end = FindMatching(json, i);
            if (end < 0) return null;
            var values = new List<double>();
            int p = i + 1;
            while (p < end)
            {
                while (p < end && (char.IsWhiteSpace(json[p]) || json[p] == ',')) p++;
                if (p >= end) break;
                double value;
                int next;
                if (!TryReadDouble(json, p, out value, out next)) return null;
                values.Add(value);
                p = next;
            }
            return values.ToArray();
        }

        public static int FindMatching(string json, int open)
        {
            if (string.IsNullOrEmpty(json) || open < 0 || open >= json.Length) return -1;
            char openCh = json[open];
            char closeCh = openCh == '{' ? '}' : openCh == '[' ? ']' : '\0';
            if (closeCh == '\0') return -1;
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = open; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == openCh) depth++;
                else if (c == closeCh)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static bool ObjectsHaveMotion(string json, string objectKey)
        {
            string token = "\"" + objectKey + "\"";
            int search = 0;
            while (search < json.Length)
            {
                int hit = json.IndexOf(token, search, StringComparison.Ordinal);
                if (hit < 0) return false;
                int colon = json.IndexOf(':', hit + token.Length);
                int brace = json.IndexOf('{', hit + token.Length);
                if (colon < 0 || brace < 0 || colon > brace)
                {
                    search = hit + token.Length;
                    continue;
                }
                int end = FindMatching(json, brace);
                if (end < 0) return false;
                if (KeyedNumberAbove(json, brace, end, "x")
                    || KeyedNumberAbove(json, brace, end, "y")
                    || KeyedNumberAbove(json, brace, end, "z"))
                    return true;
                search = end + 1;
            }
            return false;
        }

        private static bool KeyedNumberAbove(string json, int from, int to, string key)
        {
            string token = "\"" + key + "\"";
            int search = from;
            while (search < to)
            {
                int hit = json.IndexOf(token, search, StringComparison.Ordinal);
                if (hit < 0 || hit >= to) return false;
                int colon = json.IndexOf(':', hit + token.Length);
                if (colon < 0 || colon >= to) return false;
                int p = colon + 1;
                while (p < to && char.IsWhiteSpace(json[p])) p++;
                double value;
                int next;
                if (!TryReadDouble(json, p, out value, out next)) return false;
                if (Math.Abs(value) > MotionEpsilon) return true;
                search = next;
            }
            return false;
        }

        private static bool ArrayHasMotion(string json, string key)
        {
            double[] values = ReadNumberArray(json, key);
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(values[i]) > MotionEpsilon) return true;
            }
            return false;
        }

        public static string StripNamedArrays(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json)) return json ?? "";
            var sb = new StringBuilder();
            string token = "\"" + fieldName + "\"";
            int i = 0;
            while (i < json.Length)
            {
                int hit = json.IndexOf(token, i, StringComparison.Ordinal);
                if (hit < 0)
                {
                    sb.Append(json, i, json.Length - i);
                    break;
                }
                int colon = json.IndexOf(':', hit + token.Length);
                if (colon < 0)
                {
                    sb.Append(json, i, json.Length - i);
                    break;
                }
                int b = colon + 1;
                while (b < json.Length && char.IsWhiteSpace(json[b])) b++;
                if (b >= json.Length || json[b] != '[')
                {
                    sb.Append(json, i, colon + 1 - i);
                    i = colon + 1;
                    continue;
                }
                int end = FindMatching(json, b);
                if (end < 0)
                {
                    sb.Append(json, i, json.Length - i);
                    break;
                }
                sb.Append(json, i, hit - i);
                i = end + 1;
            }
            return sb.ToString();
        }

        private static bool TryReadLongKey(string json, int from, int to, string key, out long value)
        {
            value = 0;
            string token = "\"" + key + "\"";
            int hit = json.IndexOf(token, from, StringComparison.Ordinal);
            if (hit < 0 || hit >= to) return false;
            int colon = json.IndexOf(':', hit + token.Length);
            if (colon < 0 || colon >= to) return false;
            int p = colon + 1;
            while (p < to && char.IsWhiteSpace(json[p])) p++;
            int start = p;
            if (p < to && json[p] == '-') p++;
            int digits = p;
            while (p < to && char.IsDigit(json[p])) p++;
            if (p == digits) return false;
            return long.TryParse(json.Substring(start, p - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadDouble(string json, int start, out double value, out int next)
        {
            value = 0;
            next = start;
            if (json == null || start < 0 || start >= json.Length) return false;
            int i = start;
            if (json[i] == '-') i++;
            int digits = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            bool sawDigit = i > digits;
            if (i < json.Length && json[i] == '.')
            {
                i++;
                int frac = i;
                while (i < json.Length && char.IsDigit(json[i])) i++;
                sawDigit = sawDigit || i > frac;
            }
            if (!sawDigit) return false;
            if (i < json.Length && (json[i] == 'e' || json[i] == 'E'))
            {
                int e = i + 1;
                if (e < json.Length && (json[e] == '+' || json[e] == '-')) e++;
                int expDigits = e;
                while (e < json.Length && char.IsDigit(json[e])) e++;
                if (e == expDigits) return false;
                i = e;
            }
            string token = json.Substring(start, i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            next = i;
            return true;
        }

        private static bool Contains(string topic, string fragment)
        {
            return topic != null && topic.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
