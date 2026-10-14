using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control.Manip
{
    /// <summary>
    /// Single-DOF gripper. Same enable / REAL confirm / clutch gate as the
    /// 6-DOF arm so a one-DOF tool cannot close on enable. Pinch is applied
    /// only while the clutch is held and is rate-limited like every other joint.
    /// </summary>
    public sealed class GripperOnlyScheme : IManipulationScheme
    {
        private const float PublishHz = 20f;

        private RobotProfile _profile;
        private readonly ArmSessionGate _gate = new ArmSessionGate();
        private float _publishInterval;
        private float _publishTimer;
        private float _gripper;
        private float _gripperOpen;
        private float _gripperClosed;
        private float _maxDelta = RobotDescriptor.MaxJointDeltaRad;
        private string[] _jointNames = { "gripper" };
        private ArmOperatorInput _input;
        private ArmOperatorInput _frame;
        private bool _suppressMotionThisPublish;
        private string _status = "Arm off";

        public bool IsConfigured => _profile != null;
        public bool ArmEnabled => _gate.Flow.TorqueEnabled;
        public ArmEnablePhase EnablePhase => _gate.Flow.Phase;
        public bool ClutchEngaged => _gate.Flow.ClutchEngaged;
        public string StatusText => _status;
        public string HudHint => "Right trigger: clutch · Pinch: gripper · Left X / thumbs-up: enable · REAL: Left X again";

        public void Configure(RobotProfile profile, Transform workspaceOrigin)
        {
            _profile = profile;
            _gate.Bind(profile);
            _publishInterval = 1f / PublishHz;
            _publishTimer = 0f;
            _suppressMotionThisPublish = false;
            _maxDelta = RobotDescriptor.ClampJointDelta(profile != null ? profile.MaxJointDeltaRad : 0f);

            if (profile != null && profile.Joints != null && profile.Joints.Length > 0)
            {
                var grip = profile.Joints[profile.Joints.Length - 1];
                _jointNames = new[] { grip.Name };
                _gripperOpen = grip.Max;
                _gripperClosed = grip.Min;
                _gripper = grip.Home;
            }
            else
            {
                _jointNames = new[] { "gripper" };
                _gripperOpen = 0.8f;
                _gripperClosed = 0f;
                _gripper = 0f;
            }
            RefreshStatus();
        }

        public void FeedArmInput(ArmOperatorInput input) => _input = input;

        public bool Tick(ref HandPose rightHand, ref HandPose leftHand, IRobotLink link)
        {
            if (!IsConfigured || _profile == null || link == null || !link.IsConnected) return false;

            _frame = _input;
            ClearEdges();
            var signal = _gate.Consume(_frame);
            ArmEnablePublish.Apply(signal, _profile, link);
            RefreshStatus();

            if (_gate.ClutchRising) _suppressMotionThisPublish = true;
            if (!_gate.Flow.MotionAllowed)
            {
                _suppressMotionThisPublish = false;
                return false;
            }

            float dt = _frame.DeltaTime > 0f ? _frame.DeltaTime : Time.deltaTime;
            _publishTimer += dt;
            if (_publishTimer < _publishInterval) return false;
            _publishTimer -= _publishInterval;

            if (!_suppressMotionThisPublish && _frame.TrackingGood)
            {
                float goal = Mathf.Lerp(_gripperOpen, _gripperClosed, Mathf.Clamp01(rightHand.PinchStrength));
                var current = new[] { _gripper };
                JointRateLimiter.StepTowards(current, new[] { goal }, _maxDelta,
                    new[] { _gripperClosed }, new[] { _gripperOpen });
                _gripper = current[0];
            }
            _suppressMotionThisPublish = false;

            if (_profile.Topics == null || string.IsNullOrEmpty(_profile.Topics.ArmCommands)) return false;
            link.Publish(_profile.Topics.ArmCommands, new JointStateMsg((string[])_jointNames.Clone(), new[] { _gripper }));
            return true;
        }

        public void Stop(IRobotLink link)
        {
            var signal = _gate.Flow.Disarm();
            ArmEnablePublish.Apply(signal, _profile, link);
            _publishTimer = 0f;
            _suppressMotionThisPublish = false;
            RefreshStatus();
        }

        public void SetArmEnabled(bool enabled, IRobotLink link)
        {
            var signal = enabled ? _gate.Flow.RequestEnable() : _gate.Flow.Disarm();
            ArmEnablePublish.Apply(signal, _profile, link);
            RefreshStatus();
        }

        private void RefreshStatus() => _status = _gate.Flow.Describe(_frame.TrackingGood, _jointNames[0]);

        private void ClearEdges()
        {
            _input.EnableRequested = false;
            _input.ConfirmRequested = false;
            _input.DisarmRequested = false;
            _input.CancelRequested = false;
            _input.CycleJointPressed = false;
        }
    }
}
