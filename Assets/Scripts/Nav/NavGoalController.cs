using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using OmniBot.VR.Core;

namespace OmniBot.VR.Nav
{
    /// <summary>
    /// Teleop-page Nav2 panel: named waypoints, a clickable map, goal status / ETA /
    /// global plan, an explicit Nav2 / Teleop switch, and cancel (<c>/mission/cancel</c>
    /// plus the <c>navigate_to_pose</c> action).
    /// </summary>
    public class NavGoalController : MonoBehaviour
    {
        private readonly NavGoalSession _session = NavGoalSession.Active;
        private ROSBridgeClient _ros;
        private bool _rosHooked;
        private bool _uiBuilt;

        private TMP_Text _status;
        private TMP_Text _source;
        private TMP_Text _hint;
        private RectTransform _waypointRoot;
        private RawImage _mapImage;
        private RawImage _planImage;
        private TMP_Text _mapLabel;
        private Texture2D _mapTex;
        private Texture2D _planTex;
        private OccupancyMap _drawnMap;
        private int _drawnPlanCount = -1;

        public static void Ensure(MonoBehaviour host)
        {
            if (host == null) return;
            if (host.GetComponent<NavGoalController>() == null)
                host.gameObject.AddComponent<NavGoalController>();
        }

        private void OnEnable()
        {
            _session.Changed += Refresh;
            HookRos();
            BuildUi();
            _session.RequestWaypointList();
            Refresh();
        }

        private void OnDisable()
        {
            _session.Changed -= Refresh;
        }

        private void OnDestroy()
        {
            if (_ros != null) _ros.OnProtocolMessage -= OnProtocol;
            var conn = ConnectionManager.Instance;
            if (conn != null) conn.OnConnectionStateChanged -= OnConnection;
            if (_mapTex != null) Destroy(_mapTex);
            if (_planTex != null) Destroy(_planTex);
        }

        private void HookRos()
        {
            if (_rosHooked) return;
            _rosHooked = true;
            _session.Transport = RosbridgeNavTransport.Instance;
            _ros = ROSBridgeClient.Instance;
            _ros.OnProtocolMessage += OnProtocol;

            var transport = RosbridgeNavTransport.Instance;
            transport.Advertise(RobotConfig.TOPIC_CONTROL_MODE, "std_msgs/String");
            transport.Advertise(RobotConfig.TOPIC_MISSION_CANCEL, "std_msgs/String");
            transport.Subscribe(RobotConfig.TOPIC_GLOBAL_PLAN, "nav_msgs/Path",
                json => NavGoalSession.Active.HandlePlan(json));
            transport.Subscribe(RobotConfig.TOPIC_MAP, "nav_msgs/OccupancyGrid",
                json => NavGoalSession.Active.HandleMap(json));
            transport.Subscribe(RobotConfig.TOPIC_WAYPOINTS, "std_msgs/String",
                json => NavGoalSession.Active.HandleWaypointMessage(json));
            transport.Subscribe(RobotConfig.TOPIC_CONTROL_MODE_ACTIVE, "std_msgs/String",
                json => NavGoalSession.Active.HandleModeEcho(json));

            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.OnConnectionStateChanged += OnConnection;
        }

        private void OnProtocol(string json) => _session.HandleProtocol(json);

        private void OnConnection(bool connected)
        {
            if (connected) _session.RequestWaypointList();
            Refresh();
        }

        private void BuildUi()
        {
            if (_uiBuilt) return;
            _uiBuilt = true;

            var font = FindFont();
            var panel = NewRect("Nav2 Panel", transform);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(560f, 640f);
            panelRt.anchoredPosition = new Vector2(40f, -20f);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(OhhoTheme.Surface.r, OhhoTheme.Surface.g, OhhoTheme.Surface.b, 0.94f);

            var title = MakeText("Nav Title", panel.transform, font, "NAV2", 28, OhhoTheme.Cyan, TextAlignmentOptions.MidlineLeft);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(520f, 40f), new Vector2(20f, -16f));

