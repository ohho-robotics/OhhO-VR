using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Session;
using OmniBot.VR.Core;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Profile-driven camera feed. A sim session opens the curved sim screen on
    /// the chase camera, with a strip for front / wrist / BEV and a wrist
    /// picture-in-picture. A real session keeps the profile camera order on the
    /// flat panel. Both use MJPEG with off-thread decode. The BEV topic is also
    /// drawn on a floor quad under the tabletop twin.
    /// </summary>
    public class CameraFeedController : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private RawImage displayImage;
        [SerializeField] private TMP_Text cameraNameOverlay;
        [SerializeField] private Texture2D noSignalTexture;

        [Header("Transport")]
        [Tooltip("Prefer WebRTC when com.unity.webrtc is available. Falls back to MJPEG.")]
        [SerializeField] private bool preferWebRtc = true;

        private SimScreenPlan _plan;
        private SimScreenPresenter _presenter;
        private int _mainIndex;
        private IVideoSource _mainSource;
        private MjpegVideoSource _mainMjpeg;
        private MjpegVideoSource _pipMjpeg;
        private string _robotIp;
        private bool _configured;
        private bool _offline;
        private bool _ipHooked;
        private bool _inFallback;
        private bool _hasFrame;
        private double _frameReceivedAt;
        private float _statsTimer;
        private readonly StreamFpsCounter _fps = new StreamFpsCounter();
        private readonly List<DetectionBox> _boxes = new List<DetectionBox>();
        private readonly FrontDetectionCompositor _compositor = new FrontDetectionCompositor();
        private BevFloorPanel _floor;
        private string _detectionTopic;
        private bool _detectionLogged;
        private Texture2D _grey;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_robotIp))
                _robotIp = PlayerPrefs.GetString("robot_ip", "192.168.1.101");
            EnsurePresenter();
            HookIp();
        }

        private void OnEnable()
        {
            HookIp();
            if (_configured) RestartAll();
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            StopAll();
            var conn = OmniBot.VR.Core.ConnectionManager.Instance;
            if (conn != null && _ipHooked) conn.OnRobotIpChanged -= OnRobotIpChanged;
            _ipHooked = false;
            if (_grey != null) Destroy(_grey);
            _compositor.Release();
            if (!string.IsNullOrEmpty(_detectionTopic))
            {
                var ros = ROSBridgeClient.Instance;
                if (ros != null) ros.Unsubscribe(_detectionTopic);
                _detectionTopic = null;
            }
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick, OVRInput.Controller.RTouch))
                CycleCamera();
            _statsTimer += Time.unscaledDeltaTime;
            if (_statsTimer >= 0.1f)
            {
                _statsTimer = 0f;
                RefreshStats();
            }
        }

        /// <summary>
        /// Configure from the selected robot. Chase is the default when the
        /// active session is <see cref="SessionKind.Sim"/>.
        /// </summary>
        public void Configure(RobotProfile profile)
        {
            if (string.IsNullOrEmpty(_robotIp))
                _robotIp = PlayerPrefs.GetString("robot_ip", "192.168.1.101");
            var endpoint = ActiveSession.Current;
            _offline = endpoint != null && endpoint.Offline;
            SessionKind? kind = null;
            string host = _robotIp;
            IList<string> urls = null;
            if (endpoint != null)
            {
                kind = endpoint.Kind;
                if (!string.IsNullOrEmpty(endpoint.Host)) host = endpoint.Host;
                urls = endpoint.VideoUrls;
            }
            IList<string> topics = null;
            if (profile != null && profile.Topics != null) topics = profile.Topics.Images;
            _plan = SimScreenPlan.Build(kind, topics, host, urls);
            _mainIndex = _plan.Views.Count == 0 ? 0 : _plan.DefaultIndex;
            _configured = true;
            _hasFrame = false;
            _fps.Clear();
            _boxes.Clear();
            EnsurePresenter();
            _presenter.Apply(_plan);
            _presenter.SetSelection(_mainIndex);
            EnsureFloor();
            ApplyLabel();
            LogDetectionsOnce();
            if (isActiveAndEnabled) RestartAll();
            else _floor.Apply(_plan, false);
        }

        public void CycleCamera()
        {
            if (_plan == null || _plan.Views.Count == 0) return;
            SetCamera((_mainIndex + 1) % _plan.Views.Count);
        }

        public void SetCamera(int index)
        {
            if (_plan == null || _plan.Views.Count == 0) return;
            int next = Mathf.Clamp(index, 0, _plan.Views.Count - 1);
            if (next == _mainIndex && _mainSource != null) return;
            int oldPip = _plan.PipIndexFor(_mainIndex);
            _mainIndex = next;
            int newPip = _plan.PipIndexFor(_mainIndex);
            _hasFrame = false;
            _fps.Clear();
            ApplyLabel();
            if (_presenter != null) _presenter.SetSelection(_mainIndex);
            if (!isActiveAndEnabled || _offline) return;
            StopMain();
            StartMain();
            if (oldPip != newPip)
            {
                StopPip();
                StartPip();
            }
            else if (_presenter != null)
                _presenter.SetPipVisible(newPip >= 0);
            RefreshStats();
        }

        private void EnsurePresenter()
        {
            if (_presenter != null) return;
            _presenter = GetComponent<SimScreenPresenter>();
            if (_presenter == null) _presenter = gameObject.AddComponent<SimScreenPresenter>();
            _presenter.Bind(this);
        }

        private void EnsureFloor()
        {
            if (_floor != null) return;
            var go = new GameObject("BevFloorDriver");
            go.transform.SetParent(transform, false);
            _floor = go.AddComponent<BevFloorPanel>();
        }

        private void LogDetectionsOnce()
        {
            if (_detectionLogged || DetectionTopics.IsAvailable) return;
            _detectionLogged = true;
            Debug.Log("[Perception] " + DetectionTopics.UnavailableReason);
        }

        private void StartDetections()
        {
            StopDetections();
            if (_offline || !DetectionTopics.TryResolve(out string topic)) return;
            var ros = ROSBridgeClient.Instance;
            if (ros == null) return;
            _detectionTopic = topic;
            ros.Subscribe(topic, DetectionTopics.MessageType, OnDetections);
        }

        private void StopDetections()
        {
            if (string.IsNullOrEmpty(_detectionTopic)) return;
            var ros = ROSBridgeClient.Instance;
            if (ros != null) ros.Unsubscribe(_detectionTopic);
            _detectionTopic = null;
        }

        private void OnDetections(string json)
        {
            if (!Detection2DParser.TryParse(json, _boxes, out string error))
                Debug.LogWarning("[Perception] Detection2DArray " + error);
        }

        private void HookIp()
        {
            if (_ipHooked) return;
            var conn = OmniBot.VR.Core.ConnectionManager.Instance;
            if (conn == null) return;
            conn.OnRobotIpChanged += OnRobotIpChanged;
            _ipHooked = true;
        }

        private void OnRobotIpChanged(string newIp)
        {
            _robotIp = newIp;
            if (_plan != null) _plan.RetargetHost(newIp);
            if (_configured && isActiveAndEnabled) RestartAll();
        }

        private void RestartAll()
        {
            StopAll();
            if (!_configured || _plan == null) return;
            ApplyLabel();
            if (_floor != null) _floor.Apply(_plan, !_offline);
            if (_offline || _plan.Views.Count == 0)
            {
                ShowNoSignal();
                return;
            }
            _hasFrame = false;
            _fps.Clear();
            StartDetections();
            StartMain();
            StartPip();
            if (_presenter != null) _presenter.SetSelection(_mainIndex);
            RefreshStats();
        }

        private void StartMain()
        {
            if (_plan == null || _plan.Views.Count == 0) return;
            string topic = _plan.Views[_mainIndex].Topic;
            if (preferWebRtc && WebRtcVideoSource.IsAvailable)
            {
                var webrtc = new WebRtcVideoSource(this);
                webrtc.OnFrame += OnMainFrame;
                webrtc.OnError += _ => FallbackToMjpeg();
                webrtc.Start(string.IsNullOrEmpty(_plan.Host) ? _robotIp : _plan.Host, topic);
                _mainSource = webrtc;
                _mainMjpeg = null;
                return;
            }
            StartMainMjpeg();
        }

        private void FallbackToMjpeg()
        {
            if (_inFallback) return;
            _inFallback = true;
            Debug.Log("[CameraFeedController] Falling back to MJPEG");
            StopMain();
            StartMainMjpeg();
            _inFallback = false;
        }

        private void StartMainMjpeg()
        {
            if (_plan == null || _plan.Views.Count == 0) return;
            string url = _plan.UrlFor(_mainIndex, SimScreenCameras.MainQuality, SimScreenCameras.MainWidth);
            if (string.IsNullOrEmpty(url))
            {
                ShowNoSignal();
                return;
            }
            var mjpeg = new MjpegVideoSource(this);
            mjpeg.JpegQuality = SimScreenCameras.MainQuality;
            mjpeg.JpegWidth = SimScreenCameras.MainWidth;
            mjpeg.OnFrame += OnMainFrame;
            mjpeg.OnError += err =>
            {
                Debug.LogWarning("[CameraFeedController] MJPEG " + err);
                ShowNoSignal();
            };
            mjpeg.StartUrl(url);
            _mainSource = mjpeg;
            _mainMjpeg = mjpeg;
            Debug.Log("[CameraFeedController] MJPEG " + _plan.Views[_mainIndex].Label + " " + url);
        }

        private void StartPip()
        {
            if (_plan == null) return;
            int pip = _plan.PipIndexFor(_mainIndex);
            if (_presenter != null) _presenter.SetPipVisible(pip >= 0);
            if (pip < 0) return;
            string url = _plan.UrlFor(pip, SimScreenCameras.PipQuality, SimScreenCameras.PipWidth);
            if (string.IsNullOrEmpty(url)) return;
            var mjpeg = new MjpegVideoSource(this);
            mjpeg.JpegQuality = SimScreenCameras.PipQuality;
            mjpeg.JpegWidth = SimScreenCameras.PipWidth;
            mjpeg.OnFrame += OnPipFrame;
            mjpeg.OnError += err =>
            {
                Debug.LogWarning("[CameraFeedController] Wrist PiP " + err);
                if (_presenter != null) _presenter.SetPipVisible(false);
            };
            mjpeg.StartUrl(url);
            _pipMjpeg = mjpeg;
        }

        private void StopAll()
        {
            StopMain();
            StopPip();
            StopDetections();
        }

        private void StopMain()
        {
            if (_mainSource != null)
            {
                _mainSource.OnFrame -= OnMainFrame;
                _mainSource.Dispose();
                _mainSource = null;
            }
            _mainMjpeg = null;
        }

        private void StopPip()
        {
            if (_pipMjpeg != null)
            {
                _pipMjpeg.OnFrame -= OnPipFrame;
                _pipMjpeg.Dispose();
                _pipMjpeg = null;
            }
            if (_presenter != null) _presenter.SetPipVisible(false);
        }

        private void OnMainFrame(Texture2D frame)
        {
            if (_compositor != null && _plan != null && _mainIndex >= 0 && _mainIndex < _plan.Views.Count)
            {
                Texture2D shown = _compositor.Present(frame, _plan.Views[_mainIndex].Topic, _boxes);
                if (shown != null) frame = shown;
            }
            _hasFrame = true;
            double now = Time.realtimeSinceStartupAsDouble;
            _fps.Mark(now);
            _frameReceivedAt = _mainMjpeg != null && _mainMjpeg.HasPresentedFrame
                ? _mainMjpeg.LastFrameReceivedAt
                : now;
            if (displayImage != null)
            {
                displayImage.color = Color.white;
                displayImage.texture = frame;
            }
            if (_presenter != null) _presenter.SetMainTexture(frame);
            RefreshStats();
        }

        private void OnPipFrame(Texture2D frame)
        {
            if (_presenter != null) _presenter.SetPipTexture(frame);
        }

        private void ShowNoSignal()
        {
            _hasFrame = false;
            Texture tex = noSignalTexture != null ? (Texture)noSignalTexture : Grey();
            if (displayImage != null)
            {
                displayImage.color = new Color(0.02f, 0.03f, 0.06f, 1f);
                displayImage.texture = tex;
            }
            if (_presenter != null)
            {
                _presenter.SetMainTexture(null);
                _presenter.SetStats(StreamOverlay.Format(0f, 0f, false));
            }
        }

        private void ApplyLabel()
        {
            string label;
            if (_offline) label = "In-headset offline";
            else if (_plan == null || _plan.Views.Count == 0) label = "No cameras";
            else label = _plan.Views[Mathf.Clamp(_mainIndex, 0, _plan.Views.Count - 1)].Label;
            if (cameraNameOverlay != null) cameraNameOverlay.text = label;
        }

        private void RefreshStats()
        {
            if (_presenter == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            float age = _hasFrame ? StreamOverlay.AgeMilliseconds(_frameReceivedAt, now) : 0f;
            _presenter.SetStats(StreamOverlay.Format(_fps.Fps(now), age, _hasFrame));
        }

        private Texture2D Grey()
        {
            if (_grey != null) return _grey;
            _grey = new Texture2D(4, 4, TextureFormat.RGB24, false, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.2f, 0.2f, 0.2f);
            _grey.SetPixels(pixels);
            _grey.Apply(false, false);
            return _grey;
        }
    }
}
