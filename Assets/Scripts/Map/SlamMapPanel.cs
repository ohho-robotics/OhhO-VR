using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Mapping
{
    public enum CostmapLayer
    {
        Off = 0,
        Global = 1,
        Local = 2,
    }

    /// <summary>
    /// World-space SLAM map. Subscribes to the active <see cref="RobotProfile"/>
    /// topics (OmniBot defaults when a field is blank): occupancy grid at or
    /// below 1 Hz with rosbridge png compression, laser scan at or below 10 Hz,
    /// and robot pose from <c>/tf</c> with <c>/odom</c> as a fallback.
    /// A tap raises <see cref="GoalRequested"/> in the map frame and does not
    /// publish a Nav2 goal. Texture uploads run only when a new grid or scan
    /// arrives, not every frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SlamMapPanel : MonoBehaviour, IMapGoalSource
    {
        const string GridType = "nav_msgs/OccupancyGrid";
        const string ScanType = "sensor_msgs/LaserScan";
        const string OdomType = "nav_msgs/Odometry";
        const string TfType = "tf2_msgs/TFMessage";
        const float ArrowNosePixels = 8f;

        [SerializeField] private RectTransform mapRect;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RawImage scanImage;
        [SerializeField] private RectTransform arrow;
        [SerializeField] private Button costmapButton;
        [SerializeField] private Button floorButton;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private MapFloorProjection floor;

        readonly RateGate _mapGate = new RateGate(1.0);
        readonly RateGate _costGate = new RateGate(1.0);
        readonly RateGate _scanGate = new RateGate(10.0);
        readonly RateGate _odomGate = new RateGate(20.0);
        readonly RateGate _tfGate = new RateGate(20.0);
        readonly TfBuffer _tf = new TfBuffer();
        readonly LaserScanView _scan = new LaserScanView();
        readonly OccupancyGrid _published = new OccupancyGrid();
        readonly OccupancyGrid _scratch = new OccupancyGrid();
        readonly OccupancyGrid _costmap = new OccupancyGrid();
        readonly object _decodeLock = new object();

        Action<string> _onMap;
        Action<string> _onScan;
        Action<string> _onOdom;
        Action<string> _onTf;
        Action<string> _onCostmap;

        MapTopicSet _topics;
        MapViewState _view;
        Se2 _robotInMap;
        bool _hasRobot;
        bool _listening;
        bool _configured;
        bool _placed;
        bool _floorOn;
        bool _mapVisualDirty;
        bool _scanDirty;
        bool _arrowShown;
        CostmapLayer _layer = CostmapLayer.Off;
        string _costTopic;
        int _knownCells;
        int _statusRevision = -1;
        int _scanXsLen;
        int[] _scanXs = Array.Empty<int>();
        int[] _scanYs = Array.Empty<int>();
        Color32[] _publishedColors = Array.Empty<Color32>();
        Color32[] _scratchColors = Array.Empty<Color32>();
        Color32[] _uploadColors;
        Color32[] _scanColors = Array.Empty<Color32>();
        Texture2D _mapTexture;
        Texture2D _scanTexture;
        Component _costLabel;
        Component _floorLabel;
        Component _statusLabel;
        Canvas _canvas;
        TeleopController _teleop;
        float _nextTeleopSearch;
        string _pendingJson;
        string _decodeError;
        bool _workerRunning;
        bool _scratchReady;
        int _scratchKnown;

        public event Action<MapFramePose> GoalRequested;
        public CostmapLayer Layer => _layer;
        public bool FloorShown => _floorOn;
        public Texture MapTexture => _mapTexture;

        public bool TryGetView(out MapViewState view)
        {
            view = _view;
            return _view.Width > 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (Application.isBatchMode) return;
            if (FindAnyObjectByType<SlamMapPanel>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("SlamMapPanel");
            DontDestroyOnLoad(go);
            go.AddComponent<SlamMapPanel>();
        }

        void Awake()
        {
            _onMap = OnMapMessage;
            _onScan = OnScanMessage;
            _onOdom = OnOdomMessage;
            _onTf = OnTfMessage;
            _onCostmap = OnCostmapMessage;
            _topics = MapTopics.Resolve(null);
            if (mapImage == null) BuildUi();
            else BindExisting();
            SetVisual(false);
        }

        void OnDestroy()
        {
            Unhook();
            if (costmapButton != null) costmapButton.onClick.RemoveListener(CycleCostmap);
            if (floorButton != null) floorButton.onClick.RemoveListener(ToggleFloor);
            if (_mapTexture != null) Destroy(_mapTexture);
            if (_scanTexture != null) Destroy(_scanTexture);
        }

        /// <summary>Use this profile even when teleop has not started.</summary>
        public void Configure(RobotProfile profile)
        {
            _configured = true;
            UseProfile(profile);
            SetVisual(true);
        }

        public void CycleCostmap()
        {
            CostmapLayer next = _layer == CostmapLayer.Off
                ? CostmapLayer.Global
                : _layer == CostmapLayer.Global ? CostmapLayer.Local : CostmapLayer.Off;
            SetCostmapLayer(next);
        }

        public void SetCostmapLayer(CostmapLayer layer)
        {
            if (_layer == layer) return;
            DropCostmap();
            _layer = layer;
            _costmap.Width = 0;
            if (_listening) SubscribeCostmap();
            SetLabel(_costLabel, CostmapCaption(layer));
            _mapVisualDirty = _published.Width > 0;
        }

        public void SetFloorProjection(bool shown)
        {
            _floorOn = shown;
            if (floor == null) floor = GetComponent<MapFloorProjection>();
            if (floor == null) floor = gameObject.AddComponent<MapFloorProjection>();
            floor.Bind(this);
            floor.SetShown(shown);
            SetLabel(_floorLabel, shown ? "Floor: On" : "Floor: Off");
        }

        public void ToggleFloor() => SetFloorProjection(!_floorOn);

        public void OnMapClicked(PointerEventData eventData)
        {
            if (mapRect == null || _published.Width <= 0) return;
            Camera cam = eventData.pressEventCamera;
            if (cam == null && _canvas != null) cam = _canvas.worldCamera;
            if (cam == null) cam = Camera.main;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    mapRect, eventData.position, cam, out Vector2 local))
                return;
            if (!OccupancyGridCodec.TryLocalPointToPose(mapRect.rect, local, _published, out MapFramePose pose))
                return;
            GoalRequested?.Invoke(pose);
        }

        void Update()
        {
            PumpDecode();
            if (_decodeError != null)
            {
                Debug.LogWarning("[SlamMapPanel] Map decode failed: " + _decodeError);
                _decodeError = null;
            }
            TrackTeleop();
            if (visualRoot != null && !visualRoot.activeSelf) return;
            if (_mapVisualDirty) UploadMap();
            if (_scanDirty) UploadScan();
            PlaceArrow();
            RefreshStatus();
        }

        void TrackTeleop()
        {
            if (_teleop == null && Time.unscaledTime >= _nextTeleopSearch)
            {
                _teleop = FindAnyObjectByType<TeleopController>(FindObjectsInactive.Include);
                _nextTeleopSearch = Time.unscaledTime + 1f;
            }
            if (_teleop != null && _teleop.IsTeleopActive && _teleop.Profile != null)
            {
                if (!_listening || !ReferenceEquals(_teleop.Profile, _boundProfile))
                    UseProfile(_teleop.Profile);
                SetVisual(true);
                return;
            }
            if (_configured)
            {
                SetVisual(true);
                return;
            }
            if (_listening) Unhook();
            SetVisual(false);
        }

        RobotProfile _boundProfile;

        void UseProfile(RobotProfile profile)
        {
            _boundProfile = profile;
            MapTopicSet next = MapTopics.Resolve(profile);
            if (_listening && next.Equals(_topics)) return;
            if (_listening) Unhook();
            _topics = next;
            Hook();
        }

        void Hook()
        {
            _mapGate.Reset();
            _costGate.Reset();
            _scanGate.Reset();
            _odomGate.Reset();
            _tfGate.Reset();
            var ros = ROSBridgeClient.Instance;
            if (!string.IsNullOrEmpty(_topics.Map))
                ros.Subscribe(_topics.Map, GridType, _onMap, 1000, "png");
            if (!string.IsNullOrEmpty(_topics.Scan))
                ros.Subscribe(_topics.Scan, ScanType, _onScan, 100, null);
            if (!string.IsNullOrEmpty(_topics.Odom))
                ros.Subscribe(_topics.Odom, OdomType, _onOdom, 50, null);
            ros.Subscribe(MapTopics.DefaultTf, TfType, _onTf, 50, null);
            _listening = true;
            SubscribeCostmap();
        }

        void Unhook()
        {
            if (!_listening) return;
            var ros = ROSBridgeClient.Instance;
            UnsubscribeIf(ros, _topics.Map, _onMap);
            UnsubscribeIf(ros, _topics.Scan, _onScan);
            UnsubscribeIf(ros, _topics.Odom, _onOdom);
            UnsubscribeIf(ros, MapTopics.DefaultTf, _onTf);
            DropCostmap();
            _listening = false;
        }

        void SubscribeCostmap()
        {
            string topic = _layer == CostmapLayer.Global ? _topics.GlobalCostmap
                : _layer == CostmapLayer.Local ? _topics.LocalCostmap : null;
            if (string.IsNullOrEmpty(topic)) return;
            _costTopic = topic;
            ROSBridgeClient.Instance.Subscribe(topic, GridType, _onCostmap, 1000, "png");
        }

        void DropCostmap()
        {
            if (string.IsNullOrEmpty(_costTopic)) return;
            ROSBridgeClient.Instance.Unsubscribe(_costTopic, _onCostmap);
            _costTopic = null;
        }

        void OnMapMessage(string json)
        {
            if (!_mapGate.TryEnter(Now())) return;
            bool start;
            lock (_decodeLock)
            {
                _pendingJson = json;
                start = !_workerRunning;
                if (start) _workerRunning = true;
            }
            if (start) ThreadPool.QueueUserWorkItem(_ => DecodeWorker());
        }

        void DecodeWorker()
        {
            while (true)
            {
                string json;
                lock (_decodeLock)
                {
                    json = _pendingJson;
                    _pendingJson = null;
                    if (json == null)
                    {
                        _workerRunning = false;
                        return;
                    }
                }
                try
                {
                    if (!OccupancyGridCodec.TryDecode(json, _scratch)) continue;
                    int known = OccupancyGridCodec.Colorize(_scratch, ref _scratchColors);
                    lock (_decodeLock)
                    {
                        _scratchKnown = known;
                        _scratchReady = true;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _decodeError = ex.Message;
                }
            }
        }

        void PumpDecode()
        {
            bool ready;
            lock (_decodeLock) ready = _scratchReady;
            if (!ready) return;

            // Swap by copying fields the worker owns. The cell buffer reference moves.
            int[] cells = _published.Cells;
            _published.Cells = _scratch.Cells;
            _scratch.Cells = cells;
            _published.Width = _scratch.Width;
            _published.Height = _scratch.Height;
            _published.Resolution = _scratch.Resolution;
            _published.OriginX = _scratch.OriginX;
            _published.OriginY = _scratch.OriginY;
            _published.OriginYaw = _scratch.OriginYaw;
            _published.FrameId = _scratch.FrameId;
            var colors = _publishedColors;
            _publishedColors = _scratchColors;
            _scratchColors = colors;
            _knownCells = _scratchKnown;
            _view.Width = _published.Width;
            _view.Height = _published.Height;
            _view.Resolution = _published.Resolution;
            _view.OriginX = _published.OriginX;
            _view.OriginY = _published.OriginY;
            _view.OriginYaw = _published.OriginYaw;
            _view.MapRevision++;
            _mapVisualDirty = true;
            ReprojectScan();

            lock (_decodeLock)
            {
                _scratchReady = false;
                if (_pendingJson != null)
                    ThreadPool.QueueUserWorkItem(_ => DecodeWorker());
                else
                    _workerRunning = false;
            }
        }

        void OnScanMessage(string json)
        {
            if (!_scanGate.TryEnter(Now())) return;
            if (!_scan.TryParse(json)) return;
            ReprojectScan();
        }

        void ReprojectScan()
        {
            if (_published.Width <= 0 || _scan.Count <= 0)
            {
                _scanXsLen = 0;
                _scanDirty = _published.Width > 0;
                return;
            }
            Se2 sensor = _robotInMap;
            if (!string.IsNullOrEmpty(_scan.FrameId) &&
                _tf.TryPoseOf(_scan.FrameId, _published.FrameId, out Se2 framed))
                sensor = framed;
            else if (!_hasRobot)
            {
                _scanXsLen = 0;
                _scanDirty = true;
                return;
            }
            if (_scanXs.Length < _scan.Count)
            {
                _scanXs = new int[_scan.Count];
                _scanYs = new int[_scan.Count];
            }
            _scanXsLen = OccupancyGridCodec.ProjectScan(_scan, sensor, _published, _scanXs, _scanYs);
            _scanDirty = true;
        }

        void OnOdomMessage(string json)
        {
            if (!_odomGate.TryEnter(Now())) return;
            if (!PoseMessages.TryApplyOdom(json, _tf, out _)) return;
            AcceptPose();
        }

        void OnTfMessage(string json)
        {
            if (!_tfGate.TryEnter(Now())) return;
            if (!PoseMessages.TryApplyTf(json, _tf)) return;
            AcceptPose();
        }

        void OnCostmapMessage(string json)
        {
            if (!_costGate.TryEnter(Now())) return;
            if (!OccupancyGridCodec.TryDecode(json, _costmap)) return;
            _mapVisualDirty = _published.Width > 0 || _costmap.Width > 0;
        }

        void AcceptPose()
        {
            if (!_tf.TryRobotInMap(out Se2 pose))
            {
                _hasRobot = false;
                _view.HasRobot = false;
                return;
            }
            _robotInMap = pose;
            _hasRobot = true;
            _view.RobotX = pose.X;
            _view.RobotY = pose.Y;
            _view.RobotYaw = pose.Yaw;
            _view.HasRobot = true;
            _view.PoseRevision++;
        }

        void UploadMap()
        {
            _mapVisualDirty = false;
            OccupancyGrid source = _published.Width > 0 ? _published : _costmap;
            Color32[] colors = _published.Width > 0 ? _publishedColors : null;
            if (source == null || source.Width <= 0 || source.Height <= 0) return;
            int n = source.Width * source.Height;
            if (colors == null || colors.Length != n)
            {
                OccupancyGridCodec.Colorize(source, ref _uploadColors);
                colors = _uploadColors;
            }
            else if (_layer != CostmapLayer.Off && _costmap.Width > 0 && _published.Width > 0)
            {
                if (_uploadColors == null || _uploadColors.Length != n) _uploadColors = new Color32[n];
                Array.Copy(colors, _uploadColors, n);
                bool same = string.IsNullOrEmpty(_costmap.FrameId) ||
                            _costmap.FrameId == _published.FrameId ||
                            _costmap.FrameId == MapTopics.DefaultMapFrame;
                Se2 costFromMap = Se2.Identity;
                if (!same && _tf.TryPoseOf(_costmap.FrameId, _published.FrameId, out Se2 mapFromCost))
                    costFromMap = Se2.Inverse(mapFromCost);
                OccupancyGridCodec.BlendCostmap(_published, _costmap, same, costFromMap, _uploadColors);
                colors = _uploadColors;
            }
            EnsureTexture(ref _mapTexture, mapImage, source.Width, source.Height);
            _mapTexture.SetPixels32(colors);
            _mapTexture.Apply(false, false);
        }

        void UploadScan()
        {
            _scanDirty = false;
            int w = _published.Width;
            int h = _published.Height;
            if (w <= 0 || h <= 0 || scanImage == null) return;
            int n = w * h;
            if (_scanColors.Length != n) _scanColors = new Color32[n];
            else Array.Clear(_scanColors, 0, n);
            var point = OccupancyColors.ScanPoint;
            for (int i = 0; i < _scanXsLen; i++)
            {
                int x = _scanXs[i];
                int y = _scanYs[i];
                for (int dy = -1; dy <= 1; dy++)
                {
                    int py = y + dy;
                    if ((uint)py >= (uint)h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int px = x + dx;
                        if ((uint)px >= (uint)w) continue;
                        _scanColors[py * w + px] = point;
                    }
                }
            }
            EnsureTexture(ref _scanTexture, scanImage, w, h);
            _scanTexture.SetPixels32(_scanColors);
            _scanTexture.Apply(false, false);
        }

        void PlaceArrow()
        {
            if (arrow == null) return;
            bool show = _hasRobot && _published.Width > 0 && mapRect != null;
            if (_arrowShown != show)
            {
                arrow.gameObject.SetActive(show);
                _arrowShown = show;
            }
            if (!show) return;
            ArrowPlacement place = OccupancyGridCodec.PlaceArrow(
                _published, _robotInMap.X, _robotInMap.Y, _robotInMap.Yaw, ArrowNosePixels);
            Rect rect = mapRect.rect;
            float u = _published.Width > 0 ? place.PixelX / _published.Width : 0f;
            float v = _published.Height > 0 ? place.PixelY / _published.Height : 0f;
            arrow.anchoredPosition = new Vector2(u * rect.width, v * rect.height);
            arrow.localRotation = Quaternion.Euler(0f, 0f, place.PixelYaw * Mathf.Rad2Deg);
        }

        void RefreshStatus()
        {
            if (_statusLabel == null || _view.MapRevision == 0 || _statusRevision == _view.MapRevision) return;
            _statusRevision = _view.MapRevision;
            int cells = _view.Width * _view.Height;
            int pct = cells > 0 ? (_knownCells * 100) / cells : 0;
            SetLabel(_statusLabel, "Map " + _view.Width.ToString() + "x" + _view.Height.ToString() + "  " + pct.ToString() + "% known");
        }

        static void EnsureTexture(ref Texture2D tex, RawImage image, int w, int h)
        {
            if (tex != null && tex.width == w && tex.height == h) return;
            if (tex != null) Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "SlamMap"
            };
            if (image != null) image.texture = tex;
        }

        void SetVisual(bool show)
        {
            if (visualRoot == null) return;
            if (visualRoot.activeSelf == show) return;
            visualRoot.SetActive(show);
            if (show) PlaceInFrontOfCamera();
        }

        void PlaceInFrontOfCamera()
        {
            if (_placed) return;
            Camera cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null) return;
            Transform camT = cam.transform;
            transform.SetPositionAndRotation(
                camT.position + camT.forward * 1.05f + camT.right * 0.42f,
                Quaternion.LookRotation(camT.forward, Vector3.up));
            if (_canvas != null) _canvas.worldCamera = cam;
            _placed = true;
        }

        static void UnsubscribeIf(ROSBridgeClient ros, string topic, Action<string> callback)
        {
            if (!string.IsNullOrEmpty(topic)) ros.Unsubscribe(topic, callback);
        }

        static double Now() => Time.realtimeSinceStartupAsDouble;

        static string CostmapCaption(CostmapLayer layer)
        {
            if (layer == CostmapLayer.Global) return "Costmap: Global";
            if (layer == CostmapLayer.Local) return "Costmap: Local";
            return "Costmap: Off";
        }

        void BindExisting()
        {
            if (mapRect == null && mapImage != null) mapRect = mapImage.rectTransform;
            _canvas = GetComponent<Canvas>();
            if (mapImage != null)
            {
                var click = mapImage.GetComponent<MapPointerClick>();
                if (click == null) click = mapImage.gameObject.AddComponent<MapPointerClick>();
                click.Panel = this;
            }
            if (costmapButton != null) costmapButton.onClick.AddListener(CycleCostmap);
            if (floorButton != null) floorButton.onClick.AddListener(ToggleFloor);
            if (visualRoot == null) visualRoot = gameObject;
        }

        void BuildUi()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();
            var root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            root.sizeDelta = new Vector2(720f, 560f);
            root.localScale = Vector3.one * 0.0008f;

            var visual = NewRect("Visual", root);
            visual.anchorMin = Vector2.zero;
            visual.anchorMax = Vector2.one;
            visual.offsetMin = Vector2.zero;
            visual.offsetMax = Vector2.zero;
            visualRoot = visual.gameObject;
            var backdrop = visual.gameObject.AddComponent<Image>();
            backdrop.color = new Color(OhhoTheme.Surface.r, OhhoTheme.Surface.g, OhhoTheme.Surface.b, 0.92f);

            var map = NewRect("Map", visual);
            map.anchorMin = new Vector2(0f, 0f);
            map.anchorMax = new Vector2(1f, 1f);
            map.offsetMin = new Vector2(16f, 16f);
            map.offsetMax = new Vector2(-16f, -64f);
            mapImage = map.gameObject.AddComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.raycastTarget = true;
            mapRect = map;
            var click = map.gameObject.AddComponent<MapPointerClick>();
            click.Panel = this;
            map.gameObject.AddComponent<RectMask2D>();

            var scan = NewRect("Scan", map);
            Stretch(scan);
            scanImage = scan.gameObject.AddComponent<RawImage>();
            scanImage.raycastTarget = false;
            scanImage.color = Color.white;

            var arrowGo = NewRect("Arrow", map);
            arrowGo.anchorMin = Vector2.zero;
            arrowGo.anchorMax = Vector2.zero;
            arrowGo.pivot = new Vector2(0.5f, 0.5f);
            arrowGo.sizeDelta = new Vector2(28f, 28f);
            var arrowImage = arrowGo.gameObject.AddComponent<Image>();
            arrowImage.sprite = ArrowSprite();
            arrowImage.color = new Color(1f, 0.25f, 0.3f, 1f);
            arrowImage.raycastTarget = false;
            arrow = arrowGo;
            arrow.gameObject.SetActive(false);

            _statusLabel = MakeButton(visual, "Status", new Vector2(16f, -12f), new Vector2(280f, 40f), "Waiting for map", false);
            costmapButton = MakeClickButton(visual, "Costmap", new Vector2(300f, -12f), new Vector2(200f, 40f),
                CostmapCaption(CostmapLayer.Off), CycleCostmap, out _costLabel);
            floorButton = MakeClickButton(visual, "Floor", new Vector2(508f, -12f), new Vector2(190f, 40f),
                "Floor: Off", ToggleFloor, out _floorLabel);
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        Component MakeButton(RectTransform parent, string name, Vector2 anchored, Vector2 size, string caption, bool raycast)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchored;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(OhhoTheme.Bg.r, OhhoTheme.Bg.g, OhhoTheme.Bg.b, 0.85f);
            image.raycastTarget = raycast;
            var textGo = NewRect("Label", rect);
            Stretch(textGo);
            return CreateLabel(textGo.gameObject, caption, 22);
        }

        Button MakeClickButton(RectTransform parent, string name, Vector2 anchored, Vector2 size, string caption,
            UnityEngine.Events.UnityAction action, out Component label)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchored;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(OhhoTheme.Cyan.r, OhhoTheme.Cyan.g, OhhoTheme.Cyan.b, 0.25f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var textGo = NewRect("Label", rect);
            Stretch(textGo);
            label = CreateLabel(textGo.gameObject, caption, 22);
            return button;
        }

        static Component CreateLabel(GameObject go, string text, int size)
        {
            TMP_FontAsset font = null;
            try { font = TMP_Settings.defaultFontAsset; }
            catch (Exception) { font = null; }
            if (font != null)
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.text = text;
                tmp.fontSize = size;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = OhhoTheme.Text;
                tmp.raycastTarget = false;
                return tmp;
            }
            var ui = go.AddComponent<Text>();
            ui.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (ui.font == null) ui.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ui.text = text;
            ui.fontSize = size;
            ui.alignment = TextAnchor.MiddleCenter;
            ui.color = OhhoTheme.Text;
            ui.raycastTarget = false;
            return ui;
        }

        static void SetLabel(Component label, string text)
        {
            if (label is TMP_Text tmp) tmp.text = text;
            else if (label is Text ui) ui.text = text;
        }

        static Sprite ArrowSprite()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - 6f;
                    float half = (x >= 6 && x <= 28) ? (28 - x) * 0.55f : -1f;
                    bool inside = half >= 0f && Math.Abs(y - 16f) <= half;
                    pixels[y * s + x] = inside
                        ? new Color32(255, 64, 72, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
