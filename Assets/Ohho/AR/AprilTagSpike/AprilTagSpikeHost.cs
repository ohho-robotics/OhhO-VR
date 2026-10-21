using UnityEngine;

namespace OmniBot.VR.AprilTagSpike
{
    /// <summary>
    /// Isolated spike host. Do not add it to <c>Assets/scene1.unity</c>.
    /// It does not request <see cref="HeadsetCameraPermission.PermissionId"/>
    /// and it does not run unless <see cref="runOnStart"/> is enabled.
    /// </summary>
    public sealed class AprilTagSpikeHost : MonoBehaviour
    {
        [SerializeField] private bool runOnStart;

        private StubAprilTagDetector _detector;
        private UnavailablePassthroughFrameSource _frames;
        private ManualPlacementBaseline _baseline;
        private bool _wired;

        public IAprilTagDetector Detector
        {
            get
            {
                EnsureWired();
                return _detector;
            }
        }

        public IPassthroughFrameSource Frames
        {
            get
            {
                EnsureWired();
                return _frames;
            }
        }

        public ManualPlacementBaseline Baseline
        {
            get
            {
                EnsureWired();
                return _baseline;
            }
        }

        public bool RequestsHeadsetCameraPermission => false;

        public bool RunsOnStart => runOnStart;

        private void Awake()
        {
            EnsureWired();
        }

        private void Start()
        {
            if (!runOnStart)
            {
                return;
            }

            EnsureWired();
            if (!_frames.IsAvailable)
            {
                Debug.LogWarning("[AprilTagSpike] " + _frames.UnavailableReason);
            }
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            _detector = new StubAprilTagDetector();
            _frames = new UnavailablePassthroughFrameSource();
            _baseline = new ManualPlacementBaseline();
        }
    }
}
