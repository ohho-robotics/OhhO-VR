using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// JSON shapes for the OmniBot <c>ohho_webrtc</c> signaling socket
    /// (OHH-210). The headset is a viewer: <c>join</c>, then <c>offer</c>,
    /// then trickle <c>ice</c>. The server answers with <c>welcome</c>
    /// (the session record, including STUN/TURN), <c>answer</c>, <c>ice</c>,
    /// and <c>error</c>.
    /// </summary>
    public static class OhhoSignaling
    {
        public const string UpstreamPath = "/signaling";
        public const string GatewayPath = "/webrtc/signaling";

        public static string Join(IReadOnlyList<string> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                throw new SignalingFormatException("invalid_tracks");
            return new JObject
            {
                ["type"] = "join",
                ["role"] = "viewer",
                ["tracks"] = new JArray(tracks)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Offer(string sdp, IReadOnlyList<string> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                throw new SignalingFormatException("invalid_track");
            if (string.IsNullOrEmpty(sdp) || !sdp.StartsWith("v=", StringComparison.Ordinal))
                throw new SignalingFormatException("invalid_sdp");
            return new JObject
            {
                ["type"] = "offer",
                ["sdp"] = sdp,
                ["track"] = tracks[0],
                ["tracks"] = new JArray(tracks)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Ice(string track, string candidate, string sdpMid, int sdpMLineIndex)
        {
            if (string.IsNullOrEmpty(track))
                throw new SignalingFormatException("invalid_track");
            return new JObject
            {
                ["type"] = "ice",
                ["track"] = track,
                ["candidate"] = candidate,
                ["sdpMid"] = sdpMid ?? "",
                ["sdpMLineIndex"] = sdpMLineIndex
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Error(string code)
        {
            return new JObject
            {
                ["type"] = "error",
                ["error"] = code
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Welcome(
            string role,
            string viewer,
            IReadOnlyList<WebRtcTrackListing> tracks,
            IReadOnlyList<WebRtcIceServer> iceServers)
        {
            return new JObject
            {
                ["type"] = "welcome",
                ["role"] = role,
                ["viewer"] = viewer,
                ["tracks"] = TrackArray(tracks),
                ["iceServers"] = IceArray(iceServers),
                ["signalingPath"] = UpstreamPath,
                ["gatewayPath"] = GatewayPath
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Answer(string sdp, IReadOnlyList<string> tracks, string viewer)
        {
            return new JObject
            {
                ["type"] = "answer",
                ["sdp"] = sdp,
                ["track"] = tracks[0],
                ["tracks"] = new JArray(tracks),
                ["viewer"] = viewer
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ServerIce(
            string track,
            string candidate,
            string sdpMid,
            int sdpMLineIndex,
            string viewer)
        {
            return new JObject
            {
                ["type"] = "ice",
                ["track"] = track,
                ["candidate"] = candidate,
                ["sdpMid"] = sdpMid,
                ["sdpMLineIndex"] = sdpMLineIndex,
                ["viewer"] = viewer
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Parse a message the headset sends. Mirrors the server parser.</summary>
        public static ClientSignal ParseClient(string payload)
        {
            JObject data = ParseObject(payload);
            string msgType = RequiredType(data, "join", "offer", "answer", "ice", "list");
            string viewer = OptionalString(data, "viewer");
            if (msgType == "list")
                return new ClientSignal { Type = "list", Viewer = viewer };
            if (msgType == "join")
            {
                string role = data.Value<string>("role");
                if (role != "viewer" && role != "publisher")
                    throw new SignalingFormatException("invalid_role");
                return new ClientSignal
                {
                    Type = "join",
                    Role = role,
                    Tracks = RequiredTrackList(data["tracks"]),
                    Viewer = viewer
                };
            }
            if (msgType == "offer" || msgType == "answer")
            {
                string sdp = data.Value<string>("sdp");
                if (string.IsNullOrEmpty(sdp) || !sdp.StartsWith("v=", StringComparison.Ordinal))
                    throw new SignalingFormatException("invalid_sdp");
                var tracks = OfferTracks(data);
                return new ClientSignal
                {
                    Type = msgType,
                    Sdp = sdp,
                    Tracks = tracks,
                    Track = tracks[0],
                    Viewer = viewer
                };
            }
            return ParseIce(data, viewer);
        }

        /// <summary>Parse a message the server sends to a viewer.</summary>
        public static SignalingInbound ParseServer(string payload)
        {
            JObject data = ParseObject(payload);
            string msgType = data.Value<string>("type");
            if (string.IsNullOrEmpty(msgType))
                throw new SignalingFormatException("invalid_message");
            if (msgType == "error")
            {
                string code = data.Value<string>("error");
                if (string.IsNullOrEmpty(code))
                    throw new SignalingFormatException("invalid_message");
                var inbound = SignalingInbound.ErrorOf(code);
                JToken trackToken = data["track"];
                if (trackToken != null && trackToken.Type == JTokenType.String)
                    inbound.Track = trackToken.Value<string>();
                return inbound;
            }
            if (msgType == "welcome")
                return SignalingInbound.WelcomeOf(ReadWelcome(data));
            if (msgType == "answer")
            {
                string sdp = data.Value<string>("sdp");
                if (string.IsNullOrEmpty(sdp) || !sdp.StartsWith("v=", StringComparison.Ordinal))
                    throw new SignalingFormatException("invalid_sdp");
                var tracks = OfferTracks(data);
                return new SignalingInbound
                {
                    Type = "answer",
                    Sdp = sdp,
                    Track = tracks[0],
                    Tracks = tracks,
                    Viewer = OptionalString(data, "viewer")
                };
            }
            if (msgType == "ice")
            {
                var ice = ParseIce(data, OptionalString(data, "viewer"));
                return new SignalingInbound
                {
                    Type = "ice",
                    Track = ice.Track,
                    Candidate = ice.Candidate,
                    SdpMid = ice.SdpMid,
                    SdpMLineIndex = ice.SdpMLineIndex,
                    Viewer = ice.Viewer
                };
            }
            if (msgType == "tracks")
            {
                return new SignalingInbound { Type = "tracks" };
            }
            throw new SignalingFormatException("unknown_type");
        }

        private static WebRtcSessionRecord ReadWelcome(JObject data)
        {
            string role = data.Value<string>("role");
            string viewer = data.Value<string>("viewer");
            if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(viewer))
                throw new SignalingFormatException("incomplete_welcome");
            if (data["tracks"] == null || data["iceServers"] == null
                || data["signalingPath"] == null || data["gatewayPath"] == null)
                throw new SignalingFormatException("incomplete_welcome");
            return new WebRtcSessionRecord
            {
                Role = role,
                ViewerId = viewer,
                Tracks = ReadTrackListings(data["tracks"]),
                IceServers = ReadIceServers(data["iceServers"]),
                SignalingPath = data.Value<string>("signalingPath"),
                GatewayPath = data.Value<string>("gatewayPath")
            };
        }

        public static List<WebRtcIceServer> ReadIceServers(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new List<WebRtcIceServer>();
            if (token.Type != JTokenType.Array)
                throw new SignalingFormatException("invalid_ice");
            var servers = new List<WebRtcIceServer>();
            foreach (var entry in (JArray)token)
            {
                if (entry.Type != JTokenType.Object)
                    throw new SignalingFormatException("invalid_ice");
                var obj = (JObject)entry;
                servers.Add(new WebRtcIceServer(
                    ReadUrls(obj["urls"]),
                    obj.Value<string>("username"),
                    obj.Value<string>("credential")));
            }
            return servers;
        }

        /// <summary>
        /// URLs the peer connection should use. Empty when the session record
        /// lists none — callers must not invent a STUN server.
        /// </summary>
        public static List<string> IceUrls(IReadOnlyList<WebRtcIceServer> servers)
        {
            var urls = new List<string>();
            if (servers == null) return urls;
            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                if (server?.Urls == null) continue;
                for (int u = 0; u < server.Urls.Length; u++)
                {
                    if (!string.IsNullOrEmpty(server.Urls[u]))
                        urls.Add(server.Urls[u]);
                }
            }
            return urls;
        }

        private static List<WebRtcTrackListing> ReadTrackListings(JToken token)
        {
            if (token.Type != JTokenType.Array)
                throw new SignalingFormatException("invalid_tracks");
            var list = new List<WebRtcTrackListing>();
            foreach (var entry in (JArray)token)
            {
                if (entry.Type != JTokenType.Object)
                    throw new SignalingFormatException("invalid_tracks");
                var obj = (JObject)entry;
                string name = obj.Value<string>("name");
                string topic = obj.Value<string>("topic");
                if (!WebRtcTracks.IsName(name) || string.IsNullOrEmpty(topic))
                    throw new SignalingFormatException("invalid_tracks");
                list.Add(new WebRtcTrackListing(name, topic));
            }
            return list;
        }

        private static ClientSignal ParseIce(JObject data, string viewer)
        {
            string track = data.Value<string>("track");
            if (string.IsNullOrEmpty(track))
                throw new SignalingFormatException("invalid_track");
            if (data["candidate"] == null)
                throw new SignalingFormatException("invalid_candidate");
            JToken candidateToken = data["candidate"];
            string candidate = null;
            if (candidateToken.Type == JTokenType.Null)
                candidate = null;
            else if (candidateToken.Type == JTokenType.String)
                candidate = candidateToken.Value<string>();
            else
                throw new SignalingFormatException("invalid_candidate");

            JToken midToken = data["sdpMid"];
            JToken lineToken = data["sdpMLineIndex"];
            string sdpMid = null;
            int? sdpMLine = null;
            if (midToken != null && midToken.Type != JTokenType.Null)
            {
                if (midToken.Type != JTokenType.String)
                    throw new SignalingFormatException("invalid_candidate");
                sdpMid = midToken.Value<string>();
            }
            if (lineToken != null && lineToken.Type != JTokenType.Null)
            {
                if (lineToken.Type != JTokenType.Integer)
                    throw new SignalingFormatException("invalid_candidate");
                sdpMLine = lineToken.Value<int>();
            }
            if (!string.IsNullOrEmpty(candidate))
            {
                if (sdpMid == null || sdpMLine == null)
                    throw new SignalingFormatException("invalid_candidate");
            }
            return new ClientSignal
            {
                Type = "ice",
                Track = track,
                Tracks = new[] { track },
                Candidate = candidate,
                SdpMid = sdpMid,
                SdpMLineIndex = sdpMLine,
                Viewer = viewer
            };
        }

        private static string[] OfferTracks(JObject data)
        {
            string[] tracks = data["tracks"] == null || data["tracks"].Type == JTokenType.Null
                ? new string[0]
                : RequiredTrackList(data["tracks"], required: false);
            JToken trackToken = data["track"];
            string track = null;
            if (trackToken != null && trackToken.Type != JTokenType.Null)
            {
                if (trackToken.Type != JTokenType.String)
                    throw new SignalingFormatException("invalid_track");
                track = trackToken.Value<string>();
            }
            if (tracks.Length > 0 && !string.IsNullOrEmpty(track))
            {
                bool found = false;
                for (int i = 0; i < tracks.Length; i++)
                {
                    if (tracks[i] == track) { found = true; break; }
                }
                if (!found)
                    throw new SignalingFormatException("invalid_track");
            }
            if (tracks.Length > 0) return tracks;
            if (!string.IsNullOrEmpty(track)) return new[] { track };
            throw new SignalingFormatException("invalid_track");
        }

        private static string[] RequiredTrackList(JToken token, bool required = true)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required) throw new SignalingFormatException("invalid_tracks");
                return new string[0];
            }
            if (token.Type != JTokenType.Array)
                throw new SignalingFormatException("invalid_tracks");
            var array = (JArray)token;
            if (required && array.Count == 0)
                throw new SignalingFormatException("invalid_tracks");
            var names = new string[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String || string.IsNullOrEmpty(array[i].Value<string>()))
                    throw new SignalingFormatException("invalid_tracks");
                names[i] = array[i].Value<string>();
            }
            return names;
        }

        private static string[] ReadUrls(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new string[0];
            if (token.Type == JTokenType.String)
            {
                string url = token.Value<string>();
                return string.IsNullOrEmpty(url) ? new string[0] : new[] { url };
            }
            if (token.Type != JTokenType.Array)
                throw new SignalingFormatException("invalid_ice");
            var array = (JArray)token;
            var urls = new List<string>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String)
                    throw new SignalingFormatException("invalid_ice");
                string url = array[i].Value<string>();
                if (!string.IsNullOrEmpty(url)) urls.Add(url);
            }
            return urls.ToArray();
        }

        private static JArray TrackArray(IReadOnlyList<WebRtcTrackListing> tracks)
        {
            var array = new JArray();
            if (tracks == null) return array;
            for (int i = 0; i < tracks.Count; i++)
            {
                array.Add(new JObject
                {
                    ["name"] = tracks[i].Name,
                    ["topic"] = tracks[i].Topic
                });
            }
            return array;
        }

        private static JArray IceArray(IReadOnlyList<WebRtcIceServer> servers)
        {
            var array = new JArray();
            if (servers == null) return array;
            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                var entry = new JObject();
                bool singleBare = server.Urls != null
                    && server.Urls.Length == 1
                    && string.IsNullOrEmpty(server.Username);
                if (singleBare)
                    entry["urls"] = server.Urls[0];
                else
                    entry["urls"] = new JArray(server.Urls ?? new string[0]);
                if (!string.IsNullOrEmpty(server.Username))
                    entry["username"] = server.Username;
                if (!string.IsNullOrEmpty(server.Credential))
                    entry["credential"] = server.Credential;
                array.Add(entry);
            }
            return array;
        }

        private static JObject ParseObject(string payload)
        {
            if (payload == null)
                throw new SignalingFormatException("invalid_json");
            JToken token;
            try { token = JToken.Parse(payload); }
            catch (Newtonsoft.Json.JsonException) { throw new SignalingFormatException("invalid_json"); }
            if (token.Type != JTokenType.Object)
                throw new SignalingFormatException("invalid_message");
            return (JObject)token;
        }

        private static string RequiredType(JObject data, params string[] allowed)
        {
            string msgType = data.Value<string>("type");
            if (string.IsNullOrEmpty(msgType))
                throw new SignalingFormatException("unknown_type");
            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == msgType) return msgType;
            }
            throw new SignalingFormatException("unknown_type");
        }

        private static string OptionalString(JObject data, string key)
        {
            JToken token = data[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
                throw new SignalingFormatException("invalid_" + key);
            return token.Value<string>();
        }
    }

    public sealed class SignalingFormatException : Exception
    {
        public string Code { get; }

        public SignalingFormatException(string code) : base(code)
        {
            Code = code;
        }
    }

    public sealed class WebRtcTrackListing
    {
        public string Name { get; }
        public string Topic { get; }

        public WebRtcTrackListing(string name, string topic)
        {
            Name = name;
            Topic = topic;
        }
    }

    /// <summary>One STUN or TURN entry from the welcome session record.</summary>
    public sealed class WebRtcIceServer
    {
        public string[] Urls { get; }
        public string Username { get; }
        public string Credential { get; }

        public WebRtcIceServer(string[] urls, string username, string credential)
        {
            Urls = urls ?? new string[0];
            Username = username;
            Credential = credential;
        }
    }

    /// <summary>The <c>welcome</c> payload. ICE servers come from here.</summary>
    public sealed class WebRtcSessionRecord
    {
        public string Role;
        public string ViewerId;
        public List<WebRtcTrackListing> Tracks = new List<WebRtcTrackListing>();
        public List<WebRtcIceServer> IceServers = new List<WebRtcIceServer>();
        public string SignalingPath;
        public string GatewayPath;
    }

    public sealed class ClientSignal
    {
        public string Type;
        public string Role;
        public string[] Tracks = new string[0];
        public string Track;
        public string Sdp;
        public string Candidate;
        public string SdpMid;
        public int? SdpMLineIndex;
        public string Viewer;
    }

    public sealed class SignalingInbound
    {
        public string Type;
        public string Error;
        public string Sdp;
        public string Track;
        public string[] Tracks = new string[0];
        public string Candidate;
        public string SdpMid;
        public int? SdpMLineIndex;
        public string Viewer;
        public WebRtcSessionRecord Record;

        public static SignalingInbound ErrorOf(string code)
        {
            return new SignalingInbound { Type = "error", Error = code };
        }

        public static SignalingInbound WelcomeOf(WebRtcSessionRecord record)
        {
            return new SignalingInbound { Type = "welcome", Record = record, Viewer = record.ViewerId };
        }
    }

    /// <summary>Viewer-side state for one ohho_webrtc socket.</summary>
    public sealed class OhhoViewerSession
    {
        private readonly List<string> _requested;

        public WebRtcSessionRecord Record { get; private set; }
        public bool Failed { get; private set; }
        public string Failure { get; private set; }
        public IReadOnlyList<string> RequestedTracks => _requested;

        public OhhoViewerSession(IReadOnlyList<string> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                throw new SignalingFormatException("invalid_tracks");
            _requested = new List<string>(tracks);
        }

        public string CreateJoin()
        {
            return OhhoSignaling.Join(_requested);
        }

        public string CreateOffer(string sdp)
        {
            return OhhoSignaling.Offer(sdp, _requested);
        }

        public string CreateIce(string track, string candidate, string sdpMid, int sdpMLineIndex)
        {
            if (!_requested.Contains(track))
                throw new SignalingFormatException("unknown_track");
            return OhhoSignaling.Ice(track, candidate, sdpMid, sdpMLineIndex);
        }

        public SignalingInbound Accept(string json)
        {
            SignalingInbound inbound;
            try { inbound = OhhoSignaling.ParseServer(json); }
            catch (SignalingFormatException ex)
            {
                MarkFailed(ex.Code);
                return SignalingInbound.ErrorOf(ex.Code);
            }
            if (inbound.Type == "error")
            {
                MarkFailed(inbound.Error);
                return inbound;
            }
            if (inbound.Type == "welcome")
            {
                Record = inbound.Record;
                return inbound;
            }
            if (inbound.Type == "answer" && Record != null
                && !string.IsNullOrEmpty(inbound.Viewer)
                && inbound.Viewer != Record.ViewerId)
            {
                MarkFailed("unknown_viewer");
                return SignalingInbound.ErrorOf("unknown_viewer");
            }
            return inbound;
        }

        private void MarkFailed(string code)
        {
            Failed = true;
            Failure = code;
        }
    }

    public static class WebRtcEndpoints
    {
        public const int DirectPort = 8766;
        public const int GatewayPort = 8443;
        public const string UrlPref = "ohho.webrtc.url";
        public const string TokenPref = "ohho.webrtc.token";

        /// <summary>
        /// Explicit URL wins. A gateway token uses
        /// <c>wss://host:8443/webrtc/signaling</c>. Otherwise the headset
        /// opens the streamer's LAN socket on port 8766.
        /// </summary>
        public static string Resolve(string robotIp, string explicitUrl, string token)
        {
            if (!string.IsNullOrWhiteSpace(explicitUrl))
                return explicitUrl.Trim();
            if (string.IsNullOrWhiteSpace(robotIp))
                robotIp = "127.0.0.1";
            if (!string.IsNullOrWhiteSpace(token))
            {
                return "wss://" + robotIp + ":" + GatewayPort + OhhoSignaling.GatewayPath
                    + "?token=" + Uri.EscapeDataString(token.Trim());
            }
            return "ws://" + robotIp + ":" + DirectPort + OhhoSignaling.UpstreamPath;
        }

        public static string Describe(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int query = url.IndexOf('?');
            return query >= 0 ? url.Substring(0, query) : url;
        }
    }

    public static class WebRtcTracks
    {
        private static readonly Regex NamePattern = new Regex(
            "^[a-z][a-z0-9_]{0,31}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsName(string name)
        {
            return !string.IsNullOrEmpty(name) && NamePattern.IsMatch(name);
        }

        /// <summary>
        /// <c>/camera/front/image_raw</c> → <c>front</c>. The segment before
        /// <c>image_raw</c> is the ohho_webrtc track name.
        /// </summary>
        public static string NameForTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return null;
            string[] parts = topic.Split('/');
            for (int i = parts.Length - 1; i >= 1; i--)
            {
                if (parts[i] == "image_raw" || parts[i] == "image_raw_compressed")
                    return IsName(parts[i - 1]) ? parts[i - 1] : null;
            }
            return null;
        }

        public static List<string> NamesForTopics(IEnumerable<string> topics)
        {
            var names = new List<string>();
            if (topics == null) return names;
            var seen = new HashSet<string>();
            foreach (string topic in topics)
            {
                string name = NameForTopic(topic);
                if (name == null || !seen.Add(name)) continue;
                names.Add(name);
            }
            return names;
        }
    }

    public static class VideoTransportChoice
    {
        public const string WebRtc = "WebRTC";
        public const string Mjpeg = "MJPEG";

        public static string Select(bool preferWebRtc, bool webRtcAvailable)
        {
            return preferWebRtc && webRtcAvailable ? WebRtc : Mjpeg;
        }
    }

    public static class VideoHud
    {
        public static string Label(string topic, string transport)
        {
            if (string.IsNullOrEmpty(transport)) return topic ?? "";
            if (string.IsNullOrEmpty(topic)) return transport;
            return topic + "  ·  " + transport;
        }
    }

    /// <summary>
    /// Holds the latest decoded frame per track and drops anything older.
    /// <c>com.unity.webrtc</c> 3.0.0 does not expose <c>jitterBufferTarget</c>,
    /// so the client does not add a second queue on top of libwebrtc.
    /// </summary>
    public sealed class MinimalJitterBuffer<T>
    {
        public const int Capacity = 1;
        private readonly Dictionary<string, T> _latest = new Dictionary<string, T>();
        private readonly Dictionary<string, int> _dropped = new Dictionary<string, int>();

        public int Depth(string track)
        {
            return _latest.ContainsKey(track) ? Capacity : 0;
        }

        public int Dropped(string track)
        {
            return _dropped.TryGetValue(track, out int count) ? count : 0;
        }

        public T Push(string track, T frame)
        {
            if (_latest.ContainsKey(track))
                _dropped[track] = Dropped(track) + 1;
            _latest[track] = frame;
            return frame;
        }

        public bool TryLatest(string track, out T frame)
        {
            return _latest.TryGetValue(track, out frame);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// In-process stand-in for the ohho_webrtc viewer handshake. EditMode
    /// tests drive it instead of a robot. It checks the same message shapes
    /// the server accepts and answers an offer with a canned SDP.
    /// </summary>
    public sealed class FakeOhhoSignalingPeer
    {
        private readonly List<WebRtcTrackListing> _catalog;
        private readonly List<WebRtcIceServer> _ice;
        private readonly HashSet<string> _known = new HashSet<string>();
        private string _role;
        private List<string> _joined = new List<string>();
        private string _viewerId;
        private int _ids;

        public IReadOnlyList<string> LastOfferTracks { get; private set; }
        public string LastOfferSdp { get; private set; }
        public List<string> ReceivedIce { get; } = new List<string>();

        public FakeOhhoSignalingPeer(
            IEnumerable<WebRtcTrackListing> catalog,
            IEnumerable<WebRtcIceServer> iceServers)
        {
            _catalog = new List<WebRtcTrackListing>(catalog);
            _ice = new List<WebRtcIceServer>(iceServers);
            for (int i = 0; i < _catalog.Count; i++)
                _known.Add(_catalog[i].Name);
        }

        public static FakeOhhoSignalingPeer WithLanTurn()
        {
            return new FakeOhhoSignalingPeer(
                new[]
                {
                    new WebRtcTrackListing("chase", "/camera/chase/image_raw"),
                    new WebRtcTrackListing("front", "/camera/front/image_raw"),
                    new WebRtcTrackListing("wrist", "/camera/wrist/image_raw")
                },
                new[]
                {
                    new WebRtcIceServer(new[] { "stun:stun.l.google.com:19302" }, null, null),
                    new WebRtcIceServer(
                        new[] { "turn:turn.example.com:3478?transport=udp" },
                        "ohho",
                        "secret")
                });
        }

        public string Receive(string json)
        {
            ClientSignal message;
            try { message = OhhoSignaling.ParseClient(json); }
            catch (SignalingFormatException ex) { return OhhoSignaling.Error(ex.Code); }

            if (_role == null)
            {
                if (message.Type != "join")
                    return OhhoSignaling.Error("join_required");
                if (message.Role == "publisher")
                    return OhhoSignaling.Error("publisher_internal");
                for (int i = 0; i < message.Tracks.Length; i++)
                {
                    if (!_known.Contains(message.Tracks[i]))
                    {
                        return new JObject
                        {
                            ["type"] = "error",
                            ["error"] = "unknown_track",
                            ["track"] = message.Tracks[i]
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
                _ids++;
                _viewerId = "v" + _ids;
                _role = "viewer";
                _joined = new List<string>(message.Tracks);
                return OhhoSignaling.Welcome("viewer", _viewerId, _catalog, _ice);
            }

            if (message.Type == "list")
            {
                return new JObject
                {
                    ["type"] = "tracks",
                    ["tracks"] = new JArray()
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            if (message.Type == "offer")
            {
                if (_role != "viewer")
                    return OhhoSignaling.Error("offer_from_viewer_only");
                for (int i = 0; i < message.Tracks.Length; i++)
                {
                    if (!_joined.Contains(message.Tracks[i]))
                        return OhhoSignaling.Error("unknown_track");
                }
                LastOfferTracks = message.Tracks;
                LastOfferSdp = message.Sdp;
                return OhhoSignaling.Answer("v=0\r\nlocal-answer\r\n", message.Tracks, _viewerId);
            }
            if (message.Type == "ice")
            {
                if (!_known.Contains(message.Track))
                    return OhhoSignaling.Error("unknown_track");
                if (!string.IsNullOrEmpty(message.Candidate))
                    ReceivedIce.Add(message.Candidate);
                return null;
            }
            return OhhoSignaling.Error("unexpected_message");
        }

        public string EmitIce(string track, string candidate, string sdpMid, int sdpMLineIndex)
        {
            return OhhoSignaling.ServerIce(track, candidate, sdpMid, sdpMLineIndex, _viewerId);
        }
    }
#endif
}
