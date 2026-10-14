using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using OmniBot.VR.Control.Session;
using OmniBot.VR.Core.Platform;

namespace OmniBot.VR.UI.Session
{
    /// <summary>Which page of the picker is up.</summary>
    public enum PickerStep
    {
        Mode,
        Simulate,
        Cloud,
        RealSafety,
        RealConnect,
    }

    /// <summary>
    /// Sits between the garage and teleop: Simulate or Drive real, then a target.
    /// Simulate → Local desktop is one click on this page (the IP is already filled),
    /// so garage → robot → Simulate → Local is three clicks to a driving session.
    /// Drive real always shows the safety notice, and the deadman tutorial the first time.
    /// </summary>
    public class SessionPickerPanel : MonoBehaviour
    {
        public event System.Action<SessionLaunch> Confirmed;
        public event System.Action Cancelled;

        private string _robotId;
        private string _aliasId;
        private string _robotName;
        private string _ip = EndpointAddresses.DefaultIp;
        private string _port = EndpointAddresses.RosbridgePort.ToString(CultureInfo.InvariantCulture);
        private string _cloudUrl = "wss://";
        private PickerStep _step = PickerStep.Mode;
        private bool _safetyAcknowledged;
        private bool _chrome;
        private bool _refreshStarted;
        private string _registryNote;
        private TMP_FontAsset _font;

        private TMP_Text _title;
        private TMP_Text _status;
        private RectTransform _content;
        private Button _back;

        private static readonly Color Surface = new Color(0.086f, 0.118f, 0.20f, 1f);
        private static readonly Color TextMain = new Color(0.95f, 0.97f, 1f, 1f);
        private static readonly Color TextMut = new Color(0.70f, 0.76f, 0.84f, 1f);

        public void Open(string robotId, string aliasId, string robotName, string ip, int port, PickerStep step)
        {
            _robotId = robotId;
            _aliasId = aliasId;
            _robotName = string.IsNullOrEmpty(robotName) ? "Robot" : robotName;
            if (!string.IsNullOrEmpty(ip)) _ip = ip;
            if (port > 0) _port = port.ToString(CultureInfo.InvariantCulture);
            _safetyAcknowledged = false;
            _refreshStarted = false;
            _registryNote = null;
            gameObject.SetActive(true);
            EnsureChrome();
            ShowStep(step);
            RefreshRemote();
        }

        private void EnsureChrome()
        {
            if (_chrome) return;
            _chrome = true;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SessionUi.WhiteSprite;
            bg.color = new Color(0.039f, 0.055f, 0.102f, 0.98f);

            _title = MakeText("Title", transform, 36, TextMain, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -24f), new Vector2(-48f, -88f));

            _status = MakeText("Status", transform, 22, TextMut, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -96f), new Vector2(-48f, -140f));

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.offsetMin = new Vector2(48f, 120f);
            _content.offsetMax = new Vector2(-48f, -156f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _back = MakeButton("Back", transform, "< Garage", Surface, TextMain);
            var backRt = (RectTransform)_back.transform;
            backRt.anchorMin = new Vector2(0f, 0f);
            backRt.anchorMax = new Vector2(0f, 0f);
            backRt.pivot = new Vector2(0f, 0f);
            backRt.sizeDelta = new Vector2(240f, 56f);
            backRt.anchoredPosition = new Vector2(48f, 36f);
            _back.onClick.AddListener(OnBack);
        }

