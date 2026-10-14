using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.MR;
using OmniBot.VR.Recording;
using OmniBot.VR.Video;

namespace OmniBot.VR.UI.Operate
{
    /// <summary>
    /// Single forward operate view: mode/safety bar, left telemetry, centre sim
    /// screen, right arm column, bottom controls legend. While it is active the
    /// screen stops billboarding; Recentre snaps it back to 1.6 m in front.
    ///
    /// The centre slot is the stand-in for OHH-209's sim screen. A
    /// <see cref="CameraFeedController"/> on that slot shows the feed until
    /// <c>SimScreenPresenter</c> is mounted on the same object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OperateDashboard : MonoBehaviour
    {
        public OperateModeCommands Commands { get; private set; }
        public int ModePublishAttempts { get; private set; }

        public TMP_Text RobotNameText;
        public TMP_InputField IpField;
        public TMP_InputField PortField;
        public Button ConnectButton;
        public Button DisconnectButton;
        public Image ConnectionDot;
        public TMP_Text ConnectionStatusText;
        public TMP_Text PoseText;
        public TMP_Text VelocityText;
        public TMP_Text ArmText;
        public TMP_Text ModeText;
        public TMP_Text OperateModeText;
        public Button CycleCameraButton;
        public CameraFeedController SimFeedController;
        public RawImage SimFeedImage;
        public Button StartRecordingButton;
        public Button StopSaveButton;
        public Button DiscardButton;
        public TMP_Text RecordingStatusText;
        public Image RecordingDot;
        public Button ExportButton;
        public TMP_Text ExportStatusText;
        public TMP_Text HintsText;
        public Button BackButton;

        public Button TeleopButton;
        public Button Nav2Button;
        public Button PolicyButton;
        public Button RecordButton;
        public Button EStopButton;
        public Button ResetButton;
        public Button RecentreButton;
        public GameObject PolicyConfirmRoot;
        public Button PolicyConfirmYesButton;
        public Button PolicyConfirmNoButton;

        private bool _built;
        private bool _wired;
        private float _refreshTimer;
        private TeleopController _teleop;
        private string _realmLabel;

        public static OperateDashboard Ensure(GameObject page)
        {
            if (page == null) return null;
            var dash = page.GetComponent<OperateDashboard>();
            if (dash == null) dash = page.AddComponent<OperateDashboard>();
            dash.Construct();
            return dash;
        }

        public static OperateDashboard Build(RectTransform page) => Ensure(page.gameObject);

        public void Bind(TeleopController teleop) => _teleop = teleop;

        public void SetRealmLabel(string label)
        {
            _realmLabel = label;
            RefreshReadout();
        }

        public void EnterOperation()
        {
            EnsurePlacementAnchor();
            SetPlacementLocked(true);
            Recentre();
        }

        public void ExitOperation() => SetPlacementLocked(false);

        public void Recentre()
        {
            var canvas = GetComponentInParent<Canvas>();
            var hud = canvas != null
                ? canvas.GetComponentInParent<SmoothHUD>()
                : GetComponentInParent<SmoothHUD>();
            if (hud != null)
            {
                hud.Recentre();
                if (canvas != null && canvas.transform != hud.transform && canvas.transform.IsChildOf(hud.transform))
                {
                    canvas.transform.localPosition = new Vector3(0f, 0f, OperateComfort.OperateDistanceMetres);
                    canvas.transform.localRotation = Quaternion.identity;
                }
                return;
            }

            var placer = canvas != null
                ? canvas.GetComponent<WorldSpaceUiPlacer>()
                : GetComponentInParent<WorldSpaceUiPlacer>();
            if (placer != null) placer.Recentre();
        }

        private void OnEnable() => Wire();

        private void OnDisable() => Unwire();

        private void Update()
        {
            if (!Application.isPlaying || !_built) return;
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < 0.1f) return;
            _refreshTimer = 0f;
            SyncRecordingFromManager();
            RefreshReadout();
        }

