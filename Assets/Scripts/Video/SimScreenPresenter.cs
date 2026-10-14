using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Curved world-space sim screen: camera strip, wrist picture-in-picture,
    /// and the fps / age readout. Move uses DraggableScreen on the camera canvas.
    /// Resize uses the canvas SpatialWindowManipulator handle already in the scene.
    /// Pose and size are stored in <see cref="SimScreenPoseStore"/>.
    /// </summary>
    public sealed class SimScreenPresenter : MonoBehaviour
    {
        private const int CurveColumns = 32;
        private const float StripHeight = 108f;

        private CameraFeedController _controller;
        private SimScreenPlan _plan;
        private bool _sim;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private GameObject _curveGo;
        private GameObject _pipGo;
        private MeshRenderer _curveRenderer;
        private MeshRenderer _pipRenderer;
        private Mesh _curveMesh;
        private Mesh _pipMesh;
        private Material _curveMat;
        private Material _pipMat;
        private RectTransform _strip;
        private readonly List<Image> _buttons = new List<Image>();
        private readonly List<LabelRef> _buttonLabels = new List<LabelRef>();
        private LabelRef _stats;
        private string _statsText = "";
        private string _pendingStats;
        private double _statsNotBefore = -1;
        private Image _background;
        private Color _backgroundColor;
        private bool _backgroundDimmed;
        private float _radius = SimScreenPose.DefaultRadius;
        private float _builtW = -1f;
        private float _builtH = -1f;
        private Vector3 _savedPos;
        private Quaternion _savedRot;
        private string _chromeKey = "";
        private float _saveTimer;
        private bool _poseDirty;
        private Texture2D _grey;
        private static TMP_FontAsset _font;
        private static bool _fontTried;
        private static Font _legacy;
        private static Shader _shader;

        public void Bind(CameraFeedController controller)
        {
            _controller = controller;
        }

        public void Apply(SimScreenPlan plan)
        {
            _plan = plan;
            _sim = plan != null && plan.IsSim;
            _canvas = GetComponentInParent<Canvas>();
            _canvasRect = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
            SetLegacyChrome(!_sim);
            if (!_sim)
            {
                if (_root != null) _root.gameObject.SetActive(false);
                RestoreBackground();
                return;
            }
            EnsureRoot();
            EnsureDrag();
            ApplySize();
            RebuildChrome();
            bool show = isActiveAndEnabled;
            _root.gameObject.SetActive(show);
            if (show) DimBackground();
        }

        public void SetSelection(int index)
        {
            var on = new Color(0f, 0.83f, 1f, 1f);
            var off = new Color(0.10f, 0.12f, 0.16f, 0.94f);
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] == null) continue;
                bool selected = i == index;
                _buttons[i].color = selected ? on : off;
                if (i < _buttonLabels.Count && _buttonLabels[i] != null)
                    _buttonLabels[i].Color = selected ? Color.black : Color.white;
            }
        }

        public void SetMainTexture(Texture texture)
        {
            if (_curveMat == null) return;
            if (texture == null)
            {
                _curveMat.mainTexture = Grey();
                _curveMat.color = new Color(0.16f, 0.17f, 0.2f, 1f);
                return;
            }
            _curveMat.mainTexture = texture;
            _curveMat.color = Color.white;
        }

        public void SetPipTexture(Texture texture)
        {
            if (_pipMat == null || texture == null) return;
            _pipMat.mainTexture = texture;
            _pipMat.color = Color.white;
            if (_pipRenderer != null) _pipRenderer.enabled = true;
        }

        public void SetPipVisible(bool visible)
        {
            if (_pipRenderer != null) _pipRenderer.enabled = visible && _sim;
        }

        public void SetStats(string text)
        {
            if (_stats == null || text == null) return;
            _pendingStats = text;
            FlushStats(Time.unscaledTimeAsDouble);
        }

        public void Persist()
        {
            if (!_sim || _canvasRect == null) return;
            var state = Capture();
            SimScreenPoseStore.Save(state);
            _savedPos = _canvasRect.localPosition;
            _savedRot = _canvasRect.localRotation;
            _poseDirty = false;
            _saveTimer = 0f;
        }

        private void OnEnable()
        {
            if (!_sim || _root == null) return;
            _root.gameObject.SetActive(true);
            DimBackground();
        }

        private void OnDisable()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            Persist();
            RestoreBackground();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Persist();
        }

        private void LateUpdate()
        {
            FlushStats(Time.unscaledTimeAsDouble);
            if (!_sim || _canvasRect == null || _root == null) return;
            var size = _canvasRect.sizeDelta;
            if (Mathf.Abs(size.x - _builtW) > 0.5f || Mathf.Abs(size.y - _builtH) > 0.5f)
                RebuildMesh();
            if ((_canvasRect.localPosition - _savedPos).sqrMagnitude > 1e-8f ||
                Quaternion.Angle(_canvasRect.localRotation, _savedRot) > 0.05f)
                _poseDirty = true;
            if (!_poseDirty) return;
            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer < 0.5f) return;
            Persist();
        }

        private void OnDestroy()
        {
            DestroyObject(_curveMesh);
            DestroyObject(_pipMesh);
            DestroyObject(_curveMat);
            DestroyObject(_pipMat);
            DestroyObject(_grey);
            RestoreBackground();
        }

        private void EnsureRoot()
        {
            if (_root != null || _canvasRect == null) return;
            var go = new GameObject("Sim Screen", typeof(RectTransform));
            go.transform.SetParent(_canvasRect, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.localScale = Vector3.one;
            _root.localRotation = Quaternion.identity;
        }

        private void EnsureDrag()
        {
            if (_canvas == null) return;
            var drag = _canvas.GetComponent<OmniBot.VR.UI.DraggableScreen>();
            if (drag == null) drag = _canvas.gameObject.AddComponent<OmniBot.VR.UI.DraggableScreen>();
            drag.snapHeightToHead = false;
        }

        private void ApplySize()
        {
            if (_canvasRect == null) return;
            if (SimScreenPoseStore.TryLoad(out var saved))
            {
                saved = SimScreenPose.Clamp(saved);
                _radius = saved.Radius;
                _canvasRect.localPosition = new Vector3(saved.LocalX, saved.LocalY, saved.LocalZ);
                _canvasRect.localRotation = new Quaternion(saved.RotX, saved.RotY, saved.RotZ, saved.RotW);
                _canvasRect.sizeDelta = new Vector2(saved.Width, saved.Height);
            }
            else
            {
                var size = SimScreenPose.Default();
                _radius = size.Radius;
                _canvasRect.sizeDelta = new Vector2(size.Width, size.Height);
            }
            _savedPos = _canvasRect.localPosition;
            _savedRot = _canvasRect.localRotation;
        }

        private void RebuildChrome()
        {
            if (_root == null || _plan == null) return;
            _root.sizeDelta = _canvasRect != null ? _canvasRect.sizeDelta : _root.sizeDelta;
            string key = ChromeKey(_plan);
            if (key != _chromeKey || _strip == null)
            {
                _chromeKey = key;
                BuildStrip();
                BuildStats();
            }
            RebuildMesh();
        }

        private void RebuildMesh()
        {
            if (_root == null || _canvasRect == null) return;
            float width = Mathf.Max(8f, _canvasRect.sizeDelta.x);
            float height = Mathf.Max(8f, _canvasRect.sizeDelta.y);
            _root.sizeDelta = new Vector2(width, height);
            _builtW = width;
            _builtH = height;
            float scale = _canvas.transform.lossyScale.x;
            if (scale < 1e-5f) scale = 0.001f;
            float radiusPx = _radius / scale;
            var shader = LoadShader();
            if (shader == null) return;

            if (_curveMat == null)
            {
                _curveMat = new Material(shader);
                _curveMat.color = Color.white;
            }
            if (_curveGo == null)
            {
                _curveGo = new GameObject("Curve");
                _curveGo.transform.SetParent(_root, false);
                _curveGo.AddComponent<MeshFilter>();
                _curveRenderer = _curveGo.AddComponent<MeshRenderer>();
                _curveRenderer.sharedMaterial = _curveMat;
                Quiet(_curveRenderer);
            }
            _curveGo.transform.localPosition = new Vector3(0f, 0f, -4f);
            _curveGo.transform.localRotation = Quaternion.identity;
            _curveGo.transform.localScale = Vector3.one;
            DestroyObject(_curveMesh);
            _curveMesh = CurvedScreenGeometry.BuildMesh(width, height, radiusPx, CurveColumns);
            var filter = _curveGo.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = _curveMesh;
            if (_curveMat.mainTexture == null) SetMainTexture(null);

            float pipW = Mathf.Clamp(width * 0.26f, 180f, 520f);
            float pipH = pipW * 9f / 16f;
            if (_pipMat == null)
            {
                _pipMat = new Material(shader);
                _pipMat.color = Color.white;
            }
            if (_pipGo == null)
            {
                _pipGo = new GameObject("Wrist PiP");
                _pipGo.transform.SetParent(_root, false);
                _pipGo.AddComponent<MeshFilter>();
                _pipRenderer = _pipGo.AddComponent<MeshRenderer>();
                _pipRenderer.sharedMaterial = _pipMat;
                _pipRenderer.enabled = false;
                Quiet(_pipRenderer);
            }
            _pipGo.transform.localPosition = new Vector3(
                width * 0.5f - pipW * 0.5f - 36f,
                -height * 0.5f + pipH * 0.5f + StripHeight + 28f,
                -36f);
            _pipGo.transform.localRotation = Quaternion.identity;
            _pipGo.transform.localScale = Vector3.one;
            DestroyObject(_pipMesh);
            _pipMesh = BuildQuad(pipW, pipH);
            var pipFilter = _pipGo.GetComponent<MeshFilter>();
            if (pipFilter != null) pipFilter.sharedMesh = _pipMesh;
            _poseDirty = true;
        }

        private void BuildStrip()
        {
            if (_strip != null) DestroyObject(_strip.gameObject);
            _buttons.Clear();
            _buttonLabels.Clear();
            var go = new GameObject("Camera Strip", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            _strip = (RectTransform)go.transform;
            _strip.anchorMin = new Vector2(0.5f, 0f);
            _strip.anchorMax = new Vector2(0.5f, 0f);
            _strip.pivot = new Vector2(0.5f, 0f);
            _strip.anchoredPosition = new Vector2(0f, 18f);
            int n = _plan != null ? _plan.Views.Count : 0;
            float gap = 16f;
            float width = _canvasRect != null ? _canvasRect.sizeDelta.x : 1600f;
            float btnW = n > 0 ? Mathf.Min(280f, (width - 120f - gap * (n - 1)) / n) : 200f;
            if (btnW < 80f) btnW = 80f;
            _strip.sizeDelta = new Vector2(n * btnW + Mathf.Max(0, n - 1) * gap, 76f);
            var font = LoadTmp();
            var legacy = font == null ? LoadLegacy() : null;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                var btnGo = new GameObject(_plan.Views[i].Label, typeof(RectTransform));
                btnGo.transform.SetParent(_strip, false);
                var rt = (RectTransform)btnGo.transform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnW, 68f);
                rt.anchoredPosition = new Vector2(i * (btnW + gap), 0f);
                var image = btnGo.AddComponent<Image>();
                image.color = new Color(0.10f, 0.12f, 0.16f, 0.94f);
                var button = btnGo.AddComponent<Button>();
                button.targetGraphic = image;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() =>
                {
                    if (_controller != null) _controller.SetCamera(index);
                });
                var label = AddLabel(btnGo.transform, _plan.Views[i].Label, 28f, Color.white, font, legacy);
                _buttons.Add(image);
                _buttonLabels.Add(label);
            }
        }

        private void BuildStats()
        {
            if (_stats != null && _stats.Root != null) DestroyObject(_stats.Root);
            var back = new GameObject("Stats", typeof(RectTransform));
            back.transform.SetParent(_root, false);
            var rt = (RectTransform)back.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -20f);
            rt.sizeDelta = new Vector2(360f, 52f);
            var image = back.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = false;
            var font = LoadTmp();
            var legacy = font == null ? LoadLegacy() : null;
            _stats = AddLabel(back.transform, "No signal", 26f, Color.white, font, legacy);
            _stats.Root = back;
            _statsText = "No signal";
            _pendingStats = null;
            _statsNotBefore = -1;
        }

        private SimScreenPoseState Capture()
        {
            var p = _canvasRect.localPosition;
            var r = _canvasRect.localRotation;
            var s = _canvasRect.sizeDelta;
            return SimScreenPose.Clamp(new SimScreenPoseState
            {
                LocalX = p.x, LocalY = p.y, LocalZ = p.z,
                RotX = r.x, RotY = r.y, RotZ = r.z, RotW = r.w,
                Width = s.x, Height = s.y, Radius = _radius,
                HasSaved = true,
            });
        }

        private void SetLegacyChrome(bool visible)
        {
            SetNamedActive("Cycle Camera Button", visible);
            SetNamedActive("Camera Panel", visible);
            SetNamedActive("Camera Name", visible);
        }

        private void SetNamedActive(string childName, bool visible)
        {
            var child = transform.Find(childName);
            if (child != null) child.gameObject.SetActive(visible);
        }

        private void DimBackground()
        {
            if (_background == null && _canvas != null) _background = _canvas.GetComponent<Image>();
            if (_background == null || _backgroundDimmed) return;
            _backgroundColor = _background.color;
            var c = _backgroundColor;
            c.a = 0f;
            _background.color = c;
            _backgroundDimmed = true;
        }

        private void RestoreBackground()
        {
            if (!_backgroundDimmed || _background == null) return;
            _background.color = _backgroundColor;
            _backgroundDimmed = false;
        }

        private Texture2D Grey()
        {
            if (_grey != null) return _grey;
            _grey = new Texture2D(4, 4, TextureFormat.RGB24, false, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.16f, 0.17f, 0.2f);
            _grey.SetPixels(pixels);
            _grey.Apply(false, false);
            return _grey;
        }

        private static Mesh BuildQuad(float width, float height)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            var mesh = new Mesh();
            mesh.name = "SimScreenPiP";
            mesh.vertices = new[]
            {
                new Vector3(-hw, -hh, 0f),
                new Vector3(hw, -hh, 0f),
                new Vector3(hw, hh, 0f),
                new Vector3(-hw, hh, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 3, 1, 1, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static LabelRef AddLabel(Transform parent, string text, float size, Color color, TMP_FontAsset font, Font legacy)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 4f);
            rt.offsetMax = new Vector2(-8f, -4f);
            var label = new LabelRef();
            if (font != null)
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.fontSize = size;
                tmp.color = color;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                tmp.richText = false;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                label.Tmp = tmp;
                label.Value = text;
            }
            else
            {
                var ui = go.AddComponent<Text>();
                ui.font = legacy != null ? legacy : LoadLegacy();
                ui.fontSize = Mathf.RoundToInt(size);
                ui.color = color;
                ui.alignment = TextAnchor.MiddleCenter;
                ui.raycastTarget = false;
                ui.horizontalOverflow = HorizontalWrapMode.Overflow;
                ui.verticalOverflow = VerticalWrapMode.Overflow;
                label.Legacy = ui;
                label.Value = text;
            }
            return label;
        }

        private void FlushStats(double now)
        {
            if (_stats == null || _pendingStats == null) return;
            if (!OverlayTextGate.ShouldPublish(_statsText, _pendingStats, now, _statsNotBefore))
            {
                if (_pendingStats == _statsText) _pendingStats = null;
                return;
            }
            _statsText = _pendingStats;
            _stats.Value = _pendingStats;
            _pendingStats = null;
            _statsNotBefore = now + OverlayTextGate.IntervalSeconds;
        }

        private static string ChromeKey(SimScreenPlan plan)
        {
            if (plan == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < plan.Views.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(plan.Views[i].Topic);
            }
            return sb.ToString();
        }

        private static TMP_FontAsset LoadTmp()
        {
            if (_fontTried) return _font;
            _fontTried = true;
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return _font;
        }

        private static Font LoadLegacy()
        {
            if (_legacy != null) return _legacy;
            _legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacy == null) _legacy = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacy;
        }

        private static Shader LoadShader()
        {
            if (_shader != null) return _shader;
            _shader = Resources.Load<Shader>("SimScreenUnlit");
            if (_shader == null) _shader = Shader.Find("OhhO/SimScreenUnlit");
            if (_shader == null) _shader = Shader.Find("Unlit/Texture");
            return _shader;
        }

        private static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private sealed class LabelRef
        {
            public TMP_Text Tmp;
            public Text Legacy;
            public GameObject Root;
            private string _cached;
            public string Value
            {
                get { return _cached ?? ""; }
                set
                {
                    if (value == _cached) return;
                    _cached = value;
                    if (Tmp != null) Tmp.text = value;
                    else if (Legacy != null) Legacy.text = value;
                }
            }
            public Color Color
            {
                set
                {
                    if (Tmp != null) Tmp.color = value;
                    if (Legacy != null) Legacy.color = value;
                }
            }
        }
    }
}
