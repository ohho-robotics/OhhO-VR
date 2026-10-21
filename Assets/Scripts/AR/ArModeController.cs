using OmniBot.VR.Control;
using OmniBot.VR.Detection;
using OmniBot.VR.MR;
using UnityEngine;

namespace OmniBot.VR.AR
{
    /// <summary>
    /// Passthrough AR mode. Parents a 1:1 twin and a joint/metric HUD to the
    /// detected robot pose. When detection is not locked, a manual pose is the
    /// fallback. Display only: this does not advertise or publish teleop commands.
    /// Not added to <c>Assets/scene1.unity</c>. Call <see cref="Bind"/> from Awake
    /// on the headset, or add the component and let <c>Start</c> find detection.
    /// </summary>
    public sealed class ArModeController : MonoBehaviour
    {
        public const float OneToOneScale = 1f;

        private readonly ArModeSession _session = new ArModeSession();
        private readonly ArTelemetrySource _telemetry = new ArTelemetrySource();
        private RobotDetectionService _detection;
        private ArTwinOverlayView _twin;
        private ArRobotHudView _hud;
        private ArTwinKinematics.ArmGeometry _geometry = ArTwinKinematics.ArmGeometry.OmniBotNominal;
        private Transform _externalTwin;
        private bool _bound;

        public ArModeSession Session => _session;
        public ArTelemetrySource Telemetry => _telemetry;
        public ArTwinOverlayView Twin => _twin;
        public ArRobotHudView Hud => _hud;

        private void Awake()
        {
            EnsureViews();
        }

        private void Start()
        {
            if (!_bound)
            {
                TryAutoBind();
            }

            EnablePassthrough();
        }

        private void Update()
        {
            Tick(HeadPose());
        }

        public void Bind(RobotDetectionService detection, IRobotLink link, RobotProfile profile)
        {
            EnsureViews();
            _detection = detection;
            _geometry = ArTwinKinematics.ArmGeometry.FromProfile(profile);
            _telemetry.Attach(link, profile);
            _bound = true;
            if (_detection != null)
            {
                _session.ApplyDetection(_detection.Snapshot);
            }

            Refresh(HeadPose());
        }

        public void SetExternalTwin(Transform twin)
        {
            _externalTwin = twin;
        }

        public void Tick(Pose head)
        {
            EnsureViews();
            if (_detection != null)
            {
                _session.ApplyDetection(_detection.Snapshot);
            }

            Refresh(head);
        }

        public void PlaceManually(Pose worldPose)
        {
            _session.PlaceManually(worldPose);
            Refresh(HeadPose());
        }

        public void PlaceInFrontOf(Pose head)
        {
            _session.PlaceManually(ArManualPlacement.InFrontOf(
                head,
                ArManualPlacement.DefaultDistanceMeters,
                ArManualPlacement.DefaultDropMeters));
            Refresh(head);
        }

        public void ClearManualPlacement()
        {
            _session.ClearManualPlacement();
            Refresh(HeadPose());
        }

        private void OnDestroy()
        {
            _telemetry.Detach();
        }

        private void Refresh(Pose head)
        {
            _telemetry.SetTrack(_session);
            _twin.Apply(_session.TwinPose, _session.TwinVisible, _telemetry.Model.Joints, _geometry);
            _hud.SetText(ArHudText.Format(_telemetry.Model));
            if (_session.TwinVisible)
            {
                _hud.ApplyAnchor(_twin.transform, ArAnchoring.HudLocalOffset, head);
            }

            ApplyExternalTwin();
        }

        private void ApplyExternalTwin()
        {
            if (_externalTwin == null)
            {
                return;
            }

            _externalTwin.SetPositionAndRotation(_session.TwinPose.position, _session.TwinPose.rotation);
            if (_externalTwin.parent == null)
            {
                _externalTwin.localScale = Vector3.one * OneToOneScale;
            }

            if (_externalTwin.gameObject.activeSelf != _session.TwinVisible)
            {
                _externalTwin.gameObject.SetActive(_session.TwinVisible);
            }
        }

        private void EnsureViews()
        {
            if (_twin == null)
            {
                var existing = transform.Find("ArTwin");
                if (existing != null)
                {
                    _twin = existing.GetComponent<ArTwinOverlayView>();
                }

                if (_twin == null)
                {
                    var twinObject = new GameObject("ArTwin");
                    twinObject.transform.SetParent(transform, false);
                    _twin = twinObject.AddComponent<ArTwinOverlayView>();
                }
            }

            if (_hud == null)
            {
                var existingHud = _twin.transform.Find("ArRobotHud");
                if (existingHud != null)
                {
                    _hud = existingHud.GetComponent<ArRobotHudView>();
                }

                if (_hud == null)
                {
                    var hudObject = new GameObject("ArRobotHud");
                    hudObject.transform.SetParent(_twin.transform, false);
                    _hud = hudObject.AddComponent<ArRobotHudView>();
                }
            }
        }

        private void TryAutoBind()
        {
            var detection = FindAnyObjectByType<RobotDetectionService>();
            var teleop = FindAnyObjectByType<TeleopController>();
            if (teleop != null && teleop.Profile != null)
            {
                Bind(detection, RosBridgeLink.Instance, teleop.Profile);
                return;
            }

            Bind(detection, null, null);
        }

        private void EnablePassthrough()
        {
            var passthrough = FindAnyObjectByType<PassthroughManager>();
            if (passthrough != null)
            {
                passthrough.EnablePassthrough(true);
            }
        }

        private static Pose HeadPose()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return new Pose(Vector3.zero, Quaternion.identity);
            }

            return new Pose(camera.transform.position, camera.transform.rotation);
        }
    }
}
