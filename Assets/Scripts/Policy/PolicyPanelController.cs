using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Policy
{
    /// <summary>
    /// World-space policy panel. The safety decisions live in
    /// <see cref="PolicyController"/> so they can be tested without a scene.
    /// This view is created at runtime when a robot is picked, which keeps it
    /// off the shared HUD.
    /// </summary>
    public class PolicyPanelController : MonoBehaviour
    {
        private const int ServePort = 8000;

        private PolicyController _controller;
        private TeleopController _teleop;
        private PolicySessionKind _sessionKind = PolicySessionKind.Real;
        private Font _font;
        private Text _listText;
        private Text _statusText;
        private Text _gateText;
        private Text _detailText;
        private Image _superviseImage;
        private bool _uiSupervise;
        private string _detail = "";
        private float _refreshTimer;

        /// <summary>
        /// Garage status <c>simulated</c> (or <c>sim</c>) is a sim session.
        /// Every other status, including a missing one, is real.
        /// </summary>
        public static PolicySessionKind SessionKindForStatus(string status)
        {
            if (string.Equals(status, "simulated", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "sim", StringComparison.OrdinalIgnoreCase))
                return PolicySessionKind.Sim;
            return PolicySessionKind.Real;
        }

        public static PolicyPanelController FindOrCreate(TeleopController teleop)
        {
            var existing = FindObjectOfType<PolicyPanelController>();
            if (existing != null)
            {
                if (teleop != null) existing.Attach(teleop);
                return existing;
            }

            var go = new GameObject("PolicyPanel");
            var panel = go.AddComponent<PolicyPanelController>();
            if (teleop != null) panel.Attach(teleop);
            return panel;
        }

        /// <summary>
        /// Stores sim vs real before <see cref="TeleopController.StartTeleop"/>.
        /// The session-started hook binds the profile after any running policy
        /// has handed back on the previous robot.
        /// </summary>
        public void PrepareSession(TeleopController teleop, PolicySessionKind kind)
        {
            _sessionKind = kind == PolicySessionKind.Unspecified ? PolicySessionKind.Real : kind;
            Attach(teleop);
        }

        public void Attach(TeleopController teleop)
        {
            if (_teleop == teleop) return;
            Detach();
            _teleop = teleop;
            if (_teleop == null) return;
            _teleop.EmergencyStopChanged += OnEmergencyStop;
            _teleop.TeleopSessionStarted += OnSessionStarted;
            _teleop.TeleopSessionStopped += OnSessionStopped;
        }

        private void Awake()
        {
            _controller = new PolicyController();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
        }

        private void Start()
        {
            PlaceInFrontOfCamera();
        }

        private void OnDestroy()
        {
            Detach();
        }

        private void Update()
        {
            if (_controller == null) return;
            bool hardware = false;
            try
            {
                hardware = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                    || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) > 0.5f;
            }
            catch (Exception)
            {
                hardware = false;
            }
            _controller.SetSuperviseHeld(_uiSupervise || hardware);

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < 0.1f) return;
            _refreshTimer = 0f;
            RefreshView();
        }

        private void OnEmergencyStop(bool active)
        {
            _controller?.NotifyEmergencyStop(active);
            RefreshView();
        }

        private void OnSessionStopped()
        {
            if (_controller != null && _controller.RunState == PolicyRunState.Running)
                _controller.Stop();
            RefreshView();
        }

        private void OnSessionStarted(RobotProfile profile)
        {
            if (profile == null || _controller == null) return;
            IRobotLink link = _teleop != null && _teleop.RobotLink != null
                ? _teleop.RobotLink
                : RosBridgeLink.Instance;
            _controller.Bind(profile, link, _sessionKind);
            PlaceInFrontOfCamera();
            RefreshView();
        }

        private void Detach()
        {
            if (_teleop == null) return;
            _teleop.EmergencyStopChanged -= OnEmergencyStop;
            _teleop.TeleopSessionStarted -= OnSessionStarted;
            _teleop.TeleopSessionStopped -= OnSessionStopped;
            _teleop = null;
        }

        private void OnRefresh()
        {
            StartCoroutine(FetchModels());
        }

        private void OnNext()
        {
            _controller.SelectNext();
            _detail = "Selected " + (_controller.SelectedId ?? "—");
            RefreshView();
        }

        private void OnLoad()
        {
            PolicyCommandResult result = _controller.Load();
            _detail = result.Accepted
                ? "Load sent for " + _controller.LoadedCheckpoint
                : result.Reason;
            RefreshView();
            if (result.Accepted)
                StartCoroutine(PostServeLoad(_controller.LoadedCheckpoint));
        }

        private void OnConfirm()
        {
            _controller.Confirm();
            _detail = "Start confirmed";
            RefreshView();
        }

        private void OnStartPolicy()
        {
            PolicyCommandResult result = _controller.Start();
            _detail = result.Accepted ? "Policy running" : result.Reason;
            RefreshView();
        }

        private void OnStopPolicy()
        {
            _controller.Stop();
            _detail = "Teleop + zero";
            RefreshView();
        }

        private void OnSupervise(bool held)
        {
            _uiSupervise = held;
            if (_controller != null)
                _controller.SetSuperviseHeld(held || HardwareSupervise());
            RefreshView();
        }

        private bool HardwareSupervise()
        {
            try
            {
                return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                    || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) > 0.5f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private IEnumerator FetchModels()
        {
            string origin = ServeOrigin();
            if (string.IsNullOrEmpty(origin))
            {
                _controller.UseParamFallback();
                _detail = "No serve URL — param list";
                RefreshView();
                yield break;
            }

            using (var request = UnityWebRequest.Get(origin + "/models"))
            {
                request.timeout = 3;
                yield return request.SendWebRequest();
                if (this == null) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _controller.UseParamFallback();
                    _detail = "serve /models unavailable — param list";
                }
                else
                {
                    _controller.ApplyServeModelsJson(request.downloadHandler.text);
                    _detail = "Models from serve";
                }
            }
            RefreshView();
        }

        private IEnumerator PostServeLoad(string checkpoint)
        {
            string origin = ServeOrigin();
            if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(checkpoint))
                yield break;

            string url = origin + "/load_model?model_path=" + UnityWebRequest.EscapeURL(checkpoint);
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (this == null)
            {
                request.Dispose();
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
                _detail = "Serve load failed. /policy/load was published; policy_node may ignore it until restart.";
            else
                _detail = "Serve loaded " + checkpoint;
            request.Dispose();
            RefreshView();
        }

        private string ServeOrigin()
        {
            string configured = _controller != null && _controller.Profile != null
                ? _controller.Profile.ServeBaseUrl
                : null;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            var connection = FindObjectOfType<ConnectionManager>();
            if (connection != null && !string.IsNullOrEmpty(connection.CurrentIp))
                return "http://" + connection.CurrentIp + ":" + ServePort;
            return null;
        }

        private void RefreshView()
        {
            if (_controller == null) return;
            if (_listText != null) _listText.text = _controller.FormatPolicyList();
            if (_statusText != null) _statusText.text = _controller.FormatStatus();
            if (_gateText != null) _gateText.text = _controller.FormatGate();
            if (_detailText != null) _detailText.text = _detail ?? "";
            if (_superviseImage != null)
            {
                _superviseImage.color = _controller.SuperviseHeld
                    ? OhhoTheme.Cyan
                    : (_controller.RequiresSupervise ? new Color(0.75f, 0.16f, 0.22f) : OhhoTheme.Violet);
            }
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = cam;

            Vector3 position = cam.transform.position + cam.transform.forward * 0.9f + cam.transform.right * 0.46f;
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(cam.transform.position - position);
        }

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            gameObject.AddComponent<GraphicRaycaster>();

            var root = GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(680f, 560f);
            transform.localScale = Vector3.one * 0.001f;

            var card = new GameObject("Card");
            card.transform.SetParent(transform, false);
            var cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(OhhoTheme.Surface.r, OhhoTheme.Surface.g, OhhoTheme.Surface.b, 0.94f);
            Stretch(card.GetComponent<RectTransform>());

            var accent = new GameObject("Accent");
            accent.transform.SetParent(card.transform, false);
            var accentImage = accent.AddComponent<Image>();
            accentImage.color = OhhoTheme.Cyan;
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 8f);
            accentRect.anchoredPosition = Vector2.zero;

            var parent = card.GetComponent<RectTransform>();
            MakeText(parent, "Title", "Policy", new Vector2(0f, 230f), new Vector2(620f, 40f), 28, TextAnchor.MiddleLeft, Color.white);
            _listText = MakeText(parent, "List", "No policies", new Vector2(0f, 120f), new Vector2(620f, 150f), 22, TextAnchor.UpperLeft, Color.white);
            _statusText = MakeText(parent, "Status", "", new Vector2(0f, 10f), new Vector2(620f, 70f), 20, TextAnchor.UpperLeft, OhhoTheme.Cyan);
            _gateText = MakeText(parent, "Gate", "", new Vector2(0f, -50f), new Vector2(620f, 36f), 20, TextAnchor.MiddleLeft, OhhoTheme.VioletLite);
            _detailText = MakeText(parent, "Detail", "", new Vector2(0f, -84f), new Vector2(620f, 28f), 16, TextAnchor.MiddleLeft, OhhoTheme.Muted);

            MakeButton(parent, "Refresh", new Vector2(-230f, -140f), new Vector2(140f, 44f), OnRefresh, OhhoTheme.Cyan);
            MakeButton(parent, "Next", new Vector2(-70f, -140f), new Vector2(140f, 44f), OnNext, OhhoTheme.Cyan);
            MakeButton(parent, "Load", new Vector2(90f, -140f), new Vector2(140f, 44f), OnLoad, OhhoTheme.Cyan);
            MakeButton(parent, "Confirm", new Vector2(-230f, -196f), new Vector2(140f, 44f), OnConfirm, OhhoTheme.Violet);
            MakeButton(parent, "Start", new Vector2(-70f, -196f), new Vector2(140f, 44f), OnStartPolicy, new Color(0.2f, 0.75f, 0.45f));
            MakeButton(parent, "Stop", new Vector2(90f, -196f), new Vector2(140f, 44f), OnStopPolicy, new Color(0.75f, 0.2f, 0.24f));

            var hold = MakeButton(parent, "Hold to supervise  (L index)", new Vector2(0f, -256f), new Vector2(460f, 52f), null, new Color(0.75f, 0.16f, 0.22f));
            _superviseImage = hold.GetComponent<Image>();
            var trigger = hold.gameObject.AddComponent<EventTrigger>();
            AddHold(trigger, EventTriggerType.PointerDown, true);
            AddHold(trigger, EventTriggerType.PointerUp, false);
            AddHold(trigger, EventTriggerType.PointerExit, false);

            MakeText(parent, "Help",
                "Real: hold supervise. Release returns to teleop and zeros. E-stop wins.",
                new Vector2(0f, -300f), new Vector2(620f, 28f), 16, TextAnchor.MiddleLeft, OhhoTheme.Muted);

            if (FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<EventSystem>();
                events.AddComponent<StandaloneInputModule>();
            }
        }

        private Text MakeText(RectTransform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return text;
        }

        private Button MakeButton(RectTransform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction click, Color color)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (click != null) button.onClick.AddListener(click);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = MakeText(rect, "Label", label, Vector2.zero, size, 18, TextAnchor.MiddleCenter, new Color(0.05f, 0.07f, 0.12f, 1f));
            text.raycastTarget = false;
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void AddHold(EventTrigger trigger, EventTriggerType type, bool held)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => OnSupervise(held));
            trigger.triggers.Add(entry);
        }
    }
}
