using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Control.Session;

namespace OmniBot.VR.Control.Preflight
{
    /// <summary>
    /// What the checklist asked the rosbridge probe for. Implementations used in
    /// edit-mode tests answer immediately. The headset panel fills one after the
    /// socket is up, then <see cref="ConnectionPreflight.Run"/> scores it.
    /// </summary>
    public interface IPreflightLink
    {
        bool IsConnected { get; }

        /// <summary>Latest JSON payload for a subscribed topic, or null.</summary>
        string Message(string topic);

        /// <summary>Rosbridge service values JSON. False when the call failed.</summary>
        bool TryService(string service, out string valuesJson, out string error);

        void ReadVideo(out VideoProbeState state, out string detail);

        /// <summary>Client-measured round trip, in milliseconds.</summary>
        bool TryRtt(out double rttMs, out string detail);
    }

    public enum VideoProbeState
    {
        NotProbed,
        Reachable,
        Unreachable,
    }

    public enum PreflightStatus
    {
        Pass,
        Warn,
        Fail,
    }

    public static class PreflightCheckId
    {
        public const string Handshake = "handshake";
        public const string Descriptor = "descriptor";
        public const string Topics = "topics";
        public const string Safety = "safety";
        public const string Video = "video";
        public const string Rtt = "rtt";
    }

    /// <summary>
    /// Topic and service names for OHH-218. Mux outputs are the ones the headset
    /// does not advertise itself, so a sim without cmd_vel_mux / arm_cmd_mux
    /// cannot look healthy just because this client connected.
    /// </summary>
    public static class PreflightTopics
    {
        public const string Descriptor = "/ohho/descriptor";
        public const string SafetyState = "/ohho/safety/state";
        public const string TopicsService = "/rosapi/topics";
        public const string TimeService = "/rosapi/get_time";
        public const string BaseMuxOutput = "/cmd_vel/out";
        public const string ArmMuxOutput = "/arm/joint_commands/out";
        public const string StringType = "std_msgs/String";
    }

    public sealed class PreflightRequest
    {
        public SessionKind Kind = SessionKind.Real;
        public string RosbridgeUrl;
    }

    public sealed class PreflightCheck
    {
        public readonly string Id;
        public readonly string Title;
        public readonly PreflightStatus Status;
        public readonly bool Safety;
        public readonly string Detail;

        public PreflightCheck(string id, string title, PreflightStatus status, bool safety, string detail)
        {
            Id = id;
            Title = title;
            Status = status;
            Safety = safety;
            Detail = detail ?? "";
        }
    }

    public sealed class PreflightReport
    {
        public readonly SessionKind Kind;
        public readonly IReadOnlyList<PreflightCheck> Checks;
        public readonly bool BlocksTeleop;
        public readonly string Summary;
        public readonly string Diagnostics;

        private PreflightReport(SessionKind kind, IReadOnlyList<PreflightCheck> checks, bool blocksTeleop, string summary, string diagnostics)
        {
            Kind = kind;
            Checks = checks;
            BlocksTeleop = blocksTeleop;
            Summary = summary;
            Diagnostics = diagnostics;
        }