        private void ShowStep(PickerStep step)
        {
            _step = step;
            EnsureChrome();
            ClearContent();
            switch (step)
            {
                case PickerStep.Mode:
                    SessionModeBanner.Hide(gameObject);
                    _title.text = _robotName;
                    SetStatus("Simulate, or drive the real robot.");
                    AddButton("Simulate", SessionBannerStyle.SimBlue, Color.white, () => ShowStep(PickerStep.Simulate));
                    AddButton("Drive real", SessionBannerStyle.RealOrange, Color.white, () => ShowStep(PickerStep.RealSafety));
                    AddBody("Local desktop uses the IP below and starts in one click. Cloud can start or attach. In-headset is an offline demo.");
                    break;
                case PickerStep.Simulate:
                    SessionModeBanner.Apply(gameObject, SessionKind.Sim);
                    _title.text = "Simulate  ·  " + _robotName;
                    SetStatus(string.IsNullOrEmpty(_registryNote) ? "Pick where the simulator is running." : _registryNote);
                    AddIpRow();
                    AddButton("Local desktop", SessionBannerStyle.SimBlue, Color.white, () => ConfirmLocal(null));
                    AddSessions(SessionCatalog.Shared.ListLocalSim(_robotId, _aliasId), ConfirmLocal);
                    AddButton("Cloud", Surface, TextMain, () => ShowStep(PickerStep.Cloud));
                    AddButton("In-headset (offline)", Surface, TextMain, ConfirmInHeadset);
                    break;
                case PickerStep.Cloud:
                    SessionModeBanner.Apply(gameObject, SessionKind.Sim);
                    _title.text = "Cloud  ·  " + _robotName;
                    SetStatus("Start a cloud session, or attach to one that is already up.");
                    AddButton("Start cloud session", SessionBannerStyle.SimBlue, Color.white, ConfirmCloudStart);
                    AddSessions(SessionCatalog.Shared.ListCloud(_robotId, _aliasId), ConfirmCloudSession);
                    AddField("Cloud URL", _cloudUrl, value => _cloudUrl = value);
                    AddButton("Attach", Surface, TextMain, ConfirmCloudAttach);
                    break;
                case PickerStep.RealSafety:
                    SessionModeBanner.Apply(gameObject, SessionKind.Real);
                    _title.text = "Drive real  ·  " + _robotName;
                    SetStatus("Read this before the robot can move.");
                    AddBody(RealRobotSafety.Notice);
                    if (!RealRobotSafety.TutorialSeen)
                        AddBody(RealRobotSafety.Tutorial);
                    AddButton("I understand — continue", SessionBannerStyle.RealOrange, Color.white, AcknowledgeSafety);
                    break;
                case PickerStep.RealConnect:
                    SessionModeBanner.Apply(gameObject, SessionKind.Real);
                    _title.text = "Drive real  ·  " + _robotName;
                    SetStatus(string.IsNullOrEmpty(_registryNote)
                        ? "Choose an announced robot, or type its IP."
                        : _registryNote);
                    AddIpRow();
                    AddButton("Connect", SessionBannerStyle.RealOrange, Color.white, () => ConfirmReal(null));
                    AddSessions(SessionCatalog.Shared.ListReal(_robotId, _aliasId), ConfirmReal);
                    break;
            }
        }

        private void AcknowledgeSafety()
        {
            _safetyAcknowledged = true;
            if (!RealRobotSafety.TutorialSeen)
                RealRobotSafety.MarkTutorialSeen();
            ShowStep(PickerStep.RealConnect);
        }

        private void ConfirmLocal(string sessionId)
        {
            Publish(SessionPlanner.LocalDesktop(_robotId, _aliasId, _ip, PortOrDefault(), SessionCatalog.Shared, sessionId));
        }

        private void ConfirmInHeadset()
        {
            Publish(SessionPlanner.InHeadset(_robotId));
        }

        private void ConfirmCloudStart()
        {
            Publish(SessionPlanner.CloudStart(_robotId, SessionCatalog.Shared));
        }

        private void ConfirmCloudAttach()
        {
            Publish(SessionPlanner.CloudAttach(_robotId, _cloudUrl));
        }

        private void ConfirmCloudSession(string sessionId)
        {
            var found = SessionCatalog.Shared.Find(_robotId, _aliasId, sessionId, SessionKind.Sim);
            Publish(SessionPlanner.LaunchListedSim(found));
        }

        private void ConfirmReal(string sessionId)
        {
            Publish(SessionPlanner.DriveReal(_robotId, _aliasId, _ip, PortOrDefault(), SessionCatalog.Shared,
                sessionId, _safetyAcknowledged, RealRobotSafety.TutorialSeen));
        }

        private void Publish(SessionLaunch plan)
        {
            if (plan == null || !plan.ReadyToDrive || plan.Endpoint == null)
            {
                SetStatus(plan != null && !string.IsNullOrEmpty(plan.BlockReason)
                    ? plan.BlockReason
                    : "Can't start that session.");
                return;
            }
            if (plan.Endpoint.Kind == SessionKind.Real && !plan.DeadmanRequired)
            {
                SetStatus("Real robots keep the deadman on.");
                return;
            }
            Confirmed?.Invoke(plan);
        }

        private void OnBack()
        {
            switch (_step)
            {
                case PickerStep.Mode:
                    Cancelled?.Invoke();
                    break;
                case PickerStep.Simulate:
                case PickerStep.RealSafety:
                    _safetyAcknowledged = false;
                    ShowStep(PickerStep.Mode);
                    break;
                case PickerStep.Cloud:
                    ShowStep(PickerStep.Simulate);
                    break;
                case PickerStep.RealConnect:
                    _safetyAcknowledged = false;
                    ShowStep(PickerStep.RealSafety);
                    break;
            }
        }

