using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using OmniBot.VR.Control;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Profile-driven camera feed controller — the Phase 4 replacement for the
    /// legacy <c>CameraFeedViewer</c>. Instead of hardcoded topic names, it reads
    /// the camera list from <see cref="RobotProfile.Topics.Images"/> (set by
    /// <see cref="RobotProfileFactory"/> from the catalog), so a drone shows its
    /// FPV + down cameras, a mobile manipulator shows front + wrist + BEV, and an
    /// industrial arm shows just its wrist camera — all automatically.
    ///
    /// Video transport: tries <see cref="WebRtcVideoSource"/> first when
    /// <c>preferWebRtc</c> is set and the camera topic maps to an ohho_webrtc
    /// track. Every mapped camera shares one peer connection. The camera name
    /// overlay shows WebRTC or MJPEG. If signaling fails, that camera falls
    /// back to <see cref="MjpegVideoSource"/>. Right thumbstick press cycles
    /// cameras.
    /// </summary>
    public class CameraFeedController : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private RawImage displayImage;
        [SerializeField] private TMP_Text cameraNameOverlay;
        [SerializeField] private Texture2D noSignalTexture;

        [Header("Transport")]
        [Tooltip("Prefer WebRTC when com.unity.webrtc can run. Falls back to MJPEG per camera.")]
        [SerializeField] private bool preferWebRtc = true;

        [Tooltip("Optional label that shows WebRTC or MJPEG. When empty, the camera name overlay includes the transport.")]
        [SerializeField] private TMP_Text transportLabel;

        // ── State ─────────────────────────────────────────────────────────────
        private RobotProfile _profile;
        private List<string> _cameras = new List<string>();
        private int _currentIndex;
        private IVideoSource _activeSource;
        private string _robotIp;
        private int _generation;

        /// <summary>Active transport, "WebRTC" or "MJPEG", or null when there is no camera.</summary>
        public string ActiveTransport { get; private set; }

        /// <summary>Fires when the HUD transport label changes.</summary>
        public event Action<string> OnTransportChanged;

        // ── Unity lifecycle ────────────────────────────────────────────────────
        private void Start()
        {
            _robotIp = PlayerPrefs.GetString("robot_ip", "192.168.1.101");
            var conn = OmniBot.VR.Core.ConnectionManager.Instance;
            if (conn != null) conn.OnRobotIpChanged += OnRobotIpChanged;
        }

        private void OnDestroy()
        {
            var conn = OmniBot.VR.Core.ConnectionManager.Instance;
            if (conn != null) conn.OnRobotIpChanged -= OnRobotIpChanged;
            _activeSource?.Dispose();
        }

        private void Update()
        {
            _activeSource?.Tick();
            if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick, OVRInput.Controller.RTouch))
                CycleCamera();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Configure the camera list from the selected robot's profile. Called by
        /// <see cref="OmniBot.VR.Control.TeleopController"/> (or OhhoVrApp) when
        /// teleop starts.
        /// </summary>
        public void Configure(RobotProfile profile)
        {
            _profile = profile;
            _cameras = profile?.Topics?.Images ?? new List<string>();
            _currentIndex = 0;
            StartCurrentCamera();
        }

        /// <summary>Advances to the next camera. WebRTC tracks already negotiated are swapped in place.</summary>
        public void CycleCamera()
        {
            if (_cameras.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _cameras.Count;
            ShowOrRestart();
        }

        /// <summary>Switches to a specific camera by index.</summary>
        public void SetCamera(int index)
        {
            if (_cameras.Count == 0) return;
            _currentIndex = Mathf.Clamp(index, 0, _cameras.Count - 1);
            ShowOrRestart();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void OnRobotIpChanged(string newIp)
        {
            _robotIp = newIp;
            StartCurrentCamera();
        }

        private void ShowOrRestart()
        {
            string topic = CurrentTopic();
            if (_activeSource is WebRtcVideoSource live && live.TryShow(topic))
            {
                ShowTransport(topic, live.TransportName);
                return;
            }
            StartCurrentCamera();
        }

        private void StartCurrentCamera()
        {
            _generation++;
            int generation = _generation;
            _activeSource?.Dispose();
            _activeSource = null;

            if (_cameras.Count == 0)
            {
                ActiveTransport = null;
                ShowNoSignal();
                if (cameraNameOverlay != null) cameraNameOverlay.text = "No cameras";
                if (transportLabel != null) transportLabel.text = "";
                OnTransportChanged?.Invoke(null);
                return;
            }

            string topic = CurrentTopic();
            List<string> trackNames = WebRtcTracks.NamesForTopics(_cameras);
            string currentName = WebRtcTracks.NameForTopic(topic);
            bool wantWebRtc = VideoTransportChoice.Select(preferWebRtc, WebRtcVideoSource.IsAvailable)
                    == VideoTransportChoice.WebRtc
                && currentName != null
                && trackNames.Count > 0;

            if (wantWebRtc)
            {
                var webrtc = new WebRtcVideoSource(this);
                webrtc.OnFrame += OnVideoFrame;
                webrtc.OnError += err =>
                {
                    Debug.LogWarning($"[CameraFeedController] WebRTC error: {err}");
                    FallbackToMjpeg(generation);
                };
                _activeSource = webrtc;
                ShowTransport(topic, webrtc.TransportName);
                webrtc.StartTracks(_robotIp, trackNames);
                if (_activeSource == webrtc)
                    webrtc.TryShow(topic);
                Debug.Log($"[CameraFeedController] Starting WebRTC for {string.Join(",", trackNames)}");
                return;
            }

            StartMjpeg(topic);
        }

        private void FallbackToMjpeg(int generation)
        {
            if (generation != _generation) return;
            _generation++;
            string topic = CurrentTopic();
            Debug.Log($"[CameraFeedController] Falling back to MJPEG for {topic}");
            _activeSource?.Dispose();
            _activeSource = null;
            if (string.IsNullOrEmpty(topic))
            {
                ShowNoSignal();
                return;
            }
            StartMjpeg(topic);
        }

        private void StartMjpeg(string topic)
        {
            var mjpeg = new MjpegVideoSource(this);
            mjpeg.OnFrame += OnVideoFrame;
            mjpeg.OnError += err =>
            {
                Debug.LogWarning($"[CameraFeedController] MJPEG error: {err}");
                ShowNoSignal();
            };
            mjpeg.Start(_robotIp, topic);
            _activeSource = mjpeg;
            ShowTransport(topic, mjpeg.TransportName);
        }

        private string CurrentTopic()
        {
            if (_cameras.Count == 0) return null;
            int idx = Mathf.Clamp(_currentIndex, 0, _cameras.Count - 1);
            return _cameras[idx];
        }

        private void ShowTransport(string topic, string transport)
        {
            ActiveTransport = transport;
            if (transportLabel != null)
            {
                transportLabel.text = transport;
                if (cameraNameOverlay != null) cameraNameOverlay.text = topic;
            }
            else if (cameraNameOverlay != null)
            {
                cameraNameOverlay.text = VideoHud.Label(topic, transport);
            }
            OnTransportChanged?.Invoke(transport);
        }

        private void OnVideoFrame(Texture2D frame)
        {
            if (displayImage != null)
            {
                displayImage.color = Color.white; // full brightness once streaming
                displayImage.texture = frame;
            }
        }

        private void ShowNoSignal()
        {
            if (displayImage == null) return;
            displayImage.color = new Color(0.02f, 0.03f, 0.06f, 1f); // dark idle state
            if (noSignalTexture != null)
                displayImage.texture = noSignalTexture;
            else
            {
                var grey = new Texture2D(64, 64);
                var pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.2f, 0.2f, 0.2f);
                grey.SetPixels(pixels);
                grey.Apply();
                displayImage.texture = grey;
            }
        }
    }
}
