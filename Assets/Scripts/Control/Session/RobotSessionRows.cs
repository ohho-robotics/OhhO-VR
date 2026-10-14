using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Control.Session
{
    /// <summary>
    /// Parses session-registry rows and LAN announcements into <see cref="RobotEndpoint"/>s.
    /// The Supabase <c>robot_sessions</c> table and mDNS <c>_ohho._tcp</c> TXT records are
    /// optional; a bad payload is skipped and manual IP still works.
    /// </summary>
    public static class RobotSessionRows
    {
        public static List<RobotEndpoint> Parse(string json, out string error)
        {
            error = null;
            var list = new List<RobotEndpoint>();
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty session list";
                return list;
            }

            JToken token;
            try { token = JToken.Parse(json); }
            catch (Exception ex)
            {
                error = ex.Message;
                return list;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    if (item is JObject row && TryFromRegistryRow(row, out var ep))
                        list.Add(ep);
                }
                return list;
            }

            if (token is JObject single && TryFromRegistryRow(single, out var one))
                list.Add(one);
            else
                error = "not a session list";
            return list;
        }

        public static bool TryFromRegistryRow(JObject row, out RobotEndpoint endpoint)
        {
            endpoint = null;
            if (row == null) return false;

            string targetText = String(row, "target");
            string kindText = String(row, "kind");
            if (!TryKindAndTarget(kindText, targetText, out var kind, out var target))
                return false;

            var videos = ReadVideos(row["video_urls"] ?? row["video_url"] ?? row["video"]);
            string rosbridge = String(row, "rosbridge_url") ?? String(row, "rosbridge");
            if (target == SessionTarget.InHeadset ||
                string.Equals(kindText, "offline", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = EndpointAddresses.InHeadset(String(row, "robot_id"));
                endpoint.SessionId = String(row, "id") ?? "in-headset";
                endpoint.Label = String(row, "label") ?? String(row, "name") ?? endpoint.Label;
                endpoint.Source = "registry";
                endpoint.AuthToken = String(row, "auth_token");
                return true;
            }

            if (string.IsNullOrWhiteSpace(rosbridge)) return false;
            if (!EndpointAddresses.TryRosbridge(String(row, "robot_id"), rosbridge, kind, target, out endpoint, out _))
                return false;

            endpoint.SessionId = String(row, "id") ?? endpoint.RosbridgeUrl;
            endpoint.Kind = kind;
            endpoint.Target = target;
            endpoint.VideoUrls = videos;
            endpoint.WebrtcSignalUrl = String(row, "webrtc_signal_url") ?? String(row, "webrtc");
            endpoint.AuthToken = String(row, "auth_token");
            endpoint.Source = "registry";
            var label = String(row, "label") ?? String(row, "name");
            if (!string.IsNullOrEmpty(label)) endpoint.Label = label;
            // A registry row cannot turn the deadman off. Any such field is ignored.
            return true;
        }

        /// <summary>
        /// TXT body of an <c>_ohho._tcp</c> announcement. <c>kind</c> is required so a
        /// real robot is never filed as a sim because the record was incomplete.
        /// </summary>
        public static bool TryFromAnnouncement(IReadOnlyDictionary<string, string> txt, string robotId,
            out RobotEndpoint endpoint, out string error)
        {
            endpoint = null;
            error = null;
            if (txt == null || txt.Count == 0)
            {
                error = "Announcement is empty.";
                return false;
            }

            string kindText = Txt(txt, "kind");
            string targetText = Txt(txt, "target");
            if (string.IsNullOrEmpty(kindText))
            {
                error = "Announcement has no sim/real kind.";
                return false;
            }
            if (!TryKindAndTarget(kindText, targetText, out var kind, out var target))
            {
                error = "Announcement kind is not sim or real.";
                return false;
            }

            if (target == SessionTarget.InHeadset)
            {
                endpoint = EndpointAddresses.InHeadset(robotId);
                endpoint.Source = "announcement";
                var name = Txt(txt, "name");
                if (!string.IsNullOrEmpty(name)) endpoint.Label = name;
                return true;
            }

            string rosbridge = Txt(txt, "rosbridge") ?? Txt(txt, "rosbridge_url");
            if (!EndpointAddresses.TryRosbridge(robotId, rosbridge, kind, target, out endpoint, out error))
                return false;

            endpoint.Kind = kind;
            endpoint.Target = target == SessionTarget.ManualIp && kind == SessionKind.Real
                ? SessionTarget.Announced
                : target;
            endpoint.Source = "announcement";
            endpoint.WebrtcSignalUrl = Txt(txt, "webrtc") ?? Txt(txt, "webrtc_signal_url");
            endpoint.AuthToken = Txt(txt, "auth_token");
            var video = Txt(txt, "video") ?? Txt(txt, "video_url");
            if (!string.IsNullOrEmpty(video)) endpoint.VideoUrls.Add(video);
            var announcedName = Txt(txt, "name");
            if (!string.IsNullOrEmpty(announcedName)) endpoint.Label = announcedName;
            return true;
        }

        private static bool TryKindAndTarget(string kindText, string targetText, out SessionKind kind, out SessionTarget target)
        {
            kind = SessionKind.Sim;
            target = SessionTarget.LocalDesktop;
            bool kindSet = TryParseKind(kindText, out kind);
            bool targetSet = TryParseTarget(targetText, out target);
            if (!kindSet && !targetSet) return false;
            if (!targetSet)
                target = kind == SessionKind.Real ? SessionTarget.Announced : SessionTarget.LocalDesktop;
            if (!kindSet)
                kind = target == SessionTarget.Announced ? SessionKind.Real : SessionKind.Sim;
            // In-headset is the offline demo. Cloud is a sim target unless the row
            // explicitly says real — those stay real and show up on Drive real.
            if (target == SessionTarget.InHeadset)
                kind = SessionKind.Sim;
            else if (target == SessionTarget.Cloud && !(kindSet && kind == SessionKind.Real))
                kind = SessionKind.Sim;
            return true;
        }

        private static bool TryParseKind(string text, out SessionKind kind)
        {
            kind = SessionKind.Sim;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "sim":
                case "simulation":
                case "simulated":
                    kind = SessionKind.Sim;
                    return true;
                case "real":
                case "hardware":
                case "robot":
                    kind = SessionKind.Real;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseTarget(string text, out SessionTarget target)
        {
            target = SessionTarget.LocalDesktop;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "local":
                case "local-desktop":
                case "local_desktop":
                case "lan":
                case "desktop":
                    target = SessionTarget.LocalDesktop;
                    return true;
                case "cloud":
                    target = SessionTarget.Cloud;
                    return true;
                case "in-headset":
                case "in_headset":
                case "inheadset":
                case "offline":
                    target = SessionTarget.InHeadset;
                    return true;
                case "manual":
                case "manual-ip":
                case "manual_ip":
                    target = SessionTarget.ManualIp;
                    return true;
                case "announced":
                case "announce":
                case "mdns":
                    target = SessionTarget.Announced;
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> ReadVideos(JToken token)
        {
            var list = new List<string>();
            if (token == null || token.Type == JTokenType.Null) return list;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                {
                    var value = item.Type == JTokenType.String ? item.Value<string>() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
                }
                return list;
            }

            var text = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            if (string.IsNullOrWhiteSpace(text)) return list;
            if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                try { return ReadVideos(JToken.Parse(text)); }
                catch { /* treat as a single URL */ }
            }
            foreach (var part in text.Split(new[] { ',', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(part)) list.Add(part.Trim());
            }
            return list;
        }

        private static string String(JObject row, string name)
        {
            var token = row[name];
            if (token == null || token.Type == JTokenType.Null) return null;
            var value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Txt(IReadOnlyDictionary<string, string> txt, string key)
        {
            foreach (var pair in txt)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
            }
            return null;
        }
    }
}
