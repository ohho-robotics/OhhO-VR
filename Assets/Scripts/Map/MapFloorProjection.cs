using UnityEngine;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// Optional floor quad of the SLAM map, on the Unity XZ plane
    /// (Unity x = map x, Unity z = map y) so it sits under a tabletop twin.
    /// Off until <see cref="SetShown"/> is called. Does not follow the robot:
    /// the grid stays in the map frame and a small marker shows the robot pose.
    /// </summary>
    public sealed class MapFloorProjection : MonoBehaviour
    {
        [SerializeField] private SlamMapPanel panel;
        [SerializeField] private float floorY;
        [SerializeField] private Transform tabletopAnchor;

        MapViewState _applied;
        Texture _appliedTexture;
        Transform _quad;
        Transform _marker;
        Mesh _floorMesh;
        Material _material;
        bool _shown;

        public void Bind(SlamMapPanel source) => panel = source;

        public void SetShown(bool shown)
        {
            _shown = shown;
            if (_quad != null) _quad.gameObject.SetActive(shown);
            if (_marker != null) _marker.gameObject.SetActive(shown && _applied.HasRobot);
            if (shown) Apply(force: true);
        }

        void LateUpdate()
        {
            if (!_shown || panel == null) return;
            Apply(force: false);
        }

        void Apply(bool force)
        {
            if (panel == null || !panel.TryGetView(out MapViewState view)) return;
            Texture texture = panel.MapTexture;
            if (texture == null || view.Width <= 0) return;
            EnsureObjects();
            bool gridChanged = force || _applied.MapRevision != view.MapRevision || _appliedTexture != texture;
            bool poseChanged = _applied.PoseRevision != view.PoseRevision;
            if (gridChanged)
            {
                float widthM = view.Width * view.Resolution;
                float heightM = view.Height * view.Resolution;
                // Mesh occupies local x in [0,1] and z in [0,1], so the pivot is the map origin.
                _quad.localScale = new Vector3(Mathf.Max(widthM, 0.01f), 1f, Mathf.Max(heightM, 0.01f));
                _quad.localPosition = new Vector3(view.OriginX, floorY, view.OriginY);
                // Unity yaw is clockwise from above; ROS yaw is counter-clockwise.
                _quad.localRotation = Quaternion.Euler(0f, -view.OriginYaw * Mathf.Rad2Deg, 0f);
                if (_material != null) _material.mainTexture = texture;
                _appliedTexture = texture;
            }
            if (view.HasRobot && (gridChanged || poseChanged))
            {
                _marker.gameObject.SetActive(_shown);
                _marker.localPosition = new Vector3(view.RobotX, floorY + 0.02f, view.RobotY);
                _marker.localRotation = Quaternion.Euler(0f, -view.RobotYaw * Mathf.Rad2Deg, 0f);
            }
            _applied = view;
        }

        void EnsureObjects()
        {
            if (_quad != null) return;
            Transform parent = tabletopAnchor != null ? tabletopAnchor : transform;
            var quadGo = new GameObject("SlamMapFloor");
            var filter = quadGo.AddComponent<MeshFilter>();
            var renderer = quadGo.AddComponent<MeshRenderer>();
            filter.sharedMesh = _floorMesh = FloorMesh();
            _quad = quadGo.transform;
            _quad.SetParent(parent, false);
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _material = new Material(shader) { name = "SlamMapFloor" };
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "SlamMapRobot";
            if (marker.TryGetComponent<Collider>(out var markerCol)) Destroy(markerCol);
            _marker = marker.transform;
            _marker.SetParent(parent, false);
            _marker.localScale = new Vector3(0.28f, 0.04f, 0.18f);
            _quad.gameObject.SetActive(_shown);
            _marker.gameObject.SetActive(false);
        }

        static Mesh FloorMesh()
        {
            var mesh = new Mesh { name = "SlamMapFloor" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            return mesh;
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_floorMesh != null) Destroy(_floorMesh);
        }
    }
}
