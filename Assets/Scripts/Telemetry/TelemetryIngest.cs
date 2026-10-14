using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ohho.Schemas;

namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// Display copy of a safety sample. <see cref="Measured"/> is false when
    /// the document is missing. False deadman is not a permit. This struct
    /// does not command motion.
    /// </summary>
    public struct SafetyReading
    {
        public bool Measured;
        public bool Deadman;
        public bool Active;
        public bool Latched;
        public EstopSource Source;
        public bool HasMode;
        public SubsystemMode BaseMode;
        public SubsystemMode ArmMode;
        public bool HasLink;
        public double RttMs;
        public double Hz;
    }

    public static class ModeMap
    {
        public static string Base(string controlMode)
        {
            if (string.IsNullOrEmpty(controlMode)) return "idle";
            if (Equals(controlMode, "nav2") || Equals(controlMode, "vla") || Equals(controlMode, "rl_nav") || Equals(controlMode, "auto"))
                return "auto";
            if (Equals(controlMode, "idle") || Equals(controlMode, "teleop") || Equals(controlMode, "hold") || Equals(controlMode, "fault") || Equals(controlMode, "not_present"))
                return Lower(controlMode);
            return "idle";
        }

        public static string Arm(string controlMode, string armMode, bool armPresent)
        {
            if (!armPresent) return "not_present";
            if (Equals(controlMode, "teleop")) return "teleop";
            if (Equals(controlMode, "nav2") || Equals(controlMode, "vla") || Equals(controlMode, "rl_nav") || Equals(controlMode, "auto"))
                return "auto";
            if (IsArmSchema(armMode)) return Lower(armMode);
            return "idle";
        }

        static bool IsArmSchema(string mode)
        {
            return Equals(mode, "idle") || Equals(mode, "teleop") || Equals(mode, "auto")
                || Equals(mode, "hold") || Equals(mode, "fault") || Equals(mode, "not_present");
        }

        static bool Equals(string value, string literal)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return string.Equals(value.Trim(), literal, StringComparison.OrdinalIgnoreCase);
        }

        static string Lower(string value)
        {
            if (string.IsNullOrEmpty(value)) return "idle";
            string trimmed = value.Trim();
            if (string.Equals(trimmed, "idle", StringComparison.OrdinalIgnoreCase)) return "idle";
            if (string.Equals(trimmed, "teleop", StringComparison.OrdinalIgnoreCase)) return "teleop";
            if (string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)) return "auto";
            if (string.Equals(trimmed, "hold", StringComparison.OrdinalIgnoreCase)) return "hold";
            if (string.Equals(trimmed, "fault", StringComparison.OrdinalIgnoreCase)) return "fault";
            if (string.Equals(trimmed, "not_present", StringComparison.OrdinalIgnoreCase)) return "not_present";
            return "idle";
        }
    }

    /// <summary>
    /// Parses <c>/ohho/telemetry</c> and <c>/ohho/safety/state</c>.
    /// <c>age_ms</c> is stripped before the generated schema parse because
    /// ohho.telemetry.v1 rejects unknown members. The ages are kept for the HUD.
    /// </summary>
    public static class TelemetryIngest
    {
        public static bool TryParseTelemetry(string messageJson, FieldAges ages, out TelemetryV1 sample)
        {
            sample = null;
            if (ages == null) return false;
            ages.Clear();
            string body = Unwrap(messageJson);
            if (body == null) return false;
            JObject obj;
            try { obj = JObject.Parse(body); }
            catch (JsonException) { return false; }
            JToken age = obj["age_ms"];
            if (age != null) obj.Remove("age_ms");
            FieldAges.Read(age, ages);
            try
            {
                sample = TelemetryV1.Parse(obj.ToString(Formatting.None));
                return sample != null;
            }
            catch (Exception)
            {
                sample = null;
                return false;
            }
        }

        public static bool TryParseSafety(string messageJson, out SafetyReading reading)
        {
            reading = default;
            string body = Unwrap(messageJson);
            if (body == null) return false;
            JObject obj;
            try { obj = JObject.Parse(body); }
            catch (JsonException) { return false; }

            if (obj["estop"] is JObject && obj["deadman"] != null)
            {
                try
                {
                    if (obj["age_ms"] != null) obj.Remove("age_ms");
                    SafetyState state = SafetyState.Parse(obj.ToString(Formatting.None));
                    reading.Measured = true;
                    reading.Deadman = state.Deadman;
                    if (state.Estop != null)
                    {
                        reading.Active = state.Estop.Active;
                        reading.Latched = state.Estop.Latched;
                        reading.Source = state.Estop.Source;
                    }
                    if (state.Mode != null)
                    {
                        reading.HasMode = true;
                        reading.BaseMode = state.Mode.Base;
                        reading.ArmMode = state.Mode.Arm;
                    }
                    if (state.Link != null)
                    {
                        reading.HasLink = true;
                        reading.RttMs = state.Link.RttMs;
                        reading.Hz = state.Link.Hz;
                    }
                    return true;
                }
                catch (Exception)
                {
                    reading = default;
                    return false;
                }
            }

            if (obj["armed"] != null || obj["latched"] != null)
            {
                reading.Measured = true;
                reading.Deadman = IsJsonTrue(obj["armed"]);
                reading.Latched = IsJsonTrue(obj["latched"]);
                reading.Active = false;
                string reason = obj["reason"] != null && obj["reason"].Type == JTokenType.String
                    ? obj["reason"].Value<string>()
                    : null;
                reading.Source = reading.Latched ? MapSource(reason) : EstopSource.None;
                return true;
            }
            return false;
        }

        public static bool TryParseEcho(string messageJson, out HeartbeatEcho echo)
        {
            echo = null;
            string body = Unwrap(messageJson);
            if (body == null) return false;
            try
            {
                echo = HeartbeatEcho.Parse(body);
                return echo != null;
            }
            catch (Exception)
            {
                echo = null;
                return false;
            }
        }

        public static string Unwrap(string messageJson)
        {
            if (string.IsNullOrEmpty(messageJson)) return null;
            int i = 0;
            while (i < messageJson.Length && char.IsWhiteSpace(messageJson[i])) i++;
            if (i >= messageJson.Length || messageJson[i] != '{') return null;
            JObject obj;
            try { obj = JObject.Parse(messageJson); }
            catch (JsonException) { return null; }
            JToken data = obj["data"];
            bool wrapper = data != null && data.Type == JTokenType.String
                && obj["v"] == null && obj["robot_id"] == null && obj["estop"] == null && obj["armed"] == null;
            if (!wrapper) return messageJson;
            return data.Value<string>();
        }

        static bool IsJsonTrue(JToken token)
        {
            return token != null && token.Type == JTokenType.Boolean && token.Value<bool>();
        }

        static EstopSource MapSource(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return EstopSource.Software;
            if (reason.IndexOf("hardware", StringComparison.OrdinalIgnoreCase) >= 0) return EstopSource.Hardware;
            if (reason.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0) return EstopSource.Remote;
            if (reason.IndexOf("watchdog", StringComparison.OrdinalIgnoreCase) >= 0) return EstopSource.Watchdog;
            if (reason.IndexOf("software", StringComparison.OrdinalIgnoreCase) >= 0) return EstopSource.Software;
            return EstopSource.Software;
        }
    }
}
