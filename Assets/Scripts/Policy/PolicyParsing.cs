using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Policy
{
    /// <summary>
    /// Parses serve <c>GET /models</c> (and the ROS param / topic fallback) into
    /// policy descriptors. No Unity and no network.
    /// </summary>
    public static class PolicyCatalog
    {
        /// <summary>
        /// Serve <c>GET /models</c> body. False when the payload is not a JSON
        /// list, so the caller can keep the param fallback instead of treating
        /// an error page as a model id.
        /// </summary>
        public static bool TryParseServe(string raw, out List<PolicyDescriptor> list)
        {
            list = new List<PolicyDescriptor>();
            string text = PolicyText.PeelStringData(raw).Trim();
            if (text.Length == 0 || (text[0] != '{' && text[0] != '[')) return false;
            try
            {
                Collect(JToken.Parse(text), list);
            }
            catch (JsonException)
            {
                list.Clear();
                return false;
            }
            return list.Count > 0;
        }

        public static List<PolicyDescriptor> Parse(string raw)
        {
            var list = new List<PolicyDescriptor>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            string text = PolicyText.PeelStringData(raw).Trim();
            if (text.Length == 0) return list;

            bool json = text[0] == '{' || text[0] == '[';
            if (json)
            {
                try
                {
                    Collect(JToken.Parse(text), list);
                    return list;
                }
                catch (JsonException)
                {
                    // Fall through to a comma-separated param.
                }
            }

            string[] parts = text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim();
                if (id.Length == 0) continue;
                if (id.IndexOf('{') >= 0 || id.IndexOf('}') >= 0 || id.IndexOf('[') >= 0 || id.IndexOf(']') >= 0)
                    continue;
                Add(list, id, id, id);
            }
            return list;
        }

        private static void Collect(JToken token, List<PolicyDescriptor> list)
        {
            if (token == null) return;
            if (token is JArray array)
            {
                for (int i = 0; i < array.Count; i++)
                    Collect(array[i], list);
                return;
            }

            if (token.Type == JTokenType.String)
            {
                Add(list, token.ToString(), token.ToString(), token.ToString());
                return;
            }

            var obj = token as JObject;
            if (obj == null) return;

            JToken nested = obj["models"] ?? obj["policies"] ?? obj["items"];
            if (nested != null)
            {
                if (nested.Type == JTokenType.String)
                {
                    var inner = Parse(nested.ToString());
                    for (int i = 0; i < inner.Count; i++)
                        Add(list, inner[i].Id, inner[i].Label, inner[i].Checkpoint);
                    return;
                }
                Collect(nested, list);
                return;
            }

            if (obj["data"] != null && obj["data"].Type == JTokenType.String
                && obj["id"] == null && obj["name"] == null && obj["checkpoint"] == null)
            {
                string inner = obj["data"].ToString();
                try
                {
                    Collect(JToken.Parse(inner), list);
                    return;
                }
                catch (JsonException)
                {
                    var csv = Parse(inner);
                    for (int i = 0; i < csv.Count; i++)
                        Add(list, csv[i].Id, csv[i].Label, csv[i].Checkpoint);
                    return;
                }
            }

            string id = FirstString(obj, "id", "name", "model", "model_type");
            if (string.IsNullOrEmpty(id)) return;
            string checkpoint = FirstString(obj, "checkpoint", "checkpoint_path", "model_path", "path");
            if (string.IsNullOrEmpty(checkpoint)) checkpoint = id;
            string label = FirstString(obj, "label", "name", "id");
            if (string.IsNullOrEmpty(label)) label = id;
            Add(list, id, label, checkpoint);
        }

        private static void Add(List<PolicyDescriptor> list, string id, string label, string checkpoint)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(new PolicyDescriptor
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? id : label.Trim(),
                Checkpoint = string.IsNullOrWhiteSpace(checkpoint) ? id : checkpoint.Trim(),
            });
        }

        private static string FirstString(JObject obj, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                JToken token = obj[keys[i]];
                if (token != null && token.Type == JTokenType.String)
                {
                    string value = token.ToString().Trim();
                    if (value.Length > 0) return value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Latency and guard trips from <c>/ohho/telemetry</c>, with <c>/ai/status</c>
    /// as the fallback when no telemetry document has arrived.
    /// </summary>
    public static class PolicyStatusParser
    {
        private static readonly string[] BlackKeys =
        {
            "black_frame", "black_frames", "missing_or_black_images",
        };

        private static readonly string[] StaleKeys =
        {
            "stale_inference", "stale",
        };

        private static readonly string[] ClampKeys =
        {
            "clamped_deltas", "clamped_delta", "clamped",
        };

        private static readonly Regex KeyValue = new Regex(
            @"(latency_ms|black_frame|black_frames|stale_inference|clamped_deltas|clamped_delta)\s*[:=]\s*(-?\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParseTelemetry(string raw, out PolicyStatusView view, out PolicySafetyNote safety)
        {
            view = null;
            safety = default;
            if (!TryObject(raw, out JObject obj)) return false;
            if (!LooksLikeTelemetry(obj)) return false;

            var policy = obj["policy"] as JObject;
            view = new PolicyStatusView
            {
                Source = "telemetry",
                PolicyName = FirstString(policy, "name", "id", "task"),
                PolicyState = FirstString(policy, "state"),
                LatencyMs = ReadLatency(policy) ?? ReadLatency(obj),
                Trips = ReadTrips(policy, obj),
                Kind = NormalizeKind(obj["kind"]),
            };
            safety = ReadSafety(obj);
            return true;
        }

        public static bool TryParseAiStatus(string raw, out PolicyStatusView view)
        {
            view = null;
            string text = PolicyText.PeelStringData(raw).Trim();
            if (text.Length == 0) return false;

            double? latency = null;
            var trips = new PolicyGuardTrips();
            string name = null;
            string state = null;

            if (text[0] == '{')
            {
                try
                {
                    var obj = JObject.Parse(text);
                    var policy = obj["policy"] as JObject;
                    latency = ReadLatency(policy) ?? ReadLatency(obj);
                    trips = ReadTrips(policy, obj);
                    name = FirstString(policy, "name", "id") ?? FirstString(obj, "name", "policy_name");
                    state = FirstString(policy, "state") ?? FirstString(obj, "state");
                }
                catch (JsonException)
                {
                    // Plain text is handled below.
                }
            }

            if (latency == null && !trips.HasAny)
                ReadKeyValues(text, ref latency, ref trips);

            if (latency == null && !trips.HasAny) return false;

            view = new PolicyStatusView
            {
                Source = "ai_status",
                PolicyName = name,
                PolicyState = state,
                LatencyMs = latency,
                Trips = trips,
            };
            return true;
        }

        private static bool TryObject(string raw, out JObject obj)
        {
            obj = null;
            string text = PolicyText.PeelStringData(raw).Trim();
            if (text.Length == 0 || text[0] != '{') return false;
            try
            {
                obj = JObject.Parse(text);
                return obj != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool LooksLikeTelemetry(JObject obj)
        {
            return obj["v"] != null || obj["kind"] != null || obj["policy"] != null
                || obj["estop"] != null || obj["deadman"] != null || obj["guards"] != null;
        }

        private static PolicySafetyNote ReadSafety(JObject obj)
        {
            var note = new PolicySafetyNote();
            JToken estop = obj["estop"];
            if (estop != null && estop.Type != JTokenType.Null)
            {
                note.HasEstop = true;
                if (estop.Type == JTokenType.Boolean)
                    note.EstopActive = estop.Value<bool>();
                else if (estop is JObject estopObj)
                    note.EstopActive = BoolTrue(estopObj["active"]) || BoolTrue(estopObj["latched"]);
            }

            JToken deadman = obj["deadman"];
            if (deadman != null && deadman.Type != JTokenType.Null)
            {
                note.HasDeadman = true;
                note.DeadmanActive = BoolTrue(deadman);
            }
            return note;
        }

        private static PolicyGuardTrips ReadTrips(JObject policy, JObject root)
        {
            var trips = new PolicyGuardTrips();
            Apply(policy, ref trips);
            Apply(ChildObject(policy, "guards") ?? ChildObject(policy, "guard_trips"), ref trips);
            Apply(root, ref trips);
            Apply(ChildObject(root, "guards") ?? ChildObject(root, "guard_trips"), ref trips);
            return trips;
        }

        private static void Apply(JObject obj, ref PolicyGuardTrips trips)
        {
            if (obj == null) return;
            TryCount(obj, BlackKeys, ref trips.HasBlackFrame, ref trips.BlackFrame);
            TryCount(obj, StaleKeys, ref trips.HasStaleInference, ref trips.StaleInference);
            TryCount(obj, ClampKeys, ref trips.HasClampedDeltas, ref trips.ClampedDeltas);
        }

        private static void TryCount(JObject obj, string[] keys, ref bool has, ref int count)
        {
            if (has) return;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!TryNumber(obj[keys[i]], out double value)) continue;
                if (value < 0) continue;
                has = true;
                count = (int)value;
                return;
            }
        }

        private static double? ReadLatency(JObject obj)
        {
            if (obj == null) return null;
            if (TryNumber(obj["latency_ms"], out double ms) && ms >= 0) return ms;
            if (TryNumber(obj["latency"], out double latency) && latency >= 0) return latency;
            return null;
        }

        private static void ReadKeyValues(string text, ref double? latency, ref PolicyGuardTrips trips)
        {
            MatchCollection matches = KeyValue.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                string key = matches[i].Groups[1].Value.ToLowerInvariant();
                if (!double.TryParse(matches[i].Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    continue;
                if (value < 0) continue;
                if (key == "latency_ms")
                {
                    if (latency == null) latency = value;
                    continue;
                }
                int count = (int)value;
                if ((key == "black_frame" || key == "black_frames") && !trips.HasBlackFrame)
                {
                    trips.HasBlackFrame = true;
                    trips.BlackFrame = count;
                }
                else if (key == "stale_inference" && !trips.HasStaleInference)
                {
                    trips.HasStaleInference = true;
                    trips.StaleInference = count;
                }
                else if ((key == "clamped_deltas" || key == "clamped_delta") && !trips.HasClampedDeltas)
                {
                    trips.HasClampedDeltas = true;
                    trips.ClampedDeltas = count;
                }
            }
        }

        private static bool TryNumber(JToken token, out double value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return true;
            }
            return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool BoolTrue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<long>() != 0;
            return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKind(JToken token)
        {
            if (token == null || token.Type != JTokenType.String) return null;
            string kind = token.ToString().Trim().ToLowerInvariant();
            if (kind == "sim" || kind == "real") return kind;
            return null;
        }

        private static JObject ChildObject(JObject obj, string key)
        {
            if (obj == null) return null;
            return obj[key] as JObject;
        }

        private static string FirstString(JObject obj, params string[] keys)
        {
            if (obj == null) return null;
            for (int i = 0; i < keys.Length; i++)
            {
                JToken token = obj[keys[i]];
                if (token != null && token.Type == JTokenType.String)
                {
                    string value = token.ToString().Trim();
                    if (value.Length > 0) return value;
                }
            }
            return null;
        }
    }

    public static class PolicyText
    {
        /// <summary>
        /// If <paramref name="raw"/> is a std_msgs/String object, return its data field.
        /// Documents that already look like telemetry or a model list are left intact.
        /// </summary>
        public static string PeelStringData(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = raw.Trim();
            if (text.Length == 0 || text[0] != '{') return text;
            try
            {
                var obj = JObject.Parse(text);
                JToken data = obj["data"];
                if (data == null || data.Type != JTokenType.String) return text;
                if (obj["v"] != null || obj["policy"] != null || obj["kind"] != null
                    || obj["models"] != null || obj["latency_ms"] != null || obj["estop"] != null)
                    return text;
                return data.ToString();
            }
            catch (JsonException)
            {
                return text;
            }
        }
    }
}