        private void RefreshRemote()
        {
            if (_refreshStarted) return;
            _refreshStarted = true;
            var garage = GarageClient.Instance;
            if (garage == null || !garage.IsConfigured) return;
            garage.GetRobotSessions((body, err) =>
            {
                if (this == null) return;
                if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
                {
                    _registryNote = "Session registry isn't available yet. Use a manual IP.";
                }
                else
                {
                    var rows = RobotSessionRows.Parse(body, out var parseError);
                    if (!string.IsNullOrEmpty(parseError))
                        _registryNote = "Session registry isn't available yet. Use a manual IP.";
                    else
                    {
                        SessionCatalog.Shared.ReplaceRemote(rows);
                        _registryNote = null;
                    }
                }
                if (isActiveAndEnabled) ShowStep(_step);
            });
        }

        private int PortOrDefault()
        {
            if (string.IsNullOrWhiteSpace(_port)) return EndpointAddresses.RosbridgePort;
            if (!int.TryParse(_port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                return -1;
            return port;
        }

        private void AddSessions(IReadOnlyList<RobotEndpoint> sessions, System.Action<string> onPick)
        {
            if (sessions == null) return;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session == null || string.IsNullOrEmpty(session.SessionId)) continue;
                string label = string.IsNullOrEmpty(session.Label) ? session.RosbridgeUrl : session.Label;
                string id = session.SessionId;
                AddButton(label, Surface, TextMain, () => onPick(id));
            }
        }

        private void AddIpRow()
        {
            var row = new GameObject("IP Row", typeof(RectTransform));
            row.transform.SetParent(_content, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 56f;
            rowLe.preferredHeight = 56f;

            var ip = AddFieldTo(row.transform, "IP", _ip, value => _ip = value);
            var ipLe = ip.gameObject.AddComponent<LayoutElement>();
            ipLe.flexibleWidth = 3f;
            ipLe.minHeight = 56f;
            var port = AddFieldTo(row.transform, "Port", _port, value => _port = value);
            var portLe = port.gameObject.AddComponent<LayoutElement>();
            portLe.flexibleWidth = 1f;
            portLe.minHeight = 56f;
        }

        private void AddField(string placeholder, string value, System.Action<string> changed)
        {
            var field = AddFieldTo(_content, placeholder, value, changed);
            var le = field.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 56f;
        }

        private TMP_InputField AddFieldTo(Transform parent, string placeholder, string value, System.Action<string> changed)
        {
            var go = new GameObject(placeholder, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = SessionUi.WhiteSprite;
            image.color = Surface;
            var field = go.AddComponent<TMP_InputField>();

            var viewport = new GameObject("Text Area", typeof(RectTransform));
            viewport.transform.SetParent(go.transform, false);
            var viewportRt = (RectTransform)viewport.transform;
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(12f, 6f);
            viewportRt.offsetMax = new Vector2(-12f, -6f);
            viewport.AddComponent<RectMask2D>();

            var text = MakeText("Text", viewport.transform, 24, TextMain, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);
            var hint = MakeText("Placeholder", viewport.transform, 24, TextMut, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            hint.text = placeholder;
            Stretch(hint.rectTransform);

            field.textViewport = viewportRt;
            field.textComponent = text;
            field.placeholder = hint;
            field.text = value ?? "";
            field.onValueChanged.AddListener(next => changed?.Invoke(next));
            field.onSelect.AddListener(_ => TouchScreenKeyboard.Open(field.text, TouchScreenKeyboardType.Default));
            return field;
        }

        private void AddButton(string label, Color background, Color foreground, UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButton(label, _content, label, background, foreground);
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 72f;
            le.preferredHeight = 72f;
            button.onClick.AddListener(onClick);
        }

        private void AddBody(string message)
        {
            var text = MakeText("Body", _content, 22, TextMain, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            text.text = message;
            text.enableWordWrapping = true;
            var le = text.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 88f;
            le.preferredHeight = 120f;
        }

        private void ClearContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message ?? "";
        }

        private Button MakeButton(string name, Transform parent, string label, Color background, Color foreground)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = SessionUi.WhiteSprite;
            image.color = background;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var text = MakeText("Text", go.transform, 26, foreground, FontStyles.Bold, TextAlignmentOptions.Center);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        private TMP_Text MakeText(string name, Transform parent, float size, Color color, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = align;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            if (Font != null) text.font = Font;
            return text;
        }

        private TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                var sample = transform.parent != null
                    ? transform.parent.GetComponentInChildren<TMP_Text>(true)
                    : null;
                _font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
