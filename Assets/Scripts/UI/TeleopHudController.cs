using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Recording;
using OmniBot.VR.Telemetry;

namespace OmniBot.VR.UI
{
    /// <summary>
    /// The teleop page of the unified OhhO screen — everything the operator needs
    /// while driving a robot, on one surface:
    ///
    ///   - Connection row (robot IP / port / Connect / Disconnect + status dot)
    ///   - Live telemetry summary (odom pose, velocity, arm joints, control mode)
    ///   - Camera feed (RawImage driven by <see cref="Video.CameraFeedController"/>)
    ///   - Dataset recording controls (Start / Stop&amp;Save / Discard, live timer)
    ///   - Control hints for the active robot profile
    ///   - Back-to-garage navigation
    ///
    /// All subscriptions ride on top of <see cref="ConnectionManager"/>'s baseline
    /// topic set; UI refresh is rate-limited to keep the Quest GPU free for
    /// rendering.
    /// </summary>
    public class TeleopHudController : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private TMP_InputField ipField;
        [SerializeField] private TMP_InputField portField;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Image connectionDot;
        [SerializeField] private TMP_Text connectionStatusText;

        [Header("Telemetry")]
        [SerializeField] private TMP_Text poseText;
        [SerializeField] private TMP_Text velocityText;
        [SerializeField] private TMP_Text armText;
        [SerializeField] private TMP_Text modeText;
        [SerializeField] private TMP_Text telemetryBody;
        [SerializeField] private Button telemetryModeButton;
        [SerializeField] private TMP_Text telemetryModeLabel;

        [Header("Camera")]
        [SerializeField] private Button cycleCameraButton;
        [SerializeField] private Video.CameraFeedController cameraFeed;

        [Header("Recording")]
        [SerializeField] private Button startRecordingButton;
        [SerializeField] private Button stopSaveButton;
        [SerializeField] private Button discardButton;
        [SerializeField] private TMP_Text recordingStatusText;
        [SerializeField] private Image recordingDot;

        [Header("Export (upload episodes to the robot's VR bridge)")]
        [SerializeField] private Button exportButton;
        [SerializeField] private TMP_Text exportStatusText;
        [SerializeField] private int bridgeHttpPort = 8765;

        [Header("Robot / navigation")]
        [SerializeField] private TMP_Text robotNameText;
        [SerializeField] private TMP_Text hintsText;
        [SerializeField] private Button backButton;

        // ── State ─────────────────────────────────────────────────────────────
        private App.OhhoVrApp _app;
        private Control.TeleopController _teleop;

        readonly TelemetryHudComposer _composer = new TelemetryHudComposer();
        readonly HashSet<string> _subscribed = new HashSet<string>();
        readonly StringBuilder _poseSb = new StringBuilder(96);
        readonly StringBuilder _velSb = new StringBuilder(96);
        readonly StringBuilder _armSb = new StringBuilder(96);
        readonly StringBuilder _modeSb = new StringBuilder(64);
        RobotProfile _boundProfile;
        bool _coreSubscribed;
        bool _profileBound;
        char[] _bodyChars = new char[4096];
        char[] _poseChars = new char[128];
        char[] _velChars = new char[128];
        char[] _armChars = new char[128];
        char[] _modeChars = new char[64];
        int _bodyCharsLen = -1;
        int _poseCharsLen = -1;
        int _velCharsLen = -1;
        int _armCharsLen = -1;
        int _modeCharsLen = -1;