            _status = MakeText("Nav Status", panel.transform, font, "", 20, OhhoTheme.Text, TextAlignmentOptions.TopLeft);
            _status.enableWordWrapping = true;
            Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(520f, 72f), new Vector2(20f, -56f));

            _source = MakeText("Nav Source", panel.transform, font, "", 16, OhhoTheme.Muted, TextAlignmentOptions.MidlineLeft);
            Place(_source.rectTransform, new Vector2(0f, 1f), new Vector2(520f, 24f), new Vector2(20f, -128f));

            var listGo = NewRect("Waypoint Buttons", panel.transform);
            _waypointRoot = listGo.GetComponent<RectTransform>();
            Place(_waypointRoot, new Vector2(0f, 1f), new Vector2(220f, 340f), new Vector2(16f, -160f));

            _mapImage = NewRect("Map Image", panel.transform).AddComponent<RawImage>();
            _mapImage.color = new Color(0.15f, 0.17f, 0.22f, 1f);
            Place(_mapImage.rectTransform, new Vector2(0f, 1f), new Vector2(280f, 280f), new Vector2(250f, -160f));
            var click = _mapImage.gameObject.AddComponent<MapClickRelay>();
            click.Owner = this;

            _planImage = NewRect("Plan Image", _mapImage.transform).AddComponent<RawImage>();
            _planImage.raycastTarget = false;
            _planImage.color = Color.white;
            Stretch(_planImage.rectTransform);

            _mapLabel = MakeText("Map Label", panel.transform, font, "Waiting for /map", 16, OhhoTheme.Muted, TextAlignmentOptions.MidlineLeft);
            Place(_mapLabel.rectTransform, new Vector2(0f, 1f), new Vector2(280f, 24f), new Vector2(250f, -444f));

            var nav = MakeButton("Nav2 Button", panel.transform, font, "Nav2", OhhoTheme.Cyan, Color.black);
            Place(nav.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(160f, 48f), new Vector2(16f, 64f));
            nav.onClick.AddListener(() => _session.RequestMode("nav2"));

            var teleop = MakeButton("Teleop Button", panel.transform, font, "Teleop", OhhoTheme.Violet, OhhoTheme.Text);
            Place(teleop.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(160f, 48f), new Vector2(188f, 64f));
            teleop.onClick.AddListener(() => _session.RequestMode("teleop"));

            var cancel = MakeButton("Cancel Button", panel.transform, font, "Cancel", new Color(0.85f, 0.25f, 0.25f), OhhoTheme.Text);
            Place(cancel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(180f, 48f), new Vector2(360f, 64f));
            cancel.onClick.AddListener(() => _session.CancelFromButton(null));

