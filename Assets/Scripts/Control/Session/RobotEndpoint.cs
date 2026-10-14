using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmniBot.VR.Control.Session
{
    /// <summary>Simulation versus a physical robot. Real keeps the deadman on.</summary>
    public enum SessionKind
    {
        Sim,
        Real,
    }

    /// <summary>Where the rosbridge (or the offline demo) lives.</summary>
    public enum SessionTarget
    {
        LocalDesktop,
        Cloud,
        InHeadset,
        ManualIp,
        Announced,
    }

    /// <summary>
    /// One drive target. Mirrors the headset contract
    /// <c>RobotEndpoint { rosbridgeUrl, videoUrl(s), webrtcSignalUrl, authToken, kind }</c>.
    /// </summary>
    public sealed class RobotEndpoint
    {
        public string RobotId;
        public string SessionId;
        public string Label;
        public string RosbridgeUrl;
        public List<string> VideoUrls = new List<string>();
        public string WebrtcSignalUrl;
        public string AuthToken;
        public SessionKind Kind;
        public SessionTarget Target;
        public bool Offline;
        public string Host;
        public int Port;
        /// <summary>manual, registry, cloud, in-headset, announcement, or saved.</summary>
        public string Source;

        public RobotEndpoint Clone()
        {
            return new RobotEndpoint
            {
                RobotId = RobotId,
                SessionId = SessionId,
                Label = Label,
                RosbridgeUrl = RosbridgeUrl,
                VideoUrls = VideoUrls != null ? new List<string>(VideoUrls) : new List<string>(),
                WebrtcSignalUrl = WebrtcSignalUrl,
                AuthToken = AuthToken,
                Kind = Kind,
                Target = Target,
                Offline = Offline,
                Host = Host,
                Port = Port,
                Source = Source,
            };
        }
    }

    /// <summary>
    /// Builds endpoints from a typed IP, a ws/wss URL, or the in-headset offline demo.
    /// Manual IP is the path that works before a session registry or mDNS announcer exists.
    /// </summary>
    public static class EndpointAddresses
    {
        public const string DefaultIp = "192.168.1.101";
        public const int RosbridgePort = 9090;
        public const int VideoPort = 8080;

        public static RobotEndpoint InHeadset(string robotId)
        {
            return new RobotEndpoint
            {
                RobotId = robotId,
                Label = "In-headset (offline)",
                RosbridgeUrl = "",
                VideoUrls = new List<string>(),
                Kind = SessionKind.Sim,
                Target = SessionTarget.InHeadset,
                Offline = true,
                Source = "in-headset",
                Host = "",
                Port = 0,
            };
        }

        /// <summary>
        /// Accepts an IPv4 address (port separate) or a full <c>ws://</c> / <c>wss://</c> URL.
        /// A single <c>host:port</c> in the address field overrides <paramref name="port"/>.
        /// </summary>
        public static bool TryManual(string robotId, string ipOrUrl, int port, SessionKind kind, SessionTarget target,
            out RobotEndpoint endpoint, out string error)
        {
            endpoint = null;
            error = null;
            if (string.IsNullOrWhiteSpace(ipOrUrl))
            {
                error = "Enter an IP address or rosbridge URL.";
                return false;
            }

            var text = ipOrUrl.Trim();
            if (text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                return TryRosbridge(robotId, text, kind, target, out endpoint, out error);

            if (text.IndexOf(':') >= 0 && text.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                var split = text.Split(':');
                if (split.Length != 2 || !int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out port))
                {
                    error = "Port must be a number between 1 and 65535.";
                    return false;
                }
                text = split[0];
            }

            if (!IsIpv4(text, out error)) return false;
            if (port < 1 || port > 65535)
            {
                error = "Port must be a number between 1 and 65535.";
                return false;
            }

            endpoint = new RobotEndpoint
            {
                RobotId = robotId,
                Label = text + ":" + port.ToString(CultureInfo.InvariantCulture),
                RosbridgeUrl = "ws://" + text + ":" + port.ToString(CultureInfo.InvariantCulture),
                Host = text,
                Port = port,
                Kind = kind,
                Target = target,
                Offline = false,
                Source = "manual",
                VideoUrls = new List<string>(),
            };
            return true;
        }

        public static bool TryRosbridge(string robotId, string url, SessionKind kind, SessionTarget target,
            out RobotEndpoint endpoint, out string error)
        {
            endpoint = null;
            error = null;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                error = "Rosbridge URL must start with ws:// or wss://.";
                return false;
            }

            int port = uri.Port;
            if (port <= 0)
                port = uri.Scheme == "wss" ? 443 : 80;
            if (port < 1 || port > 65535)
            {
                error = "Port must be a number between 1 and 65535.";
                return false;
            }

            endpoint = new RobotEndpoint
            {
                RobotId = robotId,
                Label = uri.Host + ":" + port.ToString(CultureInfo.InvariantCulture),
                RosbridgeUrl = url.Trim(),
                Host = uri.Host,
                Port = port,
                Kind = kind,
                Target = target,
                Offline = false,
                Source = "manual",
                VideoUrls = new List<string>(),
            };
            return true;
        }

        /// <summary>
        /// MJPEG URLs for <c>web_video_server</c> on port 8080. Topic encoding matches
        /// <c>MjpegVideoSource</c> (slashes stay unescaped).
        /// </summary>
        public static List<string> VideoUrlsFor(string host, IEnumerable<string> topics)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(host) || topics == null) return list;
            foreach (var topic in topics)
            {
                if (string.IsNullOrEmpty(topic)) continue;
                list.Add("http://" + host + ":" + VideoPort.ToString(CultureInfo.InvariantCulture)
                    + "/stream?topic=" + Uri.EscapeUriString(topic));
            }
            return list;
        }

        public static bool IsIpv4(string ip, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ip))
            {
                error = "Invalid IP address format.";
                return false;
            }
            var parts = ip.Split('.');
            if (parts.Length != 4)
            {
                error = "Invalid IP address format.";
                return false;
            }
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int val) ||
                    val < 0 || val > 255)
                {
                    error = "IP address octets must be 0–255.";
                    return false;
                }
            }
            return true;
        }
    }
}
