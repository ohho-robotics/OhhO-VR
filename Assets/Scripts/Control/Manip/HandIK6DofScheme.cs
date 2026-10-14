using UnityEngine;
using OmniBot.VR.Core;
using OmniBot.VR.Input;

namespace OmniBot.VR.Control.Manip
{
    /// <summary>
    /// One hand → one 6-DOF arm. Joint names, limits, link lengths, and
    /// kinematic zeros come from <see cref="RobotProfile"/>, which is filled
    /// from <see cref="RobotDescriptor"/> via <see cref="RobotProfileFactory"/>.
    ///
    /// Enable does not move the arm. Sim enables on one explicit request.
    /// Real stays off until a second confirm, and nothing is published on
    /// <c>/arm/enable</c> before that. Motion is published only while the
    /// clutch (right index trigger) is held. The clutch is differential: the
    /// first command after the trigger closes is the pose already held, then
    /// the arm follows hand motion. Joint steps never exceed
    /// <see cref="RobotDescriptor.MaxJointDeltaRad"/>.
    ///
    /// When hand tracking is not high confidence, the clutch jogs one joint
    /// from the right stick instead of solving IK.
    ///
    /// TODO(OHH-94): radian homes are the URDF zero until real STS3215 ticks
    /// are measured. This scheme does not embed servo tick offsets.
    /// </summary>
    public sealed class HandIK6DofScheme : IManipulationScheme
    {
        private const float PublishHz = 20f;
        private const float StillPositionMetres = 0.002f;
        private const float StillRotationDegrees = 1f;

        private RobotProfile _profile;
        private Transform _workspaceOrigin;
        private ArmIKSolver _ikSolver;
        private readonly ArmSessionGate _gate = new ArmSessionGate();
        private readonly CartesianSmoother _smoother = new CartesianSmoother();
        private float[] _currentAngles;
        private string[] _jointNames;
        private float[] _jointMin;
        private float[] _jointMax;
        private float _maxDelta = RobotDescriptor.MaxJointDeltaRad;
        private float _publishInterval;
        private float _publishTimer;
        private ArmOperatorInput _input;
        private ArmOperatorInput _frame;
        private bool _suppressMotionThisPublish;
        private bool _clutchAnchored;
        private Vector3 _handAnchor;
        private Vector3 _eeAtClutch;
        private Quaternion _rotAnchor = Quaternion.identity;
        private int _jogJoint;
        private string _status = "Arm off";

        public bool IsConfigured => _profile != null && _ikSolver != null;
        public bool ArmEnabled => _gate.Flow.TorqueEnabled;
        public ArmEnablePhase EnablePhase => _gate.Flow.Phase;
        public bool ClutchEngaged => _gate.Flow.ClutchEngaged;
        public string StatusText => _status;
        public string HudHint =>
            "Right trigger: clutch · Pinch: gripper · Left X / thumbs-up: enable · REAL: Left X again · Poor tracking: right stick Y jogs";

