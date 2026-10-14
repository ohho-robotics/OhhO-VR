using UnityEngine;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Floor-projected BEV under the tabletop twin. The quad is not a child of
    /// the UI canvas (that canvas is scaled by 0.001). It parents to
    /// <c>OmniBotTwin</c> or a <c>TwinView</c> when one is in the scene, just
    /// below the twin origin. Otherwise it lies at desk height in front of the
    /// headset, already at tabletop scale.
    /// </summary>
    public sealed class BevFloorPanel : MonoBehaviour
    {
        private SimScreenPlan _plan;
        private BevFloorSpec _spec;
        private bool _wantStream;
        private bool _seekTwin;
        private Transform _explicitTwin;
        private Vector3 _cameraPosition;
        private Vector3 _cameraForward;
        private Transform _anchor;
        private Mesh _mesh;
        private Material _material;
        private MjpegVideoSource _source;
        private string _url;
        private float _seek;
        private Texture2D _dark;

        public BevFloorSpec Spec => _spec;
        public Transform Anchor => _anchor;

        public void Apply(SimScreenPlan plan, bool stream)
        {
            _seekTwin = true;
            Vector3 position = Vector3.zero;
            Vector3 forward = Vector3.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                position = cam.transform.position;
                forward = cam.transform.forward;
            }
            PlaceAndStream(plan, stream, position, forward, BevFloorLayout.FindTwin());
        }

        /// <summary>Places the quad on <paramref name="twin"/> and does not search again.</summary>
        public void Apply(SimScreenPlan plan, bool stream, Vector3 cameraPosition, Vector3 cameraForward, Transform twin)
        {
            _seekTwin = false;
            PlaceAndStream(plan, stream, cameraPosition, cameraForward, twin);
        }

        private void PlaceAndStream(SimScreenPlan plan, bool stream, Vector3 cameraPosition, Vector3 cameraForward, Transform twin)
        {
            _plan = plan;
            _wantStream = stream;
            _explicitTwin = twin;
            _cameraPosition = cameraPosition;
            _cameraForward = cameraForward;
            _spec = BevFloorLayout.Build(plan, twin, cameraPosition, cameraForward);
            if (!_spec.Show)
            {
                StopStream();
                if (_anchor != null) _anchor.gameObject.SetActive(false);
                return;
            }
            EnsureAnchor();
            Place();
            _anchor.gameObject.SetActive(isActiveAndEnabled);
            if (Application.isPlaying && isActiveAndEnabled && _wantStream)
                StartStream(_spec.StreamUrl);
            else
                StopStream();
        }

        private void OnEnable()
        {
            if (_anchor != null && _spec.Show) _anchor.gameObject.SetActive(true);
            if (Application.isPlaying && _wantStream) StartStream(_spec.StreamUrl);
        }

        private void OnDisable()
        {
            StopStream();
            if (_anchor != null) _anchor.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_seekTwin || !Application.isPlaying || _plan == null || !_spec.Show) return;
            _seek += Time.unscaledDeltaTime;
            if (_seek < 1f) return;
            _seek = 0f;
            Transform twin = BevFloorLayout.FindTwin();
            Transform parent = _anchor != null ? _anchor.parent : null;
            if (twin == parent) return;
            var cam = Camera.main;
            if (cam != null)
            {
                _cameraPosition = cam.transform.position;
                _cameraForward = cam.transform.forward;
            }
            PlaceAndStream(_plan, _wantStream, _cameraPosition, _cameraForward, twin);
        }

        private void OnDestroy()
        {
            StopStream();
            if (_anchor != null) DestroyObject(_anchor.gameObject);
            DestroyObject(_mesh);
            DestroyObject(_material);
            DestroyObject(_dark);
            _anchor = null;
            _mesh = null;
            _material = null;
            _dark = null;
        }

        private void Place()
        {
            if (_spec.ParentedToTwin && _explicitTwin != null)
            {
                _anchor.SetParent(_explicitTwin, false);
                _anchor.localPosition = new Vector3(0f, _spec.AnchorLocalY, 0f);
                _anchor.localRotation = Quaternion.identity;
                _anchor.localScale = _spec.AnchorScale;
                return;
            }
            _anchor.SetParent(null, false);
            _anchor.position = _spec.WorldPosition;
            _anchor.rotation = _spec.WorldRotation;
            _anchor.localScale = _spec.AnchorScale;
        }

        private void EnsureAnchor()
        {
            if (_anchor != null) return;
            var go = new GameObject("BevFloor");
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            _mesh = BevFloorLayout.BuildMesh();
            filter.sharedMesh = _mesh;
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("OhhO/SimScreenUnlit");
            if (shader != null)
            {
                _material = new Material(shader) { name = "BevFloor" };
                _material.mainTexture = Dark();
                _material.color = new Color(0.16f, 0.17f, 0.2f, 1f);
                renderer.sharedMaterial = _material;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _anchor = go.transform;
        }

        private void StartStream(string url)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (string.IsNullOrEmpty(url))
            {
                StopStream();
                return;
            }
            if (_source != null && _url == url) return;
            StopStream();
            _url = url;
            var source = new MjpegVideoSource(this);
            source.JpegQuality = BevFloorLayout.StreamQuality;
            source.JpegWidth = BevFloorLayout.StreamWidth;
            source.OnFrame += OnFrame;
            source.OnError += OnStreamError;
            source.StartUrl(url);
            _source = source;
        }

        private void StopStream()
        {
            _url = null;
            if (_source == null) return;
            _source.OnFrame -= OnFrame;
            _source.OnError -= OnStreamError;
            _source.Dispose();
            _source = null;
        }

        private void OnFrame(Texture2D frame)
        {
            if (_material == null || frame == null) return;
            _material.mainTexture = frame;
            _material.color = Color.white;
        }

        private void OnStreamError(string error)
        {
            Debug.LogWarning("[BevFloor] " + error);
        }

        private Texture2D Dark()
        {
            if (_dark != null) return _dark;
            _dark = new Texture2D(4, 4, TextureFormat.RGB24, false, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.12f, 0.14f, 0.18f);
            _dark.SetPixels(pixels);
            _dark.Apply(false, false);
            return _dark;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