            _hint = MakeText("Nav Hint", panel.transform, font, "", 15, OhhoTheme.Muted, TextAlignmentOptions.MidlineLeft);
            Place(_hint.rectTransform, new Vector2(0f, 0f), new Vector2(528f, 36f), new Vector2(16f, 16f));
        }

        private void Refresh()
        {
            if (!_uiBuilt) return;
            if (_status != null) _status.text = _session.HudLine;
            if (_source != null) _source.text = "Waypoints: " + _session.WaypointSource;
            if (_hint != null)
            {
                _hint.text = _session.DeploymentKind == RobotDeployment.Sim
                    ? "Sim idle returns to Nav2. Any stick cancels the goal and takes teleop."
                    : "Real robots stay in teleop when idle. Nav2 starts from this button or a goal.";
            }
            if (_mapLabel != null)
                _mapLabel.text = _session.Map == null ? "Waiting for /map" : "Tap the map to send a goal";
            RebuildWaypointButtons();
            RedrawMap();
        }

        private void RebuildWaypointButtons()
        {
            if (_waypointRoot == null) return;
            for (int i = _waypointRoot.childCount - 1; i >= 0; i--)
                Destroy(_waypointRoot.GetChild(i).gameObject);

            var font = FindFont();
            IReadOnlyList<NamedWaypoint> waypoints = _session.Waypoints;
            int shown = Mathf.Min(waypoints.Count, 8);
            for (int i = 0; i < shown; i++)
            {
                var wp = waypoints[i];
                var button = MakeButton("Waypoint " + wp.Name, _waypointRoot, font, wp.Name, OhhoTheme.Cyan, Color.black);
                var rt = button.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(200f, 36f);
                rt.anchoredPosition = new Vector2(0f, -i * 42f);
                var captured = wp;
                button.onClick.AddListener(() => _session.SendGoal(captured));
            }
        }

        internal void OnMapClicked(float localX, float localY, float width, float height)
        {
            if (!NavMessages.TryMapClick(localX, localY, width, height, _session.Map, out var x, out var y))
                return;
            _session.SendMapGoal(x, y);
        }

        private void RedrawMap()
        {
            var map = _session.Map;
            if (map == null || _mapImage == null) return;
            if (!ReferenceEquals(map, _drawnMap))
            {
                _drawnMap = map;
                if (_mapTex != null) Destroy(_mapTex);
                _mapTex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var pixels = new Color[map.Width * map.Height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    int cell = map.Cells[i];
                    if (cell < 0) pixels[i] = new Color(0.45f, 0.45f, 0.48f, 1f);
                    else if (cell >= 50) pixels[i] = new Color(0.08f, 0.08f, 0.1f, 1f);
                    else pixels[i] = new Color(0.92f, 0.93f, 0.95f, 1f);
                }
                _mapTex.SetPixels(pixels);
                _mapTex.Apply(false);
                _mapImage.texture = _mapTex;
                _drawnPlanCount = -1;
            }

            var plan = _session.Plan;
            int count = plan != null ? plan.Points.Count : 0;
            if (count == _drawnPlanCount && _planTex != null) return;
            _drawnPlanCount = count;
            if (_planTex == null || _planTex.width != map.Width || _planTex.height != map.Height)
            {
                if (_planTex != null) Destroy(_planTex);
                _planTex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }
            var clear = new Color[map.Width * map.Height];
            _planTex.SetPixels(clear);
            if (plan != null)
            {
                var cyan = new Color(OhhoTheme.Cyan.r, OhhoTheme.Cyan.g, OhhoTheme.Cyan.b, 1f);
                for (int i = 1; i < plan.Points.Count; i++)
                {
                    if (!NavMessages.TryWorldToCell(plan.Points[i - 1].X, plan.Points[i - 1].Y, map, out var x0, out var y0))
                        continue;
                    if (!NavMessages.TryWorldToCell(plan.Points[i].X, plan.Points[i].Y, map, out var x1, out var y1))
                        continue;
                    StampLine(_planTex, x0, y0, x1, y1, cyan);
                }
            }
            _planTex.Apply(false);
            if (_planImage != null) _planImage.texture = _planTex;
        }

        private static void StampLine(Texture2D tex, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int guard = dx + dy + 4;
            while (guard-- > 0)
            {
                if (x0 >= 0 && y0 >= 0 && x0 < tex.width && y0 < tex.height)
                    tex.SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private static TMP_FontAsset FindFont()
        {
            if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
            var existing = FindAnyObjectByType<TextMeshProUGUI>();
            return existing != null ? existing.font : null;
        }

        private static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TMP_Text MakeText(string name, Transform parent, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = NewRect(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button MakeButton(string name, Transform parent, TMP_FontAsset font, string label, Color bg, Color text)
        {
            var go = NewRect(name, parent);
            var image = go.AddComponent<Image>();
            image.color = bg;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var caption = MakeText("Text", go.transform, font, label, 20, text, TextAlignmentOptions.Center);
            Stretch(caption.rectTransform);
            return button;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private sealed class MapClickRelay : MonoBehaviour, IPointerClickHandler
        {
            public NavGoalController Owner;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (Owner == null) return;
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rt, eventData.position, eventData.pressEventCamera, out var local))
                    return;
                var rect = rt.rect;
                Owner.OnMapClicked(local.x - rect.xMin, local.y - rect.yMin, rect.width, rect.height);
            }
        }
    }
}
