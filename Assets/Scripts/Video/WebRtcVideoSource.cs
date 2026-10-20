using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// WebRTC receiver for ohho_webrtc (OHH-210). The headset joins as a
    /// viewer, sends one recvonly H.264 offer covering every requested track,
    /// and renders each decoded texture as it arrives.
    ///
    /// Package: <c>com.unity.webrtc</c> 3.0.0. Its package manifest allows
    /// Unity 6000.0 and newer, which covers this project's 6000.5.2f1 pin.
    /// On that editor the Package Manager prints that the package is
    /// deprecated and no longer supported; the 3.0.0 scripts still compile
    /// here, and there is no newer release. Android support is ARM64 IL2CPP,
    /// matching <c>AndroidTargetArchitectures</c> (ARM64 only). The player
    /// graphics API is Vulkan with no OpenGL ES fallback. 3.0.0 decodes into
    /// a <see cref="Texture2D"/> on Vulkan, including the Android
    /// external-memory fix that 3.0.0-pre.8 did not have, and the native
    /// library is built for 16 KB pages. A separate MediaCodec plugin is not
    /// required for this target.
    ///
    /// ICE servers are copied from the <c>welcome</c> session record
    /// (STUN and TURN). Nothing is added when that list is empty.
    ///
    /// Quest glass-to-glass (30 fps, under 150 ms, app still at 72 Hz) needs
    /// a headset on the robot LAN. This class does not measure it.
    /// </summary>
    public sealed class WebRtcVideoSource : IVideoSource
    {
        private const float SignalingTimeoutSeconds = 8f;

        private static MonoBehaviour _pumpHost;

        public string TransportName => VideoTransportChoice.WebRtc;
        public bool IsStreaming => _streaming;
        public event Action<Texture2D> OnFrame;
        public event Action<string> OnError;

        private readonly MonoBehaviour _host;
        private readonly List<string> _tracks = new List<string>();
        private readonly List<RTCRtpTransceiver> _transceivers = new List<RTCRtpTransceiver>();
        private readonly MinimalJitterBuffer<Texture2D> _frames = new MinimalJitterBuffer<Texture2D>();
        private readonly List<SignalingInbound> _earlyIce = new List<SignalingInbound>();
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private int _socketGeneration;

        private OhhoViewerSession _session;
        private WebSocket _socket;
        private RTCPeerConnection _pc;
        private Coroutine _loop;
        private string _url;
        private string _shown;
        private bool _streaming;
        private bool _remoteSet;
        private bool _failed;
        private bool _disposed;
        private bool _offerSent;
        private bool _reconnect;

        public WebRtcVideoSource(MonoBehaviour host)
        {
            _host = host;
        }

        /// <summary>
        /// True when this player can host a receive pipeline. Desktop OpenGL
        /// is rejected the same way the package rejects it. Android (Vulkan
        /// or GLES) is accepted; this project ships Vulkan only.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                RuntimePlatform platform = Application.platform;
                bool desktop = platform == RuntimePlatform.WindowsEditor
                    || platform == RuntimePlatform.WindowsPlayer
                    || platform == RuntimePlatform.OSXEditor
                    || platform == RuntimePlatform.OSXPlayer;
                if (!desktop) return true;
                GraphicsDeviceType graphics = SystemInfo.graphicsDeviceType;
                if (graphics == GraphicsDeviceType.OpenGLCore
                    || graphics == GraphicsDeviceType.OpenGLES2
                    || graphics == GraphicsDeviceType.OpenGLES3)
                    return false;
                return true;
            }
        }

        public void Start(string robotIp, string topic)
        {
            string name = WebRtcTracks.NameForTopic(topic);
            if (name == null && WebRtcTracks.IsName(topic))
                name = topic;
            if (name == null)
            {
                Fail("no WebRTC track for " + topic);
                return;
            }
            StartTracks(robotIp, new[] { name });
        }

        public void StartTracks(string robotIp, IReadOnlyList<string> trackNames)
        {
            Stop();
            _disposed = false;
            _failed = false;
            _streaming = false;
            _remoteSet = false;
            _offerSent = false;
            _tracks.Clear();
            _transceivers.Clear();
            _earlyIce.Clear();
            if (trackNames != null)
            {
                for (int i = 0; i < trackNames.Count; i++)
                {
                    if (WebRtcTracks.IsName(trackNames[i]) && !_tracks.Contains(trackNames[i]))
                        _tracks.Add(trackNames[i]);
                }
            }
            if (_tracks.Count == 0)
            {
                Fail("no WebRTC tracks");
                return;
            }
            if (_host == null)
            {
                Fail("WebRTC source has no host");
                return;
            }
            _shown = _tracks[0];
            _session = new OhhoViewerSession(_tracks);
            while (_inbox.TryDequeue(out _)) { }
            string explicitUrl = PlayerPrefs.GetString(WebRtcEndpoints.UrlPref, "");
            string token = PlayerPrefs.GetString(WebRtcEndpoints.TokenPref, "");
            string url = WebRtcEndpoints.Resolve(robotIp, explicitUrl, token);
            Debug.Log("[WebRtcVideoSource] Signaling " + WebRtcEndpoints.Describe(url)
                + " tracks " + string.Join(",", _tracks));
            _url = url;
            _loop = _host.StartCoroutine(SignalingLoop());
        }

        /// <summary>
        /// Show one of the negotiated tracks. <paramref name="topicOrName"/>
        /// is a ROS image topic or a track name.
        /// </summary>
        public bool TryShow(string topicOrName)
        {
            string name = WebRtcTracks.NameForTopic(topicOrName);
            if (name == null && WebRtcTracks.IsName(topicOrName))
                name = topicOrName;
            if (name == null || !_tracks.Contains(name))
                return false;
            _shown = name;
            if (_frames.TryLatest(name, out Texture2D tex) && tex != null)
                OnFrame?.Invoke(tex);
            return true;
        }

        public void Tick()
        {
            DispatchSocket();
        }

        public void Stop()
        {
            _streaming = false;
            if (_loop != null && _host != null)
            {
                _host.StopCoroutine(_loop);
                _loop = null;
            }
            CloseSocket();
            ClosePeer();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private IEnumerator SignalingLoop()
        {
            while (!_disposed && !_failed)
            {
                _reconnect = false;
                yield return ConnectOnce(_url);
                if (!_reconnect || _disposed || _failed)
                    yield break;
                CloseSocket();
                ClosePeer();
                while (_inbox.TryDequeue(out _)) { }
                _offerSent = false;
                _remoteSet = false;
                _streaming = false;
            }
        }

        private IEnumerator ConnectOnce(string url)
        {
            int generation = ++_socketGeneration;
            _socket = new WebSocket(url);
            _socket.OnOpen += () => Enqueue(generation, "open");
            _socket.OnError += message => Enqueue(generation, "error\n" + message);
            _socket.OnClose += _ => Enqueue(generation, "close");
            _socket.OnMessage += bytes =>
                Enqueue(generation, "text\n" + System.Text.Encoding.UTF8.GetString(bytes));

            Task connect;
            try { connect = _socket.Connect(); }
            catch (Exception ex)
            {
                Fail("signaling connect failed: " + ex.Message);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + SignalingTimeoutSeconds;
            while (!_disposed && !_failed && !_reconnect)
            {
                DispatchSocket();
                if (connect.IsFaulted)
                {
                    Fail("signaling connect failed");
                    yield break;
                }
                DrainInbox();
                if (_reconnect)
                    yield break;
                if (_streaming)
                    deadline = float.PositiveInfinity;
                else if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("signaling timed out");
                    yield break;
                }
                yield return null;
            }
        }

        private void DrainInbox()
        {
            while (_inbox.TryDequeue(out string item) && !_disposed && !_failed && !_reconnect)
            {
                if (item == "open")
                {
                    Send(_session.CreateJoin());
                    continue;
                }
                if (item == "close")
                {
                    if (!_streaming)
                        Fail("signaling closed");
                    return;
                }
                if (item.StartsWith("error\n", StringComparison.Ordinal))
                {
                    Fail(item.Substring("error\n".Length));
                    return;
                }
                if (item.StartsWith("text\n", StringComparison.Ordinal))
                    HandleServer(item.Substring("text\n".Length));
            }
        }

        private void HandleServer(string json)
        {
            SignalingInbound inbound = _session.Accept(json);
            if (inbound.Type == "error"
                && inbound.Error == "unknown_track"
                && TryDropUnknown(inbound.Track))
                return;
            if (_session.Failed)
            {
                Fail(string.IsNullOrEmpty(_session.Failure) ? "signaling error" : _session.Failure);
                return;
            }
            if (inbound.Type == "welcome")
            {
                if (_session.Record == null || _session.Record.Role != "viewer")
                {
                    Fail("unexpected signaling role");
                    return;
                }
                try { BeginOffer(); }
                catch (Exception ex) { Fail("peer connection failed: " + ex.Message); }
                return;
            }
            if (inbound.Type == "answer")
            {
                _host.StartCoroutine(ApplyAnswer(inbound.Sdp));
                return;
            }
            if (inbound.Type == "ice")
                AddRemoteIce(inbound);
        }

        private bool TryDropUnknown(string bad)
        {
            if (string.IsNullOrEmpty(bad) || !_tracks.Remove(bad))
                return false;
            if (_tracks.Count == 0 || !_tracks.Contains(_shown))
                return false;
            _session = new OhhoViewerSession(_tracks);
            _offerSent = false;
            ClosePeer();
            _reconnect = true;
            Debug.Log("[WebRtcVideoSource] Dropping unknown track " + bad);
            return true;
        }

        private void BeginOffer()
        {
            if (_offerSent || _pc != null) return;
            EnsurePump(_host);
            var configuration = new RTCConfiguration
            {
                iceServers = ToUnityIce(_session.Record.IceServers),
                bundlePolicy = RTCBundlePolicy.BundlePolicyMaxBundle
            };
            _pc = new RTCPeerConnection(ref configuration);
            _pc.OnIceCandidate = OnLocalIce;
            _pc.OnTrack = OnRemoteTrack;
            _pc.OnConnectionStateChange = OnPeerState;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var init = new RTCRtpTransceiverInit
                {
                    direction = RTCRtpTransceiverDirection.RecvOnly
                };
                RTCRtpTransceiver transceiver = _pc.AddTransceiver(TrackKind.Video, init);
                PreferH264(transceiver);
                _transceivers.Add(transceiver);
            }
            _host.StartCoroutine(SendOffer());
        }

        private IEnumerator SendOffer()
        {
            RTCSessionDescriptionAsyncOperation offerOp = _pc.CreateOffer();
            yield return offerOp;
            if (_disposed || _failed || _pc == null) yield break;
            if (offerOp.IsError)
            {
                Fail("create offer failed: " + offerOp.Error.message);
                yield break;
            }
            RTCSessionDescription desc = offerOp.Desc;
            RTCSetSessionDescriptionAsyncOperation localOp = _pc.SetLocalDescription(ref desc);
            yield return localOp;
            if (_disposed || _failed || _pc == null) yield break;
            if (localOp.IsError)
            {
                Fail("set local description failed: " + localOp.Error.message);
                yield break;
            }
            string sdp = _pc.LocalDescription.sdp;
            try { Send(_session.CreateOffer(sdp)); }
            catch (SignalingFormatException ex)
            {
                Fail(ex.Code);
                yield break;
            }
            _offerSent = true;
        }

        private IEnumerator ApplyAnswer(string sdp)
        {
            if (_pc == null || _disposed) yield break;
            var desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = sdp
            };
            RTCSetSessionDescriptionAsyncOperation remoteOp = _pc.SetRemoteDescription(ref desc);
            yield return remoteOp;
            if (_disposed || _failed || _pc == null) yield break;
            if (remoteOp.IsError)
            {
                Fail("set remote description failed: " + remoteOp.Error.message);
                yield break;
            }
            _remoteSet = true;
            for (int i = 0; i < _earlyIce.Count; i++)
                ApplyIce(_earlyIce[i]);
            _earlyIce.Clear();
            _streaming = true;
        }

        private void OnLocalIce(RTCIceCandidate candidate)
        {
            if (_disposed || candidate == null || string.IsNullOrEmpty(candidate.Candidate))
                return;
            string track = TrackForLocal(candidate);
            int line = candidate.SdpMLineIndex ?? 0;
            try
            {
                Send(_session.CreateIce(track, candidate.Candidate, candidate.SdpMid ?? "0", line));
            }
            catch (SignalingFormatException ex)
            {
                Debug.LogWarning("[WebRtcVideoSource] Dropped local ICE: " + ex.Code);
            }
        }

        private string TrackForLocal(RTCIceCandidate candidate)
        {
            int index = candidate.SdpMLineIndex ?? 0;
            if (!string.IsNullOrEmpty(candidate.SdpMid))
            {
                for (int i = 0; i < _transceivers.Count; i++)
                {
                    if (_transceivers[i] != null && _transceivers[i].Mid == candidate.SdpMid)
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 || index >= _tracks.Count) index = 0;
            return _tracks[index];
        }

        private void OnRemoteTrack(RTCTrackEvent evt)
        {
            var video = evt.Track as VideoStreamTrack;
            if (video == null) return;
            int index = 0;
            for (int i = 0; i < _transceivers.Count; i++)
            {
                if (_transceivers[i] == evt.Transceiver)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0 || index >= _tracks.Count) return;
            string name = _tracks[index];
            video.OnVideoReceived += tex => Deliver(name, tex);
        }

        private void Deliver(string name, Texture tex)
        {
            if (_disposed) return;
            var tex2d = tex as Texture2D;
            if (tex2d == null) return;
            _frames.Push(name, tex2d);
            if (name == _shown)
                OnFrame?.Invoke(tex2d);
        }

        private void OnPeerState(RTCPeerConnectionState state)
        {
            if (state == RTCPeerConnectionState.Failed)
                Fail("peer connection failed");
        }

        private void AddRemoteIce(SignalingInbound ice)
        {
            if (string.IsNullOrEmpty(ice.Candidate)) return;
            if (!_remoteSet || _pc == null)
            {
                _earlyIce.Add(ice);
                return;
            }
            ApplyIce(ice);
        }

        private void ApplyIce(SignalingInbound ice)
        {
            if (_pc == null || string.IsNullOrEmpty(ice.Candidate)) return;
            var init = new RTCIceCandidateInit
            {
                candidate = ice.Candidate,
                sdpMid = ice.SdpMid,
                sdpMLineIndex = ice.SdpMLineIndex
            };
            _pc.AddIceCandidate(new RTCIceCandidate(init));
        }

        private static void PreferH264(RTCRtpTransceiver transceiver)
        {
            try
            {
                RTCRtpCapabilities caps = RTCRtpSender.GetCapabilities(TrackKind.Video);
                if (caps == null || caps.codecs == null) return;
                var h264 = new List<RTCRtpCodecCapability>();
                for (int i = 0; i < caps.codecs.Length; i++)
                {
                    RTCRtpCodecCapability codec = caps.codecs[i];
                    if (codec != null
                        && codec.mimeType != null
                        && codec.mimeType.Equals("video/H264", StringComparison.OrdinalIgnoreCase))
                        h264.Add(codec);
                }
                if (h264.Count == 0) return;
                RTCErrorType error = transceiver.SetCodecPreferences(h264.ToArray());
                if (error != RTCErrorType.None)
                    Debug.LogWarning("[WebRtcVideoSource] H264 preference not applied (" + error + ").");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WebRtcVideoSource] H264 preference skipped: " + ex.Message);
            }
        }

        private static RTCIceServer[] ToUnityIce(IReadOnlyList<WebRtcIceServer> servers)
        {
            if (servers == null || servers.Count == 0)
                return new RTCIceServer[0];
            var result = new List<RTCIceServer>(servers.Count);
            for (int i = 0; i < servers.Count; i++)
            {
                WebRtcIceServer server = servers[i];
                if (server == null || server.Urls == null || server.Urls.Length == 0)
                    continue;
                result.Add(new RTCIceServer
                {
                    urls = server.Urls,
                    username = server.Username ?? "",
                    credential = server.Credential ?? "",
                    credentialType = RTCIceCredentialType.Password
                });
            }
            return result.ToArray();
        }

        private static void EnsurePump(MonoBehaviour host)
        {
            if (host == null || _pumpHost == host) return;
            _pumpHost = host;
            host.StartCoroutine(WebRTC.Update());
        }

        private void Enqueue(int generation, string item)
        {
            if (generation != _socketGeneration || _disposed) return;
            _inbox.Enqueue(item);
        }

        private void DispatchSocket()
        {
            if (_socket == null) return;
            try { _socket.DispatchMessageQueue(); }
            catch (Exception) { /* socket already closed */ }
        }

        private void Send(string json)
        {
            if (_disposed || _socket == null || _socket.State != WebSocketState.Open)
                return;
            _ = _socket.SendText(json);
        }

        private void Fail(string reason)
        {
            if (_disposed || _failed) return;
            _failed = true;
            _streaming = false;
            Debug.LogWarning("[WebRtcVideoSource] " + reason);
            OnError?.Invoke(reason);
        }

        private void CloseSocket()
        {
            _socketGeneration++;
            WebSocket socket = _socket;
            _socket = null;
            if (socket == null) return;
            try
            {
                if (socket.State == WebSocketState.Open)
                    _ = socket.Close();
            }
            catch (Exception) { /* already closed */ }
        }

        private void ClosePeer()
        {
            RTCPeerConnection pc = _pc;
            _pc = null;
            _transceivers.Clear();
            if (pc == null) return;
            try
            {
                pc.Close();
                pc.Dispose();
            }
            catch (Exception) { /* native peer already released */ }
        }
    }
}
