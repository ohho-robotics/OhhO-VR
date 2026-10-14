using UnityEngine;

namespace OmniBot.VR.Control.Manip
{
    /// <summary>
    /// Wire sphere at the arm workspace origin. Radius comes from the profile
    /// (itself from <see cref="Core.RobotDescriptor"/>). The sphere is a
    /// preview of the hand volume — it does not change joint commands.
    /// </summary>
    public sealed class WorkspaceSphereVisual : MonoBehaviour
    {
        private const int Segments = 48;

        private LineRenderer[] _rings;
        private float _radius = 0.40f;

        public float Radius => _radius;

        public void Configure(float radius)
        {
            _radius = Mathf.Max(0.01f, radius);
            EnsureRings();
            ApplyGeometry();
            SetAppearance(ArmEnablePhase.Off, false, false);
        }

        public void SetAppearance(ArmEnablePhase phase, bool clutch, bool trackingGood)
        {
            if (_rings == null) return;
            Color color;
            if (phase == ArmEnablePhase.AwaitingRealConfirmation)
                color = new Color(1f, 0.85f, 0.2f, 0.9f);
            else if (phase == ArmEnablePhase.Armed && clutch && !trackingGood)
                color = new Color(1f, 0.55f, 0.15f, 0.95f);
            else if (phase == ArmEnablePhase.Armed && clutch)
                color = new Color(0.25f, 0.95f, 0.55f, 0.95f);
            else if (phase == ArmEnablePhase.Armed)
                color = new Color(0.2f, 0.85f, 1f, 0.85f);
            else
                color = new Color(1f, 1f, 1f, 0.35f);

            for (int i = 0; i < _rings.Length; i++)
            {
                _rings[i].startColor = color;
                _rings[i].endColor = color;
            }
        }

        private void EnsureRings()
        {
            if (_rings != null && _rings.Length == 3 && _rings[0] != null) return;
            _rings = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("WorkspaceRing" + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.loop = false;
                line.useWorldSpace = false;
                line.widthMultiplier = 0.004f;
                line.positionCount = Segments + 1;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    line.material = mat;
                }
                _rings[i] = line;
            }
        }

        private void ApplyGeometry()
        {
            Write(0, Vector3.right, Vector3.up);
            Write(1, Vector3.right, Vector3.forward);
            Write(2, Vector3.up, Vector3.forward);
        }

        private void Write(int index, Vector3 u, Vector3 v)
        {
            var pts = WorkspaceSphereRings.Ring(_radius, u, v, Segments);
            _rings[index].positionCount = pts.Length;
            _rings[index].SetPositions(pts);
        }
    }
}