        public bool HasFailure
        {
            get
            {
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].Status == PreflightStatus.Fail) return true;
                return false;
            }
        }

        public bool HasWarning
        {
            get
            {
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].Status == PreflightStatus.Warn) return true;
                return false;
            }
        }

        public PreflightCheck Find(string id)
        {
            for (int i = 0; i < Checks.Count; i++)
                if (Checks[i].Id == id) return Checks[i];
            return null;
        }

        internal static PreflightReport Build(PreflightRequest request, List<PreflightCheck> checks)
        {
            var kind = request != null ? request.Kind : SessionKind.Real;
            bool blocks = kind == SessionKind.Real && AnySafetyFailure(checks);
            string summary = Summarize(kind, blocks, checks);
            string diagnostics = Format(request, kind, blocks, summary, checks);
            return new PreflightReport(kind, checks, blocks, summary, diagnostics);
        }

        private static bool AnySafetyFailure(List<PreflightCheck> checks)
        {
            for (int i = 0; i < checks.Count; i++)
            {
                var check = checks[i];
                if (check.Safety && check.Status == PreflightStatus.Fail) return true;
            }
            return false;
        }

        private static string Summarize(SessionKind kind, bool blocks, List<PreflightCheck> checks)
        {
            var failed = new StringBuilder();
            for (int i = 0; i < checks.Count; i++)
            {
                if (checks[i].Status != PreflightStatus.Fail) continue;
                if (failed.Length > 0) failed.Append(' ');
                failed.Append(checks[i].Detail);
            }

            if (blocks)
                return "Teleop blocked. " + failed;
            if (failed.Length > 0 && kind == SessionKind.Sim)
                return "Preflight failed. Simulation is not blocked. " + failed;
            if (failed.Length > 0)
                return "Preflight failed. " + failed.ToString();
            for (int i = 0; i < checks.Count; i++)
            {
                if (checks[i].Status == PreflightStatus.Warn)
                    return "Preflight passed with warnings.";
            }
            return "Preflight passed.";
        }

        private static string Format(PreflightRequest request, SessionKind kind, bool blocks, string summary, List<PreflightCheck> checks)
        {
            var sb = new StringBuilder();
            sb.Append("OhhO connection preflight\n");
            sb.Append("session: ").Append(kind == SessionKind.Real ? "real" : "sim").Append('\n');
            if (request != null && !string.IsNullOrEmpty(request.RosbridgeUrl))
                sb.Append("rosbridge: ").Append(request.RosbridgeUrl).Append('\n');
            sb.Append("teleop: ").Append(blocks ? "blocked" : "allowed").Append('\n');
            sb.Append(summary).Append('\n');
            for (int i = 0; i < checks.Count; i++)
            {
                var check = checks[i];
                sb.Append('[').Append(Word(check.Status)).Append("] ")
                    .Append(check.Id).Append(" — ").Append(check.Detail).Append('\n');
            }
            return sb.ToString();
        }

        private static string Word(PreflightStatus status)
        {
            switch (status)
            {
                case PreflightStatus.Pass: return "pass";
                case PreflightStatus.Warn: return "warn";
                default: return "fail";
            }
        }
    }

    /// <summary>
    /// Scores a connected robot before teleop. No MonoBehaviour and no socket:
    /// the probe is <see cref="IPreflightLink"/>. Real sessions are blocked when
    /// a safety check fails (handshake, descriptor / protocolVersion, mux topics,
    /// e-stop). Simulation records the same failure and does not block. Video and
    /// RTT never block. This does not change deadman, e-stop latch, or speed caps.
    /// </summary>
    public static class ConnectionPreflight
    {
        /// <summary>ohho-sdk PINNED_PROTOCOL_VERSION on ohh-214-robot-descriptor.</summary>
        public const string PinnedProtocolVersion = "1.0.0";

        /// <summary>ohho.descriptor.v1 versionPattern. Patch may move; 1.1 and 2.0 may not.</summary>
        public const string ProtocolPattern = "^1\\.0\\.(0|[1-9]\\d*)$";

        private static readonly Regex ProtocolRegex = new Regex(ProtocolPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] RequiredDescriptorKeys =
        {
            "v", "id", "model", "drive", "arms", "topics", "cameras", "urdf_url", "apriltags", "telemetry", "safety",
        };

        // OmniBot ceilings from ohho.descriptor.v1. A checklist failure here does
        // not write a new cap into the drive profile.
        private static readonly (string Key, double Ceiling)[] HardCeilings =
        {
            ("max_vx_mps", 0.2),
            ("max_vy_mps", 0.2),
            ("max_wz_rad_s", 1.0),
            ("max_accel_x_mps2", 0.5),
            ("max_accel_y_mps2", 0.5),
            ("max_accel_wz_rad_s2", 2.0),
            ("max_joint_step_rad", 0.15),
        };

        public static PreflightReport Run(IPreflightLink link, PreflightRequest request)
        {
            if (request == null) request = new PreflightRequest();
            if (link == null) link = NullLink.Instance;

            var checks = new List<PreflightCheck>(6);
            JObject descriptor = null;
            checks.Add(CheckHandshake(link));
            checks.Add(CheckDescriptor(link, out descriptor));
            checks.Add(CheckTopics(link, descriptor));
            checks.Add(CheckSafety(link));
            checks.Add(CheckVideo(link, descriptor));
            checks.Add(CheckRtt(link));
            return PreflightReport.Build(request, checks);
        }

        public static PreflightReport Failed(PreflightRequest request, string reason)
        {
            if (request == null) request = new PreflightRequest();
            var checks = new List<PreflightCheck>
            {
                new PreflightCheck(
                    PreflightCheckId.Handshake,
                    "Rosbridge",
                    PreflightStatus.Fail,
                    true,
                    "Rosbridge handshake failed. " + (string.IsNullOrEmpty(reason) ? "The probe threw." : reason)),
            };
            return PreflightReport.Build(request, checks);
        }

        private static PreflightCheck CheckHandshake(IPreflightLink link)
        {
            bool open = false;
            try { open = link.IsConnected; }
            catch (Exception ex)
            {
                return Fail(PreflightCheckId.Handshake, "Rosbridge", true,
                    "Rosbridge handshake failed. " + ex.Message);
            }

            if (!open)
                return Fail(PreflightCheckId.Handshake, "Rosbridge", true,
                    "Rosbridge handshake failed. The WebSocket is not open.");
            return Pass(PreflightCheckId.Handshake, "Rosbridge", true, "Rosbridge WebSocket is open.");
        }

        private static PreflightCheck CheckDescriptor(IPreflightLink link, out JObject descriptor)
        {
            descriptor = null;
            string raw;
            try { raw = link.Message(PreflightTopics.Descriptor); }
            catch (Exception ex)
            {
                return Fail(PreflightCheckId.Descriptor, "Descriptor", true,
                    "No latched /ohho/descriptor message. " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(raw))
                return Fail(PreflightCheckId.Descriptor, "Descriptor", true,
                    "No latched /ohho/descriptor message.");

            JObject doc;
            try { doc = UnwrapObject(raw); }
            catch (Exception)
            {
                return Fail(PreflightCheckId.Descriptor, "Descriptor", true,
                    "Descriptor on /ohho/descriptor is not valid JSON.");
            }

            if (doc == null)
                return Fail(PreflightCheckId.Descriptor, "Descriptor", true,
                    "Descriptor on /ohho/descriptor is not valid JSON.");

            var problems = new List<string>();
            string version = AdvertisedProtocol(doc, problems);
            if (version == null)
                problems.Add("protocolVersion is missing. Headset supports 1.0.x.");
            else if (!ProtocolRegex.IsMatch(version))
                problems.Add("protocolVersion " + version + " is not compatible with this headset (supported 1.0.x).");

            var missing = new List<string>();
            for (int i = 0; i < RequiredDescriptorKeys.Length; i++)
            {
                if (doc[RequiredDescriptorKeys[i]] == null)
                    missing.Add(RequiredDescriptorKeys[i]);
            }
            if (missing.Count > 0)
                problems.Add("Descriptor is missing " + string.Join(", ", missing) + ".");

            AppendHardCapProblems(doc, problems);

            if (problems.Count > 0)
                return Fail(PreflightCheckId.Descriptor, "Descriptor", true, string.Join(" ", problems));

            descriptor = doc;
            string id = doc.Value<string>("id");
            string who = string.IsNullOrEmpty(id) ? "robot" : id;
            return Pass(PreflightCheckId.Descriptor, "Descriptor", true,
                "protocolVersion " + version + " is compatible (" + who + ").");
        }

        private static string AdvertisedProtocol(JObject doc, List<string> problems)
        {
            var explicitVersion = doc["protocolVersion"];
            if (explicitVersion != null && explicitVersion.Type != JTokenType.String)
            {
                problems.Add("protocolVersion is not a string.");
                return null;
            }

            string protocol = explicitVersion != null ? explicitVersion.Value<string>() : null;
            string schema = doc["v"] != null && doc["v"].Type == JTokenType.String ? doc.Value<string>("v") : null;
            if (!string.IsNullOrEmpty(protocol)) protocol = protocol.Trim();
            if (!string.IsNullOrEmpty(schema)) schema = schema.Trim();

            if (!string.IsNullOrEmpty(schema) && !ProtocolRegex.IsMatch(schema))
                problems.Add("Descriptor v " + schema + " is not ohho.descriptor.v1 (supported 1.0.x).");

            if (!string.IsNullOrEmpty(protocol)) return protocol;
            return schema;
        }

        private static void AppendHardCapProblems(JObject doc, List<string> problems)
        {
            var hard = doc["safety"]?["hard"] as JObject;
            if (hard == null)
            {
                if (doc["safety"] != null)
                    problems.Add("Descriptor safety.hard caps are missing.");
                return;
            }

            for (int i = 0; i < HardCeilings.Length; i++)
            {
                var entry = HardCeilings[i];
                var token = hard[entry.Key];
                if (token == null || token.Type == JTokenType.Boolean ||
                    (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                {
                    problems.Add("Descriptor safety.hard." + entry.Key + " is missing.");
                    continue;
                }

                double value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                {
                    problems.Add("Descriptor safety.hard." + entry.Key + " is not a usable cap.");
                    continue;
                }

                if (value > entry.Ceiling + 1e-4)
                {
                    problems.Add("safety.hard." + entry.Key + " " +
                        value.ToString("0.###", CultureInfo.InvariantCulture) +
                        " exceeds the ceiling " +
                        entry.Ceiling.ToString("0.###", CultureInfo.InvariantCulture) +
                        ". The headset will not raise that cap.");
                }
            }
        }

        private static PreflightCheck CheckTopics(IPreflightLink link, JObject descriptor)
        {
            string values;
            string error;
            bool ok;
            try { ok = link.TryService(PreflightTopics.TopicsService, out values, out error); }
            catch (Exception ex)
            {
                return Fail(PreflightCheckId.Topics, "Topics", true,
                    "rosapi/topics did not answer, so the arm mux could not be checked. " + ex.Message);
            }

            if (!ok)
            {
                string why = string.IsNullOrEmpty(error) ? "rosapi/topics did not answer." : error;
                return Fail(PreflightCheckId.Topics, "Topics", true,
                    "rosapi/topics did not answer, so the arm mux could not be checked. " + why);
            }

            List<string> advertised;
            try { advertised = ReadTopicNames(values); }
            catch (Exception)
            {
                return Fail(PreflightCheckId.Topics, "Topics", true,
                    "rosapi/topics response was not valid JSON, so the arm mux could not be checked.");
            }

            bool expectArm = ExpectArm(descriptor);
            var required = RequiredTopics(descriptor, expectArm);
            var missing = new List<string>();
            for (int i = 0; i < required.Count; i++)
            {
                if (!ContainsTopic(advertised, required[i]))
                    missing.Add(required[i]);
            }

            if (missing.Count == 0)
                return Pass(PreflightCheckId.Topics, "Topics", true,
                    "Required topics are advertised, including the mux outputs.");

            return Fail(PreflightCheckId.Topics, "Topics", true, TopicsDetail(missing));
        }

        private static string TopicsDetail(List<string> missing)
        {
            bool arm = ContainsTopic(missing, PreflightTopics.ArmMuxOutput);
            bool baseMux = ContainsTopic(missing, PreflightTopics.BaseMuxOutput);
            string lead = arm
                ? "Arm mux is not running."
                : baseMux
                    ? "Base mux is not running."
                    : "Required topics are not advertised.";
            return lead + " Missing " + string.Join(", ", missing) +
                   ". A sim without the mux ignores those commands.";
        }

        private static bool ExpectArm(JObject descriptor)
        {
            if (descriptor == null) return true;
            var arms = descriptor["arms"] as JArray;
            if (arms == null) return true;
            return arms.Count > 0;
        }

        private static List<string> RequiredTopics(JObject descriptor, bool expectArm)
        {
            var required = new List<string> { PreflightTopics.BaseMuxOutput };
            if (expectArm) required.Add(PreflightTopics.ArmMuxOutput);

            var cmd = descriptor?["topics"]?["cmd"];
            if (cmd != null && cmd.Type == JTokenType.String)
            {
                string name = cmd.Value<string>();
                if (!string.IsNullOrEmpty(name) && !ContainsTopic(required, name))
                    required.Add(name);
            }
            return required;
        }

        private static List<string> ReadTopicNames(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            var root = JObject.Parse(json);
            var arr = root["topics"] as JArray;
            if (arr == null) return list;
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null && arr[i].Type == JTokenType.String)
                {
                    string name = arr[i].Value<string>();
                    if (!string.IsNullOrEmpty(name)) list.Add(name);
                }
            }
            return list;
        }

        private static bool ContainsTopic(List<string> topics, string name)
        {
            for (int i = 0; i < topics.Count; i++)
                if (string.Equals(topics[i], name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static PreflightCheck CheckSafety(IPreflightLink link)
        {
            string raw;
            try { raw = link.Message(PreflightTopics.SafetyState); }
            catch (Exception ex)
            {
                return Fail(PreflightCheckId.Safety, "Safety", true,
                    "No message on /ohho/safety/state. " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(raw))
                return Fail(PreflightCheckId.Safety, "Safety", true,
                    "No message on /ohho/safety/state. A missing safety state is not a permit.");

            JObject doc;
            try { doc = UnwrapObject(raw); }
            catch (Exception)
            {
                return Fail(PreflightCheckId.Safety, "Safety", true,
                    "Safety state on /ohho/safety/state is not valid JSON. Missing safety fields are not a permit.");
            }

            if (doc == null)
                return Fail(PreflightCheckId.Safety, "Safety", true,
                    "Safety state on /ohho/safety/state is not valid JSON. Missing safety fields are not a permit.");

            var problems = new List<string>();
            var estop = doc["estop"] as JObject;
            if (estop == null)
            {
                problems.Add("Safety state has no estop object. Missing safety fields are not a permit.");
            }
            else
            {
                bool activeKnown = TryBool(estop["active"], out bool active);
                bool latchedKnown = TryBool(estop["latched"], out bool latched);
                string source = estop["source"] != null && estop["source"].Type == JTokenType.String
                    ? estop.Value<string>("source")
                    : "";
                if (!activeKnown || !latchedKnown)
                {
                    problems.Add("Safety state estop is incomplete. Missing safety fields are not a permit.");
                }
                else if (latched)
                {
                    problems.Add("E-stop is latched (source: " + (string.IsNullOrEmpty(source) ? "unspecified" : source) +
                                 "). Safety state is not clear.");
                }
                else if (active)
                {
                    problems.Add("E-stop is active (source: " + (string.IsNullOrEmpty(source) ? "unspecified" : source) +
                                 "). Safety state is not clear.");
                }
                else if (source != "none")
                {
                    problems.Add("E-stop source is not clear" +
                                 (string.IsNullOrEmpty(source) ? "." : " (source: " + source + ")."));
                }
            }

            if (!TryBool(doc["deadman"], out _))
                problems.Add("Safety state is missing deadman. A missing deadman is not a permit.");

            var mode = doc["mode"] as JObject;
            if (mode != null)
            {
                string baseMode = mode.Value<string>("base");
                string armMode = mode.Value<string>("arm");
                if (baseMode == "fault" || armMode == "fault")
                    problems.Add("Safety mode is fault. That subsystem must not be commanded.");
            }

            if (problems.Count > 0)
                return Fail(PreflightCheckId.Safety, "Safety", true, string.Join(" ", problems));
            return Pass(PreflightCheckId.Safety, "Safety", true, "E-stop is clear.");
        }

        private static PreflightCheck CheckVideo(IPreflightLink link, JObject descriptor)
        {
            VideoProbeState state;
            string detail;
            try { link.ReadVideo(out state, out detail); }
            catch (Exception ex)
            {
                return Fail(PreflightCheckId.Video, "Video", false,
                    "Video is not reachable. " + ex.Message);
            }

            if (string.IsNullOrEmpty(detail)) detail = "no URL";
            bool cameras = HasCameras(descriptor);

            if (state == VideoProbeState.Reachable)
                return Pass(PreflightCheckId.Video, "Video", false, "Video is reachable at " + detail + ".");
            if (state == VideoProbeState.Unreachable)
                return Fail(PreflightCheckId.Video, "Video", false, "Video is not reachable at " + detail + ".");
            if (!cameras && descriptor != null)
                return Pass(PreflightCheckId.Video, "Video", false, "No camera is listed on this robot.");
            return Fail(PreflightCheckId.Video, "Video", false,
                "Video is not reachable. No video URL was probed.");
        }

        private static bool HasCameras(JObject descriptor)
        {
            var cameras = descriptor?["cameras"] as JArray;
            if (cameras != null && cameras.Count > 0) return true;
            var images = descriptor?["topics"]?["images"] as JArray;
            return images != null && images.Count > 0;
        }

        private static PreflightCheck CheckRtt(IPreflightLink link)
        {
            bool ok;
            double rtt;
            string detail;
            try { ok = link.TryRtt(out rtt, out detail); }
            catch (Exception ex)
            {
                return new PreflightCheck(PreflightCheckId.Rtt, "RTT", PreflightStatus.Warn, false,
                    "RTT was not measured. " + ex.Message);
            }

            if (!ok || double.IsNaN(rtt) || double.IsInfinity(rtt) || rtt < 0)
            {
                string why = string.IsNullOrEmpty(detail) ? "rosapi/get_time did not answer." : detail;
                return new PreflightCheck(PreflightCheckId.Rtt, "RTT", PreflightStatus.Warn, false,
                    "RTT was not measured. " + why);
            }

            string ms = Math.Round(rtt).ToString("0", CultureInfo.InvariantCulture);
            return Pass(PreflightCheckId.Rtt, "RTT", false, "RTT " + ms + " ms.");
        }

        private static JObject UnwrapObject(string raw)
        {
            var token = JToken.Parse(raw);
            var obj = token as JObject;
            if (obj == null) return null;
            var data = obj["data"];
            if (data != null && data.Type == JTokenType.String)
            {
                string inner = data.Value<string>();
                if (!string.IsNullOrWhiteSpace(inner))
                {
                    var innerToken = JToken.Parse(inner);
                    if (innerToken is JObject innerObj) return innerObj;
                }
            }
            return obj;
        }

        private static bool TryBool(JToken token, out bool value)
        {
            value = false;
            if (token == null || token.Type != JTokenType.Boolean) return false;
            value = token.Value<bool>();
            return true;
        }

        private static PreflightCheck Pass(string id, string title, bool safety, string detail)
        {
            return new PreflightCheck(id, title, PreflightStatus.Pass, safety, detail);
        }

        private static PreflightCheck Fail(string id, string title, bool safety, string detail)
        {
            return new PreflightCheck(id, title, PreflightStatus.Fail, safety, detail);
        }

        private sealed class NullLink : IPreflightLink
        {
            public static readonly NullLink Instance = new NullLink();
            public bool IsConnected => false;
            public string Message(string topic) => null;
            public bool TryService(string service, out string valuesJson, out string error)
            {
                valuesJson = null;
                error = "No rosbridge probe.";
                return false;
            }
            public void ReadVideo(out VideoProbeState state, out string detail)
            {
                state = VideoProbeState.NotProbed;
                detail = null;
            }
            public bool TryRtt(out double rttMs, out string detail)
            {
                rttMs = -1;
                detail = "No rosbridge probe.";
                return false;
            }
        }
    }
}