        public void Configure(RobotProfile profile, Transform workspaceOrigin)
        {
            _profile = profile;
            _workspaceOrigin = workspaceOrigin;
            _ikSolver = new ArmIKSolver();
            _gate.Bind(profile);
            _publishInterval = 1f / PublishHz;
            _publishTimer = 0f;
            _suppressMotionThisPublish = false;
            _clutchAnchored = false;
            _jogJoint = 0;
            _maxDelta = RobotDescriptor.ClampJointDelta(profile != null ? profile.MaxJointDeltaRad : 0f);
            _smoother.MaxSpeedMps = RobotDescriptor.MaxCartesianSpeedMps;
            _smoother.Snap(Vector3.zero);

            int n = profile != null && profile.Joints != null ? profile.Joints.Length : 0;
            _jointNames = new string[n];
            _jointMin = new float[n];
            _jointMax = new float[n];
            _currentAngles = new float[n];
            for (int i = 0; i < n; i++)
            {
                _jointNames[i] = profile.Joints[i].Name;
                _jointMin[i] = profile.Joints[i].Min;
                _jointMax[i] = profile.Joints[i].Max;
                _currentAngles[i] = profile.Joints[i].Home;
            }

            if (_workspaceOrigin == null && profile != null)
            {
                var origin = new GameObject("HandWorkspaceOrigin");
                origin.transform.position = new Vector3(0f, profile.ArmBaseHeight, 0f);
                _workspaceOrigin = origin.transform;
                Debug.LogWarning("[HandIK6DofScheme] workspaceOrigin not set — created default at arm base height.");
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
            if (signal.PhaseChanged) Debug.Log("[HandIK6DofScheme] " + _gate.Flow.Describe(_frame.TrackingGood, JogJointLabel()));
            RefreshStatus();

            if (_gate.ClutchRising)
            {
                _handAnchor = rightHand.Position;
                _rotAnchor = rightHand.Rotation;
                _eeAtClutch = _ikSolver.ForwardEndEffector(_currentAngles);
                _clutchAnchored = true;
                _suppressMotionThisPublish = true;
                _smoother.Snap(_eeAtClutch);
            }

            if (!_gate.Flow.MotionAllowed)
            {
                _suppressMotionThisPublish = false;
                _clutchAnchored = false;
                return false;
            }

            float dt = _frame.DeltaTime > 0f ? _frame.DeltaTime : Time.deltaTime;
            _publishTimer += dt;
            if (_publishTimer < _publishInterval) return false;
            _publishTimer -= _publishInterval;

            if (_suppressMotionThisPublish)
            {
                _suppressMotionThisPublish = false;
                PublishJoints(link);
                return true;
            }

            if (!_frame.TrackingGood)
            {
                ApplyJog(dt);
                PublishJoints(link);
                return true;
            }

            if (_profile.IkLocation == IkLocation.Robot &&
                !string.IsNullOrEmpty(_profile.Topics.IkTargetPose))
            {
                return PublishRobotIk(rightHand, dt, link);
            }

            ApplyHandIk(rightHand, dt);
            PublishJoints(link);
            return true;
        }

        public void Stop(IRobotLink link)
        {
            var signal = _gate.Flow.Disarm();
            ArmEnablePublish.Apply(signal, _profile, link);
            _publishTimer = 0f;
            _suppressMotionThisPublish = false;
            _clutchAnchored = false;
            RefreshStatus();
        }

        public void SetArmEnabled(bool enabled, IRobotLink link)
        {
            // true is a request, not a REAL confirm. false disarms immediately.
            var signal = enabled ? _gate.Flow.RequestEnable() : _gate.Flow.Disarm();
            ArmEnablePublish.Apply(signal, _profile, link);
            RefreshStatus();
        }

        private void ApplyJog(float dt)
        {
            if (_currentAngles.Length == 0) return;
            if (_frame.CycleJointPressed)
                _jogJoint = (_jogJoint + 1) % _currentAngles.Length;

            var target = (float[])_currentAngles.Clone();
            int index = Mathf.Clamp(_jogJoint, 0, target.Length - 1);
            target[index] = _currentAngles[index] + JointJog.DeltaRad(_frame.JogAxis, dt);
            JointRateLimiter.StepTowards(_currentAngles, target, _maxDelta, _jointMin, _jointMax);
            RefreshStatus();
        }

        private void ApplyHandIk(HandPose rightHand, float dt)
        {
            if (_currentAngles.Length == 0) return;
            var targetAngles = (float[])_currentAngles.Clone();
            if (HandMoved(rightHand))
            {
                Vector3 target = ClutchTarget(rightHand, dt);
                float[] solved = _ikSolver.SolveIK(target, rightHand.Rotation, _currentAngles);
                int n = targetAngles.Length < solved.Length ? targetAngles.Length : solved.Length;
                for (int i = 0; i < n; i++) targetAngles[i] = solved[i];
            }

            if (targetAngles.Length > 0)
                targetAngles[targetAngles.Length - 1] = PinchToGripper(rightHand.PinchStrength);

            JointRateLimiter.StepTowards(_currentAngles, targetAngles, _maxDelta, _jointMin, _jointMax);
        }

        private bool PublishRobotIk(HandPose rightHand, float dt, IRobotLink link)
        {
            if (!HandMoved(rightHand)) return false;
            Vector3 target = ClutchTarget(rightHand, dt);
            Vector3 localPos = _workspaceOrigin.InverseTransformPoint(target);
            Quaternion localRot = Quaternion.Inverse(_workspaceOrigin.rotation) * rightHand.Rotation;
            var pose = new PoseStampedMsg(
                new Vector3Msg(localPos.x, localPos.y, localPos.z),
                new QuaternionMsg(localRot.x, localRot.y, localRot.z, localRot.w));
            link.Publish(_profile.Topics.IkTargetPose, pose);
            return true;
        }

        private Vector3 ClutchTarget(HandPose rightHand, float dt)
        {
            float reach = _profile.ArmMaxReach > 0f ? _profile.ArmMaxReach : RobotDescriptor.So101.Reach;
            Vector3 delta = _clutchAnchored ? (rightHand.Position - _handAnchor) * Scale() : Vector3.zero;
            if (reach > 0f) delta = Vector3.ClampMagnitude(delta, reach);
            Vector3 desired = _eeAtClutch + delta;
            return _smoother.MoveTowards(desired, dt);
        }

        private bool HandMoved(HandPose rightHand)
        {
            if (!_clutchAnchored) return true;
            if ((rightHand.Position - _handAnchor).sqrMagnitude > StillPositionMetres * StillPositionMetres)
                return true;
            if (rightHand.Rotation == _rotAnchor) return false;
            return Quaternion.Angle(rightHand.Rotation, _rotAnchor) > StillRotationDegrees;
        }

        private float Scale()
        {
            float radius = _profile.HandWorkspaceRadius;
            if (radius < 1e-4f) radius = RobotDescriptor.So101.HandWorkspaceRadius;
            return _profile.ArmMaxReach / radius;
        }

        private void PublishJoints(IRobotLink link)
        {
            if (_profile.Topics == null || string.IsNullOrEmpty(_profile.Topics.ArmCommands) || _jointNames.Length == 0) return;
            var msg = new JointStateMsg((string[])_jointNames.Clone(), (float[])_currentAngles.Clone());
            link.Publish(_profile.Topics.ArmCommands, msg);
        }

        private float PinchToGripper(float pinchStrength)
        {
            if (_jointMax.Length == 0) return 0f;
            int last = _jointMax.Length - 1;
            return Mathf.Lerp(_jointMax[last], _jointMin[last], Mathf.Clamp01(pinchStrength));
        }

        private string JogJointLabel()
        {
            if (_jointNames == null || _jointNames.Length == 0) return "joint";
            int index = Mathf.Clamp(_jogJoint, 0, _jointNames.Length - 1);
            return _jointNames[index];
        }

        private void RefreshStatus()
        {
            _status = _gate.Flow.Describe(_frame.TrackingGood, JogJointLabel());
        }

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