        private static readonly Color ConnectedColor = new Color(0.18f, 0.85f, 0.45f);
        private static readonly Color DisconnectedColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color RecordingColor = new Color(1f, 0.2f, 0.2f);
        private static readonly Color IdleDotColor = new Color(0.45f, 0.45f, 0.5f);

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _app = FindObjectOfType<App.OhhoVrApp>();
            _teleop = FindObjectOfType<Control.TeleopController>();
        }

        private void OnEnable()
        {
            if (connectButton != null) connectButton.onClick.AddListener(OnConnectClicked);
            if (disconnectButton != null) disconnectButton.onClick.AddListener(OnDisconnectClicked);
            if (cycleCameraButton != null) cycleCameraButton.onClick.AddListener(OnCycleCamera);
            if (startRecordingButton != null) startRecordingButton.onClick.AddListener(OnStartRecording);
            if (stopSaveButton != null) stopSaveButton.onClick.AddListener(OnStopSave);
            if (discardButton != null) discardButton.onClick.AddListener(OnDiscard);
            if (exportButton != null) exportButton.onClick.AddListener(OnExportAll);
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            EnsureTelemetryWidgets();
            if (telemetryModeButton != null) telemetryModeButton.onClick.AddListener(OnTelemetryMode);

            var conn = ConnectionManager.Instance;
            if (conn != null) conn.OnConnectionStateChanged += OnConnectionState;

            EnsureSubscriptions();
            RefreshTelemetryUi();

            // Pre-fill the connection fields from the saved preferences.
            if (conn != null)
            {
                if (ipField != null) ipField.text = conn.CurrentIp;
                if (portField != null) portField.text = conn.CurrentPort.ToString();
            }

            RefreshRobotLabel();
            OnConnectionState(conn != null && conn.IsConnected);
            RefreshRecordingUi();
        }

        private void OnDisable()
        {
            if (connectButton != null) connectButton.onClick.RemoveListener(OnConnectClicked);
            if (disconnectButton != null) disconnectButton.onClick.RemoveListener(OnDisconnectClicked);
            if (cycleCameraButton != null) cycleCameraButton.onClick.RemoveListener(OnCycleCamera);
            if (startRecordingButton != null) startRecordingButton.onClick.RemoveListener(OnStartRecording);
            if (stopSaveButton != null) stopSaveButton.onClick.RemoveListener(OnStopSave);
            if (discardButton != null) discardButton.onClick.RemoveListener(OnDiscard);
            if (exportButton != null) exportButton.onClick.RemoveListener(OnExportAll);
            if (backButton != null) backButton.onClick.RemoveListener(OnBack);
            if (telemetryModeButton != null) telemetryModeButton.onClick.RemoveListener(OnTelemetryMode);

            var conn = ConnectionManager.Instance;
            if (conn != null) conn.OnConnectionStateChanged -= OnConnectionState;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (cameraFeed != null)
            {
                _composer.NoteVideo(
                    cameraFeed.HasVideoFrame,
                    cameraFeed.LastVideoFrameTime,
                    cameraFeed.CurrentCameraTopic,
                    now);
            }

            ApplyHardCaps();
            if (!_composer.ConsumeFrame(Time.unscaledDeltaTime, now)) return;
            EnsureSubscriptions();
            PaintTelemetry(now);
            RefreshRecordingUi();
        }

        // ── Connection ────────────────────────────────────────────────────────

        private void OnConnectClicked()
        {
            string ip = ipField != null ? ipField.text.Trim() : "192.168.1.101";
            int port = 9090;
            if (portField != null) int.TryParse(portField.text.Trim(), out port);
            if (string.IsNullOrEmpty(ip)) ip = "192.168.1.101";

            SetConnectionLabel("Connecting…");
            ConnectionManager.Instance.Connect(ip, port);
        }

        private void OnDisconnectClicked()
        {
            ConnectionManager.Instance.Disconnect();
        }

        private void OnConnectionState(bool connected)
        {
            if (connectionDot != null)
                connectionDot.color = connected ? ConnectedColor : DisconnectedColor;
            SetConnectionLabel(connected ? "Connected" : "Disconnected");
            if (connectButton != null) connectButton.interactable = !connected;
            if (disconnectButton != null) disconnectButton.interactable = connected;
        }

        private void SetConnectionLabel(string s)
        {
            if (connectionStatusText != null) connectionStatusText.text = s;
        }

        // ── Telemetry ─────────────────────────────────────────────────────────

        private void OnTelemetryMode()
        {
            _composer.Expanded = !_composer.Expanded;
            if (telemetryModeLabel != null)
                telemetryModeLabel.text = _composer.Expanded ? "Expanded" : "Compact";
            RefreshTelemetryUi();
        }

        private void EnsureSubscriptions()
        {
            var ros = ROSBridgeClient.Instance;
            if (ros == null) return;
            if (!_coreSubscribed)
            {
                Sub(ros, TelemetryTopics.Telemetry, TelemetryTopics.StringType, OnTelemetry);
                Sub(ros, TelemetryTopics.SafetyState, TelemetryTopics.StringType, OnSafety);
                Sub(ros, TelemetryTopics.HeartbeatEcho, TelemetryTopics.StringType, OnHeartbeatEcho);
                Sub(ros, RobotConfig.TOPIC_CONTROL_MODE_ACTIVE, "std_msgs/String", OnBaseMode);
                Sub(ros, RobotConfig.TOPIC_ARM_MODE_ACTIVE, "std_msgs/String", OnArmMode);
                _coreSubscribed = true;
            }

            RobotProfile profile = _teleop != null ? _teleop.Profile : null;
            if (_profileBound && ReferenceEquals(profile, _boundProfile)) return;
            _boundProfile = profile;
            _profileBound = true;
            RobotTopics topics = profile != null ? profile.Topics : null;
            string odom = topics != null && !string.IsNullOrEmpty(topics.Odom) ? topics.Odom : RobotConfig.TOPIC_ODOM;
            string imu = topics != null && !string.IsNullOrEmpty(topics.Imu) ? topics.Imu : RobotConfig.TOPIC_IMU;
            string joints = topics != null && !string.IsNullOrEmpty(topics.JointStates) ? topics.JointStates : RobotConfig.TOPIC_ARM_STATES;
            Sub(ros, odom, "nav_msgs/Odometry", OnOdom);
            Sub(ros, imu, "sensor_msgs/Imu", OnImu);
            Sub(ros, joints, "sensor_msgs/JointState", OnJoints);
        }

        private void Sub(ROSBridgeClient ros, string topic, string type, System.Action<string> callback)
        {
            if (ros == null || string.IsNullOrEmpty(topic) || callback == null) return;
            if (!_subscribed.Add(topic)) return;
            ros.Subscribe(topic, type, callback);
        }

        private void OnTelemetry(string json)
        {
            _composer.IngestTelemetry(json, Time.realtimeSinceStartupAsDouble);
        }

        private void OnSafety(string json)
        {
            _composer.IngestSafety(json, Time.realtimeSinceStartupAsDouble);
        }

        private void OnHeartbeatEcho(string json)
        {
            _composer.IngestHeartbeatEcho(json, Time.realtimeSinceStartupAsDouble);
        }

        private void OnBaseMode(string json)
        {
            _composer.NoteRawBaseMode(ReadStringData(json), Time.realtimeSinceStartupAsDouble);
        }

        private void OnArmMode(string json)
        {
            _composer.NoteRawArmMode(ReadStringData(json), Time.realtimeSinceStartupAsDouble);
        }

        private void OnOdom(string json)
        {
            OdometryMsg odom;
            try { odom = JsonConvert.DeserializeObject<OdometryMsg>(json); }
            catch { return; }
            if (odom == null) return;
            bool hasPose = odom.pose != null && odom.pose.pose != null;
            bool hasTwist = odom.twist != null && odom.twist.twist != null;
            double x = 0, y = 0, qx = 0, qy = 0, qz = 0, qw = 1, vx = 0, vy = 0, wz = 0;
            if (hasPose)
            {
                if (odom.pose.pose.position != null)
                {
                    x = odom.pose.pose.position.x;
                    y = odom.pose.pose.position.y;
                }
                if (odom.pose.pose.orientation != null)
                {
                    qx = odom.pose.pose.orientation.x;
                    qy = odom.pose.pose.orientation.y;
                    qz = odom.pose.pose.orientation.z;
                    qw = odom.pose.pose.orientation.w;
                }
            }
            if (hasTwist)
            {
                if (odom.twist.twist.linear != null)
                {
                    vx = odom.twist.twist.linear.x;
                    vy = odom.twist.twist.linear.y;
                }
                if (odom.twist.twist.angular != null)
                    wz = odom.twist.twist.angular.z;
            }
            _composer.NoteRawBase(x, y, qx, qy, qz, qw, vx, vy, wz, hasPose, hasTwist, Time.realtimeSinceStartupAsDouble);
        }

        private void OnImu(string json)
        {
            ImuMsg imu;
            try { imu = JsonConvert.DeserializeObject<ImuMsg>(json); }
            catch { return; }
            if (imu == null || imu.orientation == null) return;
            double ax = imu.linear_acceleration != null ? imu.linear_acceleration.x : 0;
            double ay = imu.linear_acceleration != null ? imu.linear_acceleration.y : 0;
            double az = imu.linear_acceleration != null ? imu.linear_acceleration.z : 0;
            _composer.NoteRawImu(
                imu.orientation.x, imu.orientation.y, imu.orientation.z, imu.orientation.w,
                ax, ay, az, Time.realtimeSinceStartupAsDouble);
        }

        private void OnJoints(string json)
        {
            JointStateMsg msg;
            try { msg = JsonConvert.DeserializeObject<JointStateMsg>(json); }
            catch { return; }
            if (msg == null || msg.position == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            _composer.BeginRawJoints(now);
            int n = msg.position.Length;
            if (n > 16) n = 16;
            for (int i = 0; i < n; i++)
            {
                bool hasVel = msg.velocity != null && i < msg.velocity.Length;
                bool hasEff = msg.effort != null && i < msg.effort.Length;
                string name = msg.name != null && i < msg.name.Length ? msg.name[i] : null;
                _composer.AddRawJoint(
                    name,
                    msg.position[i],
                    hasVel ? msg.velocity[i] : 0,
                    hasEff ? msg.effort[i] : 0,
                    hasVel,
                    hasEff);
            }
        }

        static string ReadStringData(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                JToken data = JObject.Parse(json)["data"];
                if (data == null || data.Type == JTokenType.Null) return null;
                return data.ToString();
            }
            catch
            {
                return null;
            }
        }

        private void ApplyHardCaps()
        {
            RobotProfile profile = _teleop != null ? _teleop.Profile : null;
            double lin = profile != null && profile.MaxLinVel > 0f ? profile.MaxLinVel : RobotConfig.MAX_LINEAR_VEL;
            double ang = profile != null && profile.MaxAngVel > 0f ? profile.MaxAngVel : RobotConfig.MAX_ANGULAR_VEL;
            _composer.SetHardCaps(lin, ang);
            _composer.SetArmPresent(profile == null || profile.HasArm);
        }

        private void RefreshTelemetryUi()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            ApplyHardCaps();
            _composer.Write(now);
            PaintTelemetry(now);
        }

        private void PaintTelemetry(double now)
        {
            ApplyText(telemetryBody, _composer.Body, ref _bodyChars, ref _bodyCharsLen);
            _composer.WriteLegacy(now, _poseSb, _velSb, _armSb, _modeSb);
            ApplyText(poseText, _poseSb, ref _poseChars, ref _poseCharsLen);
            ApplyText(velocityText, _velSb, ref _velChars, ref _velCharsLen);
            ApplyText(armText, _armSb, ref _armChars, ref _armCharsLen);
            ApplyText(modeText, _modeSb, ref _modeChars, ref _modeCharsLen);
        }

        static void ApplyText(TMP_Text label, StringBuilder sb, ref char[] cache, ref int cacheLen)
        {
            if (label == null || sb == null) return;
            int n = sb.Length;
            if (n == cacheLen && Same(sb, cache, n)) return;
            if (cache == null || cache.Length < n)
                cache = new char[n < 256 ? 256 : n];
            sb.CopyTo(0, cache, 0, n);
            cacheLen = n;
            label.richText = true;
            label.SetCharArray(cache, 0, n);
        }

        static bool Same(StringBuilder sb, char[] cache, int n)
        {
            if (cache == null) return false;
            for (int i = 0; i < n; i++)
            {
                if (sb[i] != cache[i]) return false;
            }
            return true;
        }

        private void EnsureTelemetryWidgets()
        {
            if (telemetryBody != null) return;
            var panel = new GameObject("Telemetry V2", typeof(RectTransform));
            panel.transform.SetParent(transform, false);
            var panelRt = (RectTransform)panel.transform;
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(680f, 860f);
            panelRt.anchoredPosition = new Vector2(40f, 0f);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.06f, 0.10f, 0.94f);

            var textGo = new GameObject("Telemetry Body", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 12f);
            textRt.offsetMax = new Vector2(-12f, -68f);
            telemetryBody = textGo.AddComponent<TextMeshProUGUI>();
            telemetryBody.fontSize = 16f;
            telemetryBody.alignment = TextAlignmentOptions.TopLeft;
            telemetryBody.enableWordWrapping = true;
            telemetryBody.richText = true;
            telemetryBody.color = Color.white;
            telemetryBody.raycastTarget = false;
            if (poseText != null && poseText.font != null) telemetryBody.font = poseText.font;

            var buttonGo = new GameObject("Telemetry Mode", typeof(RectTransform));
            buttonGo.transform.SetParent(panel.transform, false);
            var buttonRt = (RectTransform)buttonGo.transform;
            buttonRt.anchorMin = new Vector2(1f, 1f);
            buttonRt.anchorMax = new Vector2(1f, 1f);
            buttonRt.pivot = new Vector2(1f, 1f);
            buttonRt.sizeDelta = new Vector2(180f, 44f);
            buttonRt.anchoredPosition = new Vector2(-12f, -12f);
            var buttonImage = buttonGo.AddComponent<Image>();
            buttonImage.color = new Color(0f, 0.831f, 1f, 1f);
            telemetryModeButton = buttonGo.AddComponent<Button>();
            telemetryModeButton.targetGraphic = buttonImage;

            var labelGo = new GameObject("Text", typeof(RectTransform));
            labelGo.transform.SetParent(buttonGo.transform, false);
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            telemetryModeLabel = labelGo.AddComponent<TextMeshProUGUI>();
            telemetryModeLabel.text = "Expanded";
            telemetryModeLabel.fontSize = 20f;
            telemetryModeLabel.alignment = TextAlignmentOptions.Center;
            telemetryModeLabel.color = Color.black;
            telemetryModeLabel.raycastTarget = false;
            if (telemetryBody.font != null) telemetryModeLabel.font = telemetryBody.font;
        }

        // ── Camera ────────────────────────────────────────────────────────────

        private void OnCycleCamera()
        {
            if (cameraFeed != null) cameraFeed.CycleCamera();
        }

        // ── Recording ─────────────────────────────────────────────────────────

        private void OnStartRecording()
        {
            EpisodeManager.Instance.StartEpisode();
            RefreshRecordingUi();
        }

        private void OnStopSave()
        {
            EpisodeManager.Instance.StopEpisode();
            RefreshRecordingUi();
        }

        private void OnDiscard()
        {
            EpisodeManager.Instance.DiscardEpisode();
            RefreshRecordingUi();
        }

        private void RefreshRecordingUi()
        {
            var em = EpisodeManager.Instance;
            bool recording = em.State == EpisodeManager.EpisodeState.Recording || em.State == EpisodeManager.EpisodeState.Paused;

            if (recordingDot != null)
                recordingDot.color = recording ? RecordingColor : IdleDotColor;
            if (recordingStatusText != null)
                recordingStatusText.text = recording
                    ? $"REC {em.CurrentEpisodeName}  {em.EpisodeDuration:F0}s"
                    : "Idle";
            if (startRecordingButton != null) startRecordingButton.interactable = !recording;
            if (stopSaveButton != null) stopSaveButton.interactable = recording;
            if (discardButton != null) discardButton.interactable = recording;
        }

        // ── Export (readme §7: upload episodes to the robot's VR bridge) ──────

        private void OnExportAll()
        {
            StartCoroutine(ExportAllEpisodes());
        }

        private System.Collections.IEnumerator ExportAllEpisodes()
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "recordings");
            if (!System.IO.Directory.Exists(dir))
            {
                ShowExportStatus("No recordings yet.");
                yield break;
            }

            string[] files = System.IO.Directory.GetFiles(dir, "*.jsonl");
            if (files.Length == 0)
            {
                ShowExportStatus("No recordings yet.");
                yield break;
            }

            ShowExportStatus($"Exporting {files.Length} episode(s)…");
            int succeeded = 0;
            foreach (string file in files)
            {
                yield return ExportSingleEpisode(file);
                succeeded++;
            }
            ShowExportStatus($"Exported {succeeded}/{files.Length} episodes.");
        }

        private System.Collections.IEnumerator ExportSingleEpisode(string filePath)
        {
            string robotIp = PlayerPrefs.GetString("robot_ip", "192.168.1.101");
            string endpoint = $"http://{robotIp}:{bridgeHttpPort}/upload_episode";

            byte[] fileData;
            try { fileData = System.IO.File.ReadAllBytes(filePath); }
            catch (System.Exception ex)
            {
                ShowExportStatus($"Read failed: {ex.Message}");
                yield break;
            }

            string filename = System.IO.Path.GetFileName(filePath);
            var form = new WWWForm();
            form.AddBinaryData("file", fileData, filename, "application/jsonl");

            using (var req = UnityEngine.Networking.UnityWebRequest.Post(endpoint, form))
            {
                req.timeout = 30;
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                    ShowExportStatus($"Export failed ({filename}): {req.error}");
            }
        }

        private Coroutine _exportStatusRoutine;
        private void ShowExportStatus(string message)
        {
            if (exportStatusText == null) return;
            exportStatusText.text = message;
            exportStatusText.gameObject.SetActive(true);
            if (_exportStatusRoutine != null) StopCoroutine(_exportStatusRoutine);
            _exportStatusRoutine = StartCoroutine(HideExportStatus(4f));
        }

        private System.Collections.IEnumerator HideExportStatus(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (exportStatusText != null) exportStatusText.gameObject.SetActive(false);
        }

        // ── Robot label / hints ───────────────────────────────────────────────

        private void RefreshRobotLabel()
        {
            var profile = _teleop != null ? _teleop.Profile : null;
            if (robotNameText != null)
                robotNameText.text = profile != null ? profile.Name : "No robot selected";
            if (hintsText != null && _teleop != null)
                hintsText.text = $"{_teleop.DriveHint}\n{_teleop.ManipHint}";
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void OnBack()
        {
            if (_app != null) _app.StopTeleopAndReturnToGarage();
        }
    }
}
