using UnityEngine;
using OmniBot.VR.Core;
using OmniBot.VR.Input;
using OmniBot.VR.Video;
using OmniBot.VR.Recording;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Control.Manip;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// The teleop control layer entry point — bridges VR input (OVR controllers +
    /// hand tracking) to the profile-driven <see cref="IDriveScheme"/> +
    /// <see cref="IManipulationScheme"/> selected for the robot the user picked in
    /// their garage. This is the Phase 2 spine: put on the headset, sign in, pick
    /// a robot, and <see cref="StartTeleop"/> wires its profile into the schemes;
    /// every frame after, this controller pumps thumbsticks → drive and hand
    /// poses → manipulation, publishing to the robot over the
    /// <see cref="IRobotLink"/>.
    ///
    /// Replaces the legacy <c>Input/BaseController.cs</c> +
    /// <c>Input/HandTrackingArmController.cs</c> as the single teleop GameObject.
    /// Those MonoBehaviours were hardcoded to OmniBot; this one is agnostic — the
    /// profile decides what gets driven and how.
    /// </summary>
    public class TeleopController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Hand tracking provider (assign the scene GestureDetector, or auto-found).")]
        [SerializeField] private GestureDetector gestureDetector;

        [Tooltip("Transform at the robot arm's base in the MR scene. Hand position is measured relative to this.")]
        [SerializeField] private Transform handWorkspaceOrigin;

        [Header("Link")]
        [Tooltip("Robot transport. Defaults to the ROSBridge singleton (RosBridgeLink).")]
        [SerializeField] private MonoBehaviour linkProvider;

        [Header("Phase 4 — video + recording")]
        [Tooltip("Profile-driven camera feed. Configure() is called on StartTeleop.")]
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("Profile-driven dataset recorder. Configure() is called on StartTeleop.")]
        [SerializeField] private ProfileDrivenRecorder recorder;

        private IRobotLink _link;
        private IRobotLink Link => _link ?? (_link = ResolveLink());

        private IRobotLink ResolveLink()
        {
            if (linkProvider is IRobotLink provider) return provider;
            return RosBridgeLink.Instance;
        }

        // ── Active control state ────────────────────────────────────────────────
        public RobotProfile Profile { get; private set; }
        private IDriveScheme _drive;
        private IManipulationScheme _manip;
        private bool _teleopActive;

        /// <summary>One-line drive control hint (for onboarding / HUD).</summary>
        public string DriveHint => _drive?.HudHint ?? "No drive on this robot";
        /// <summary>One-line manipulation control hint (for onboarding / HUD).</summary>
        public string ManipHint => _manip?.HudHint ?? "No arm on this robot";
        /// <summary>Live arm-gate status (enable, REAL confirm, clutch, jog).</summary>
        public string ArmStatus => _manip?.StatusText ?? "";

        // ── Thumbs-up gesture state (ported from HandTrackingArmController) ──────
        private bool _wasThumbsUp;
        private float _thumbsUpHoldTimer;
        private const float ThumbsUpHoldRequired = 0.5f;
        private const float ClutchTriggerThreshold = 0.6f;

        private bool _originOwned;
        private GameObject _ownedOrigin;
        private WorkspaceSphereVisual _workspaceVisual;

        // ── Previous both-grips state for edge detection ─────────────────────────
        private bool _prevBothGrips;

        // ── Unity lifecycle ──────────────────────────────────────────────────────
        private void Start()
        {
            if (gestureDetector == null)
                gestureDetector = FindObjectOfType<GestureDetector>();
        }

        private void Update()
        {
            if (!_teleopActive || !Link.IsConnected) return;

            // ── Build the controller input snapshot ──────────────────────────────
            Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);

            bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) ||
                            OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.8f;
            bool rightGrip = OVRInput.Get(OVRInput.Button.SecondaryHandTrigger, OVRInput.Controller.RTouch) ||
                             OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger, OVRInput.Controller.RTouch) > 0.8f;
            bool bothGrips = leftGrip && rightGrip;

            var input = new TeleopInput
            {
                LeftStick = leftStick,
                RightStick = rightStick,
                LeftGrip = leftGrip,
                RightGrip = rightGrip,
                BothGrips = bothGrips,
                BothGripsPressed = bothGrips && !_prevBothGrips,
                BothGripsReleased = !bothGrips && _prevBothGrips,
                DeltaTime = Time.deltaTime,
            };
            _prevBothGrips = bothGrips;

            // ── Drive ────────────────────────────────────────────────────────────
            if (_drive != null)
                _drive.Tick(ref input, Link);

            // ── Manipulation (hand poses) ────────────────────────────────────────
            // Runs without a GestureDetector so controller jog and the enable
            // button still work when hand tracking is absent.
            if (_manip != null)
            {
                HandPose rightHand = default;
                HandPose leftHand = default;
                bool trackingGood = false;
                if (gestureDetector != null)
                {
                    rightHand = BuildHandPose(gestureDetector.rightHand);
                    leftHand = BuildHandPose(gestureDetector.leftHand);
                    trackingGood = rightHand.Quality == HandTrackingQuality.Good;
                    TickThumbsUpToggle(gestureDetector.leftHand);
                }

                bool clutch = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger, OVRInput.Controller.RTouch) >= ClutchTriggerThreshold
                    || OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger, OVRInput.Controller.RTouch);
                bool xDown = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch);
                bool cycleJoint = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
                bool thumbsPulse = _thumbsUpPulse;
                _thumbsUpPulse = false;

                var armInput = new ArmOperatorInput
                {
                    ClutchHeld = clutch,
                    TrackingGood = trackingGood,
                    JogAxis = rightStick.y,
                    CycleJointPressed = cycleJoint,
                    EmergencyStop = bothGrips,
                    DeltaTime = Time.deltaTime,
                };
                // Left X confirms a pending REAL enable. A thumbs-up never does.
                // Either control disarms immediately, and either requests enable
                // when the arm is off. One press cannot both request and confirm.
                var phase = _manip.EnablePhase;
                if (phase == ArmEnablePhase.AwaitingRealConfirmation && xDown)
                    armInput.ConfirmRequested = true;
                else if (phase == ArmEnablePhase.Armed && (xDown || thumbsPulse))
                    armInput.DisarmRequested = true;
                else if (phase == ArmEnablePhase.Off && (xDown || thumbsPulse))
                    armInput.EnableRequested = true;

                _manip.FeedArmInput(armInput);
                _manip.Tick(ref rightHand, ref leftHand, Link);

                if (_workspaceVisual != null)
                    _workspaceVisual.SetAppearance(_manip.EnablePhase, _manip.ClutchEngaged, trackingGood);
            }
        }

        private bool _thumbsUpPulse;

        // ── Public API (called by GaragePanelController / OhhoVrApp) ──────────────

        /// <summary>
        /// Start teleoperating the selected robot. Builds the schemes from the
        /// profile, advertises the topics, and begins pumping input every frame.
        /// </summary>
        public void StartTeleop(RobotProfile profile)
        {
            if (profile == null) { Debug.LogWarning("[TeleopController] StartTeleop called with null profile."); return; }

            StopTeleop();

            Profile = profile;
            if (profile.HasArm)
                EnsureWorkspaceOrigin(profile);
            _drive = ControlSchemeFactory.CreateDriveScheme(profile);
            _manip = ControlSchemeFactory.CreateManipulationScheme(profile, handWorkspaceOrigin);
            if (profile.HasArm)
                EnsureWorkspaceVisual(profile);

            AdvertiseTopics(profile);
            _teleopActive = true;

            // Phase 4: configure the camera feed + recorder from the profile
            if (cameraFeed != null) cameraFeed.Configure(profile);
            if (recorder != null) recorder.Configure(profile);

            Debug.Log($"[TeleopController] Teleop started — {profile.Name} ({profile.Drive}, arm {(profile.HasArm ? $"{profile.ArmDof}-DOF" : "none")}), IK={profile.IkLocation}.");
        }

        /// <summary>Stop teleoperation: zero velocity, disarm arm, drop the schemes.</summary>
        public void StopTeleop()
        {
            if (!_teleopActive && _drive == null && _manip == null) return;
            _drive?.Stop(Link);
            _manip?.Stop(Link);
            _drive = null;
            _manip = null;
            Profile = null;
            _teleopActive = false;
            _wasThumbsUp = false;
            _thumbsUpHoldTimer = 0f;
            _thumbsUpPulse = false;
            if (_workspaceVisual != null)
            {
                Destroy(_workspaceVisual.gameObject);
                _workspaceVisual = null;
            }
            if (_originOwned && _ownedOrigin != null)
            {
                Destroy(_ownedOrigin);
                _ownedOrigin = null;
                handWorkspaceOrigin = null;
                _originOwned = false;
            }
        }

        /// <summary>Whether a robot is currently being teleoperated.</summary>
        public bool IsTeleopActive => _teleopActive;

        private void EnsureWorkspaceOrigin(RobotProfile profile)
        {
            if (handWorkspaceOrigin != null || profile == null) return;
            _ownedOrigin = new GameObject("HandWorkspaceOrigin");
            _ownedOrigin.transform.position = new Vector3(0f, profile.ArmBaseHeight, 0f);
            handWorkspaceOrigin = _ownedOrigin.transform;
            _originOwned = true;
        }

        private void EnsureWorkspaceVisual(RobotProfile profile)
        {
            if (handWorkspaceOrigin == null || profile == null) return;
            var go = new GameObject("ArmWorkspaceSphere");
            go.transform.SetParent(handWorkspaceOrigin, false);
            _workspaceVisual = go.AddComponent<WorkspaceSphereVisual>();
            _workspaceVisual.Configure(profile.HandWorkspaceRadius > 0f
                ? profile.HandWorkspaceRadius
                : RobotDescriptor.So101.HandWorkspaceRadius);
        }

        // ── Input helpers ─────────────────────────────────────────────────────────

        private HandPose BuildHandPose(OVRHand hand)
        {
            if (gestureDetector == null || hand == null || !gestureDetector.IsHandTracked(hand))
                return default;

            bool good = gestureDetector.IsHandTrackingGood(hand);
            return new HandPose
            {
                Tracked = true,
                Quality = good ? HandTrackingQuality.Good : HandTrackingQuality.Poor,
                Position = gestureDetector.GetHandPosition(hand),
                Rotation = gestureDetector.GetHandRotation(hand),
                PinchStrength = gestureDetector.GetPinchStrength(hand),
            };
        }

        // ── Thumbs-up arm-enable toggle (ported from HandTrackingArmController) ────
        private void TickThumbsUpToggle(OVRHand leftHand)
        {
            if (_manip == null || gestureDetector == null || leftHand == null ||
                !gestureDetector.IsHandTracked(leftHand)) return;

            bool thumbsUp = IsThumbsUp(leftHand);
            if (thumbsUp)
            {
                _thumbsUpHoldTimer += Time.deltaTime;
                if (!_wasThumbsUp && _thumbsUpHoldTimer >= ThumbsUpHoldRequired)
                {
                    // Request only. REAL confirmation is Left X, not this gesture.
                    _thumbsUpPulse = true;
                    _wasThumbsUp = true;
                }
            }
            else
            {
                _thumbsUpHoldTimer = 0f;
                _wasThumbsUp = false;
            }
        }

        private bool IsThumbsUp(OVRHand hand)
        {
            if (hand == null || gestureDetector == null || !gestureDetector.IsHandTracked(hand)) return false;

            OVRSkeleton skeleton = hand.GetComponent<OVRSkeleton>();
            if (skeleton == null || !skeleton.IsInitialized)
            {
                // Fallback: pinch-strength heuristic
                float indexPinch = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                float middlePinch = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
                float ringPinch = hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
                float pinkyPinch = hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);
                bool othersCurled = middlePinch > 0.6f && ringPinch > 0.6f && pinkyPinch > 0.6f;
                bool thumbFree = indexPinch < 0.3f;
                bool pointingUp = Vector3.Dot(hand.transform.up, Vector3.up) > 0.7f;
                return othersCurled && thumbFree && pointingUp;
            }

            var bones = skeleton.Bones;
            if (bones == null || bones.Count < 24) return false;

            Vector3 wristPos = bones[0].Transform.position;
            Vector3 thumbTip = bones[(int)OVRSkeleton.BoneId.Hand_ThumbTip].Transform.position;
            Vector3 indexTip = bones[(int)OVRSkeleton.BoneId.Hand_IndexTip].Transform.position;
            Vector3 middleTip = bones[(int)OVRSkeleton.BoneId.Hand_MiddleTip].Transform.position;
            Vector3 ringTip = bones[(int)OVRSkeleton.BoneId.Hand_RingTip].Transform.position;
            Vector3 pinkyTip = bones[(int)OVRSkeleton.BoneId.Hand_PinkyTip].Transform.position;

            bool thumbExtended = thumbTip.y > wristPos.y + 0.04f;
            float curled = 0.07f;
            bool indexCurled = Vector3.Distance(indexTip, wristPos) < curled;
            bool middleCurled = Vector3.Distance(middleTip, wristPos) < curled;
            bool ringCurled = Vector3.Distance(ringTip, wristPos) < curled;
            bool pinkyCurled = Vector3.Distance(pinkyTip, wristPos) < curled;

            return thumbExtended && indexCurled && middleCurled && ringCurled && pinkyCurled;
        }

        // ── Topic advertising ─────────────────────────────────────────────────────
        private void AdvertiseTopics(RobotProfile p)
        {
            if (!Link.IsConnected || p?.Topics == null) return;

            if (!string.IsNullOrEmpty(p.Topics.CmdVel))
                Link.Advertise(p.Topics.CmdVel, "geometry_msgs/Twist");
            if (!string.IsNullOrEmpty(p.Topics.ControlMode))
                Link.Advertise(p.Topics.ControlMode, "std_msgs/String");
            if (!string.IsNullOrEmpty(p.Topics.ArmCommands))
                Link.Advertise(p.Topics.ArmCommands, "sensor_msgs/JointState");
            if (!string.IsNullOrEmpty(p.Topics.ArmEnable))
                Link.Advertise(p.Topics.ArmEnable, "std_msgs/Bool");
            if (!string.IsNullOrEmpty(p.Topics.EmergencyStop))
                Link.Advertise(p.Topics.EmergencyStop, "std_msgs/Bool");
            if (!string.IsNullOrEmpty(p.Topics.IkTargetPose))
                Link.Advertise(p.Topics.IkTargetPose, "geometry_msgs/PoseStamped");
        }
    }
}