        private void Construct()
        {
            if (_built) return;
            _built = true;
            Commands = new OperateModeCommands();

            var have = Index(transform);
            var font = OperateComfort.BodyFontSize;

            var modeBar = Region(OperateLayoutSpec.ModeBarName, OperateLayoutSpec.ModeBar);
            var statusRow = Row(modeBar, "Mode Status Row", true);
            var actionRow = Row(modeBar, "Mode Action Row", false);

            var telemetry = Column(OperateLayoutSpec.TelemetryName, OperateLayoutSpec.Telemetry);
            var sim = Region(OperateLayoutSpec.SimScreenName, OperateLayoutSpec.SimScreen);
            var arm = Column(OperateLayoutSpec.ArmName, OperateLayoutSpec.Arm);
            var legend = Region(OperateLayoutSpec.LegendName, OperateLayoutSpec.Legend);

            OperateModeText = MakeChip(statusRow, "Operate Mode Text", "TELEOP");
            MakeChip(statusRow, "Operate Realm Text", "REAL");
            MakeChip(statusRow, "Operate E-Stop Text", "E-STOP OFF");
            MakeChip(statusRow, "Operate Deadman Text", "DEADMAN OPEN");
            MakeChip(statusRow, "Operate Link Text", "LINK DOWN");

            TeleopButton = MakeAction(actionRow, OperateLayoutSpec.TeleopButtonName, "Teleop");
            Nav2Button = MakeAction(actionRow, OperateLayoutSpec.Nav2ButtonName, "Nav2");
            PolicyButton = MakeAction(actionRow, OperateLayoutSpec.PolicyButtonName, "Policy");
            RecordButton = MakeAction(actionRow, OperateLayoutSpec.RecordButtonName, "Record");
            EStopButton = MakeAction(actionRow, OperateLayoutSpec.EStopButtonName, "E-Stop");
            ResetButton = MakeAction(actionRow, OperateLayoutSpec.ResetButtonName, "Reset");
            RecentreButton = MakeAction(actionRow, OperateLayoutSpec.RecentreButtonName, "Recentre");
            PaintDanger(EStopButton);

            telemetry.Add(MakeTitle(telemetry.Root, "Telemetry Title", "TELEMETRY"));
            RobotNameText = telemetry.AddText(AdoptText(have, "Robot Name", "Robot", font));
            PoseText = telemetry.AddText(AdoptText(have, "Pose Text", "x —    y —", font));
            VelocityText = telemetry.AddText(AdoptText(have, "Velocity Text", "vx —    vy —    yaw —", font));
            ModeText = telemetry.AddText(AdoptText(have, "Mode Text", "mode —", font));
            HintsText = telemetry.AddText(AdoptText(have, "Hints Text", "", font));
            HintsText.enableWordWrapping = true;
            Prefer(HintsText.rectTransform, 72f);
            telemetry.Add(MakeTitle(telemetry.Root, "Conn Title", "LINK"));
            IpField = telemetry.AddInput(AdoptInput(have, "IP Field", "192.168.1.101"));
            PortField = telemetry.AddInput(AdoptInput(have, "Port Field", "9090"));
            ConnectButton = telemetry.AddButton(AdoptButton(have, "Connect Button", "Connect"));
            DisconnectButton = telemetry.AddButton(AdoptButton(have, "Disconnect Button", "Disconnect"));
            ConnectionDot = telemetry.AddImage(AdoptImage(have, "Connection Dot", OperateComfort.Danger));
            ConnectionStatusText = telemetry.AddText(AdoptText(have, "Connection Status", "Disconnected", font));

            BuildSimSlot(sim, font);

            arm.Add(MakeTitle(arm.Root, "Arm Title", "ARM"));
            ArmText = arm.AddText(AdoptText(have, "Arm Text", "no arm data", font));
            ArmText.enableWordWrapping = true;
            Prefer(ArmText.rectTransform, 180f);
            arm.Add(MakeTitle(arm.Root, "Rec Title", "RECORDING"));
            RecordingDot = arm.AddImage(AdoptImage(have, "Recording Dot", new Color(0.45f, 0.45f, 0.5f, 1f)));
            RecordingStatusText = arm.AddText(AdoptText(have, "Recording Status", "Idle", font));
            StartRecordingButton = arm.AddButton(AdoptButton(have, "Start Recording", "Start Recording"));
            StopSaveButton = arm.AddButton(AdoptButton(have, "Stop & Save", "Stop & Save"));
            DiscardButton = arm.AddButton(AdoptButton(have, "Discard", "Discard"));
            ExportButton = arm.AddButton(AdoptButton(have, "Export Button", "Export to Robot"));
            ExportStatusText = arm.AddText(AdoptText(have, "Export Status", "", font));
            ExportStatusText.gameObject.SetActive(false);

            var legendText = CreateText("Controls Legend Text", legend, OperateLayoutSpec.LegendBody, font, OperateComfort.Text);
            legendText.alignment = TextAlignmentOptions.TopLeft;
            legendText.enableWordWrapping = true;
            Stretch(legendText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 8f), new Vector2(-220f, -8f));
            BackButton = AdoptButton(have, "Back Button", "< Garage");
            BackButton.transform.SetParent(legend, false);
            var backRt = BackButton.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(1f, 0.5f);
            backRt.anchorMax = new Vector2(1f, 0.5f);
            backRt.pivot = new Vector2(1f, 0.5f);
            backRt.sizeDelta = new Vector2(200f, 56f);
            backRt.anchoredPosition = new Vector2(-12f, 0f);

            BuildPolicyConfirm(font);
            RaiseFonts();
            Wire();
            RefreshReadout();
        }

        private void BuildSimSlot(RectTransform sim, float font)
        {
            var caption = CreateText("Sim Screen Caption", sim, "Sim screen", font, OperateComfort.Muted);
            caption.alignment = TextAlignmentOptions.Center;
            var capRt = caption.rectTransform;
            capRt.anchorMin = new Vector2(0f, 1f);
            capRt.anchorMax = new Vector2(1f, 1f);
            capRt.pivot = new Vector2(0.5f, 1f);
            capRt.sizeDelta = new Vector2(0f, font + 12f);
            capRt.anchoredPosition = new Vector2(0f, -8f);

            var feedGo = new GameObject("Camera Feed", typeof(RectTransform));
            feedGo.transform.SetParent(sim, false);
            SimFeedImage = feedGo.AddComponent<RawImage>();
            SimFeedImage.color = new Color(0.02f, 0.03f, 0.05f, 1f);
            SimFeedImage.raycastTarget = false;
            var feedRt = (RectTransform)feedGo.transform;
            feedRt.anchorMin = Vector2.zero;
            feedRt.anchorMax = Vector2.one;
            feedRt.offsetMin = new Vector2(12f, 72f);
            feedRt.offsetMax = new Vector2(-12f, -(font + 28f));

            var name = CreateText("Camera Name", sim, "camera", font, OperateComfort.Text);
            name.alignment = TextAlignmentOptions.Center;
            var nameRt = name.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.sizeDelta = new Vector2(0f, font + 8f);
            nameRt.anchoredPosition = new Vector2(0f, 64f);

            CycleCameraButton = CreateButton("Cycle Camera Button", sim, "Next camera");
            var cycleRt = CycleCameraButton.GetComponent<RectTransform>();
            cycleRt.anchorMin = new Vector2(0.5f, 0f);
            cycleRt.anchorMax = new Vector2(0.5f, 0f);
            cycleRt.pivot = new Vector2(0.5f, 0f);
            cycleRt.sizeDelta = new Vector2(240f, 52f);
            cycleRt.anchoredPosition = new Vector2(0f, 8f);

            SimFeedController = sim.gameObject.GetComponent<CameraFeedController>();
            if (SimFeedController == null) SimFeedController = sim.gameObject.AddComponent<CameraFeedController>();
            SimFeedController.AttachDisplay(SimFeedImage, name);
        }

        private void BuildPolicyConfirm(float font)
        {
            PolicyConfirmRoot = new GameObject(OperateLayoutSpec.PolicyConfirmName, typeof(RectTransform));
            PolicyConfirmRoot.transform.SetParent(transform, false);
            var rootRt = (RectTransform)PolicyConfirmRoot.transform;
            Stretch(rootRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dim = PolicyConfirmRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.78f);
            dim.raycastTarget = true;

            var box = new GameObject("Policy Confirm Box", typeof(RectTransform));
            box.transform.SetParent(PolicyConfirmRoot.transform, false);
            var boxRt = (RectTransform)box.transform;
            boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(760f, 280f);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(OperateComfort.Panel.r, OperateComfort.Panel.g, OperateComfort.Panel.b, 1f);

            var message = CreateText("Policy Confirm Text", box.transform,
                "Switch to policy? The robot will follow the learned policy.",
                font, OperateComfort.Text);
            message.alignment = TextAlignmentOptions.Center;
            message.enableWordWrapping = true;
            var msgRt = message.rectTransform;
            msgRt.anchorMin = new Vector2(0f, 1f);
            msgRt.anchorMax = new Vector2(1f, 1f);
            msgRt.pivot = new Vector2(0.5f, 1f);
            msgRt.sizeDelta = new Vector2(-48f, 120f);
            msgRt.anchoredPosition = new Vector2(0f, -24f);

            PolicyConfirmYesButton = CreateButton(OperateLayoutSpec.PolicyConfirmYesName, box.transform, "Run policy");
            PolicyConfirmNoButton = CreateButton(OperateLayoutSpec.PolicyConfirmNoName, box.transform, "Cancel");
            PlacePair(PolicyConfirmYesButton, PolicyConfirmNoButton);
            PaintAccent(PolicyConfirmYesButton, true);
            PolicyConfirmRoot.SetActive(false);
        }

        public void RefreshReadout()
        {
            if (Commands == null) return;
            float font = OperateComfort.BodyFontSize;

            SetChip("Operate Realm Text", RealmLabel(), OperateComfort.Text);
            SetChip("Operate Mode Text", Commands.ModeWireValue.ToUpperInvariant(), OperateComfort.Text);
            bool estop = Commands.EStopLatched || OperateSafetyLatch.UiEStopLatched
                || (_teleop != null && _teleop.IsGripEStopActive);
            SetChip("Operate E-Stop Text", estop ? "E-STOP ON" : "E-STOP OFF",
                estop ? OperateComfort.Danger : OperateComfort.Safe);

            bool both = _teleop != null && _teleop.BothGripsHeld;
            bool any = _teleop != null && _teleop.AnyGripHeld;
            var deadman = OperateReadout.Deadman(any, both);
            Color deadmanColor = deadman == OperateDeadman.Stop
                ? OperateComfort.Danger
                : deadman == OperateDeadman.Held ? OperateComfort.Safe : OperateComfort.Amber;
            SetChip("Operate Deadman Text", OperateReadout.DeadmanLabel(deadman), deadmanColor);

            bool linkUp = false;
            if (Application.isPlaying)
            {
                var conn = FindObjectOfType<ConnectionManager>();
                if (conn != null) linkUp = conn.IsConnected;
            }
            SetChip("Operate Link Text", linkUp ? "LINK UP" : "LINK DOWN",
                linkUp ? OperateComfort.Safe : OperateComfort.Danger);

            PaintMode(TeleopButton, Commands.Mode == OperateDriveMode.Teleop);
            PaintMode(Nav2Button, Commands.Mode == OperateDriveMode.Nav2);
            PaintMode(PolicyButton, Commands.Mode == OperateDriveMode.Policy);
            if (RecordButton != null)
            {
                var label = RecordButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = Commands.Recording ? "Recording" : "Record";
                    label.fontSize = Mathf.Max(label.fontSize, font);
                }
            }
            if (PolicyConfirmRoot != null)
                PolicyConfirmRoot.SetActive(Commands.PolicyConfirmationPending);
        }

        private string RealmLabel()
        {
            string host = null;
            if (Application.isPlaying)
            {
                var conn = FindObjectOfType<ConnectionManager>();
                if (conn != null) host = conn.CurrentIp;
            }
            var realm = OperateRealmRules.Resolve(_realmLabel, host);
            return realm == OperateRealm.Sim ? "SIM" : "REAL";
        }

        private void OnTeleop() => Commit(Commands.Request(OperateDriveMode.Teleop));
        private void OnNav2() => Commit(Commands.Request(OperateDriveMode.Nav2));
        private void OnPolicy() => Commit(Commands.Request(OperateDriveMode.Policy));

        private void OnConfirmPolicy() => Commit(Commands.ConfirmPolicy());

        private void OnCancelPolicy()
        {
            Commands.CancelPolicy();
            RefreshReadout();
        }

        private void Commit(bool applied)
        {
            if (!applied)
            {
                RefreshReadout();
                return;
            }

            bool gripStop = _teleop != null && _teleop.IsGripEStopActive;
            if (OperateSafetyRules.ModePublishAllowed(
                    Commands.EStopLatched || OperateSafetyLatch.UiEStopLatched, gripStop))
            {
                ModePublishAttempts++;
                if (_teleop != null) _teleop.RequestOperateMode(Commands.ModeWireValue);
            }
            RefreshReadout();
        }

        private void OnRecord()
        {
            if (!Application.isPlaying)
            {
                Commands.SetRecording(!Commands.Recording);
                RefreshReadout();
                return;
            }

            var em = EpisodeManager.Instance;
            bool recording = em.State == EpisodeManager.EpisodeState.Recording
                || em.State == EpisodeManager.EpisodeState.Paused;
            if (!recording) em.StartEpisode();
            else em.StopEpisode();
            Commands.SetRecording(!recording);
            RefreshReadout();
        }

        private void OnEStop()
        {
            Commands.LatchEStop();
            if (_teleop != null) _teleop.LatchOperateEStop();
            else OperateSafetyLatch.Latch();
            RefreshReadout();
        }

        private void OnReset()
        {
            if (_teleop != null && _teleop.BothGripsHeld) return;
            bool wasLatched = Commands.EStopLatched || OperateSafetyLatch.UiEStopLatched
                || (_teleop != null && _teleop.IsGripEStopActive);
            if (!Commands.TryReset(false)) return;
            if (!wasLatched)
            {
                RefreshReadout();
                return;
            }
            if (_teleop != null) _teleop.TryResetOperateEStop();
            else OperateSafetyLatch.Clear();
            RefreshReadout();
        }

        private void OnRecentre() => Recentre();

        private void SyncRecordingFromManager()
        {
            var em = FindObjectOfType<EpisodeManager>();
            if (em == null) return;
            bool recording = em.State == EpisodeManager.EpisodeState.Recording
                || em.State == EpisodeManager.EpisodeState.Paused;
            Commands.SetRecording(recording);
        }

        private void Wire()
        {
            if (_wired || TeleopButton == null) return;
            TeleopButton.onClick.AddListener(OnTeleop);
            Nav2Button.onClick.AddListener(OnNav2);
            PolicyButton.onClick.AddListener(OnPolicy);
            RecordButton.onClick.AddListener(OnRecord);
            EStopButton.onClick.AddListener(OnEStop);
            ResetButton.onClick.AddListener(OnReset);
            RecentreButton.onClick.AddListener(OnRecentre);
            if (PolicyConfirmYesButton != null) PolicyConfirmYesButton.onClick.AddListener(OnConfirmPolicy);
            if (PolicyConfirmNoButton != null) PolicyConfirmNoButton.onClick.AddListener(OnCancelPolicy);
            _wired = true;
        }

        private void Unwire()
        {
            if (!_wired) return;
            if (TeleopButton != null) TeleopButton.onClick.RemoveListener(OnTeleop);
            if (Nav2Button != null) Nav2Button.onClick.RemoveListener(OnNav2);
            if (PolicyButton != null) PolicyButton.onClick.RemoveListener(OnPolicy);
            if (RecordButton != null) RecordButton.onClick.RemoveListener(OnRecord);
            if (EStopButton != null) EStopButton.onClick.RemoveListener(OnEStop);
            if (ResetButton != null) ResetButton.onClick.RemoveListener(OnReset);
            if (RecentreButton != null) RecentreButton.onClick.RemoveListener(OnRecentre);
            if (PolicyConfirmYesButton != null) PolicyConfirmYesButton.onClick.RemoveListener(OnConfirmPolicy);
            if (PolicyConfirmNoButton != null) PolicyConfirmNoButton.onClick.RemoveListener(OnCancelPolicy);
            _wired = false;
        }

        private void EnsurePlacementAnchor()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (canvas.GetComponentInParent<SmoothHUD>() != null) return;
            if (canvas.GetComponent<WorldSpaceUiPlacer>() != null) return;
            var placer = canvas.gameObject.AddComponent<WorldSpaceUiPlacer>();
            placer.SetDistance(OperateComfort.OperateDistanceMetres);
        }

        private void SetPlacementLocked(bool locked)
        {
            var placers = GetComponentsInParent<WorldSpaceUiPlacer>(true);
            for (int i = 0; i < placers.Length; i++) placers[i].SetBillboardLocked(locked);
            var huds = GetComponentsInParent<SmoothHUD>(true);
            for (int i = 0; i < huds.Length; i++) huds[i].SetPlacementLocked(locked);
        }

        private void SetChip(string name, string value, Color color)
        {
            var chip = FindDeep(transform, name);
            if (chip == null) return;
            var tmp = chip.GetComponent<TMP_Text>();
            if (tmp == null) return;
            tmp.text = value;
            tmp.color = color;
            tmp.fontSize = Mathf.Max(tmp.fontSize, OperateComfort.BodyFontSize);
        }

        private void RaiseFonts()
        {
            float min = OperateComfort.BodyFontSize;
            var texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].fontSize < min)
                    texts[i].fontSize = min;
            }
        }

        private RectTransform Region(string name, OperateRect rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(rect.XMin, rect.YMin);
            rt.anchorMax = new Vector2(rect.XMax, rect.YMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.color = name == OperateLayoutSpec.SimScreenName
                ? new Color(0.015f, 0.02f, 0.03f, 1f)
                : OperateComfort.Panel;
            img.raycastTarget = true;
            return rt;
        }

        private RectTransform Row(Transform parent, string name, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, top ? 0.52f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0.48f);
            rt.offsetMin = new Vector2(8f, 4f);
            rt.offsetMax = new Vector2(-8f, -4f);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rt;
        }

        private ColumnBuilder Column(string name, OperateRect rect)
        {
            var rt = Region(name, rect);
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return new ColumnBuilder(rt);
        }

        private TMP_Text MakeChip(Transform row, string name, string value)
        {
            var go = new GameObject(name + " Chip", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var img = go.AddComponent<Image>();
            img.color = OperateComfort.Chip;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 36f;
            var tmp = CreateText(name, go.transform, value, OperateComfort.BodyFontSize, OperateComfort.Text);
            tmp.alignment = TextAlignmentOptions.Center;
            Stretch(tmp.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return tmp;
        }

        private Button MakeAction(Transform row, string name, string label)
        {
            var btn = CreateButton(name, row, label);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 44f;
            return btn;
        }

        private TMP_Text MakeTitle(Transform parent, string name, string label)
        {
            var tmp = CreateText(name, parent, label, OperateComfort.BodyFontSize, OperateComfort.Muted);
            tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }

        private static void PaintMode(Button button, bool active)
        {
            if (button == null) return;
            PaintAccent(button, active);
        }

        private static void PaintAccent(Button button, bool active)
        {
            var img = button.GetComponent<Image>();
            if (img != null) img.color = active ? OperateComfort.Accent : OperateComfort.Chip;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = active ? OperateComfort.AccentText : OperateComfort.Text;
        }

        private static void PaintDanger(Button button)
        {
            var img = button.GetComponent<Image>();
            if (img != null) img.color = OperateComfort.DangerButton;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = OperateComfort.DangerText;
        }

        private static void PlacePair(Button left, Button right)
        {
            var l = left.GetComponent<RectTransform>();
            var r = right.GetComponent<RectTransform>();
            l.anchorMin = new Vector2(0f, 0f);
            l.anchorMax = new Vector2(0f, 0f);
            l.pivot = new Vector2(0f, 0f);
            l.sizeDelta = new Vector2(320f, 56f);
            l.anchoredPosition = new Vector2(32f, 24f);
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(320f, 56f);
            r.anchoredPosition = new Vector2(-32f, 24f);
        }

        private TMP_Text AdoptText(Dictionary<string, Transform> have, string name, string fallback, float font)
        {
            Transform existing;
            if (have.TryGetValue(name, out existing) && existing != null)
            {
                have.Remove(name);
                var tmp = existing.GetComponent<TMP_Text>();
                if (tmp == null) tmp = existing.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = Mathf.Max(tmp.fontSize, font);
                if (tmp.GetComponentInParent<Button>() == null)
                    tmp.color = OperateComfort.Text;
                return tmp;
            }
            return CreateText(name, transform, fallback, font, OperateComfort.Text);
        }

        private Button AdoptButton(Dictionary<string, Transform> have, string name, string label)
        {
            Transform existing;
            if (have.TryGetValue(name, out existing) && existing != null)
            {
                have.Remove(name);
                var btn = existing.GetComponent<Button>();
                if (btn == null) btn = existing.gameObject.AddComponent<Button>();
                var tmp = existing.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.fontSize = Mathf.Max(tmp.fontSize, OperateComfort.BodyFontSize);
                return btn;
            }
            return CreateButton(name, transform, label);
        }

        private TMP_InputField AdoptInput(Dictionary<string, Transform> have, string name, string placeholder)
        {
            Transform existing;
            if (have.TryGetValue(name, out existing) && existing != null)
            {
                have.Remove(name);
                var field = existing.GetComponent<TMP_InputField>();
                if (field != null) return field;
            }
            return CreateInput(name, transform, placeholder);
        }

        private Image AdoptImage(Dictionary<string, Transform> have, string name, Color color)
        {
            Transform existing;
            if (have.TryGetValue(name, out existing) && existing != null)
            {
                have.Remove(name);
                var img = existing.GetComponent<Image>();
                if (img != null) return img;
            }
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var created = go.AddComponent<Image>();
            created.color = color;
            return created;
        }

        private Button CreateButton(string name, Transform parent, string label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = OperateComfort.Chip;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var tmp = CreateText("Text", go.transform, label, OperateComfort.BodyFontSize, OperateComfort.Text);
            tmp.alignment = TextAlignmentOptions.Center;
            Stretch(tmp.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return btn;
        }

        private TMP_Text CreateText(string name, Transform parent, string value, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        private TMP_InputField CreateInput(string name, Transform parent, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = OperateComfort.Chip;
            var field = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("Text Area", typeof(RectTransform));
            viewport.transform.SetParent(go.transform, false);
            var vrt = (RectTransform)viewport.transform;
            Stretch(vrt, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            viewport.AddComponent<RectMask2D>();
            var text = CreateText("Text", viewport.transform, "", OperateComfort.BodyFontSize, OperateComfort.Text);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ph = CreateText("Placeholder", viewport.transform, placeholder, OperateComfort.BodyFontSize, OperateComfort.Muted);
            ph.fontStyle = FontStyles.Italic;
            Stretch(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            field.textViewport = vrt;
            field.textComponent = text;
            field.placeholder = ph;
            return field;
        }

        private static void Prefer(RectTransform rt, float height)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Dictionary<string, Transform> Index(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == root) continue;
                if (!map.ContainsKey(all[i].name)) map.Add(all[i].name, all[i]);
            }
            return map;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            return null;
        }

        private sealed class ColumnBuilder
        {
            public readonly Transform Root;
            private int _index;

            public ColumnBuilder(Transform root) { Root = root; }

            public void Add(TMP_Text text) => Add((Component)text);

            public TMP_Text AddText(TMP_Text text)
            {
                Add(text);
                return text;
            }

            public Button AddButton(Button button)
            {
                Add(button);
                return button;
            }

            public TMP_InputField AddInput(TMP_InputField field)
            {
                Add(field);
                Prefer(field.GetComponent<RectTransform>(), 56f);
                return field;
            }

            public Image AddImage(Image image)
            {
                Add(image);
                Prefer(image.rectTransform, 28f);
                return image;
            }

            private void Add(Component component)
            {
                component.transform.SetParent(Root, false);
                component.transform.SetSiblingIndex(_index++);
                var rt = component.GetComponent<RectTransform>();
                var le = component.GetComponent<LayoutElement>();
                if (le == null) le = component.gameObject.AddComponent<LayoutElement>();
                if (le.preferredHeight < 32f) le.preferredHeight = OperateComfort.BodyFontSize + 10f;
                le.flexibleWidth = 1f;
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                }
            }
        }
    }
}
