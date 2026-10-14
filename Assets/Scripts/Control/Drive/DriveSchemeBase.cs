using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control.Drive
{
    /// <summary>
    /// Shared infrastructure for every <see cref="IDriveScheme"/> — the common
    /// plumbing that doesn't change between a mecanum base, a drone, or an
    /// underwater ROV: latched emergency stop (A+B or the HUD button; cleared
    /// only by an explicit Reset followed by a deadman release and re-press),
    /// turbo (right trigger, sim sessions only, then clamped to the robot-class
    /// hard cap), deadman (left grip, on unless this is a kind=sim session that
    /// opted out), thumbstick dead-zone, 20 Hz publish-rate timing, zero-stop
    /// after 0.1 s idle, and the <c>/control_mode</c> teleop↔idle management.
    ///
    /// Subclasses implement <see cref="ComputeVelocity"/> — the scheme-specific
    /// mapping of dead-zoned thumbsticks + turbo flag → a
    /// <see cref="TwistMsg"/>. Everything else is handled here.
    /// </summary>
    public abstract class DriveSchemeBase : IDriveScheme
    {
        protected const float DeadZone = 0.1f;
        protected const float TurboMultiplier = 2.0f;

        /// <summary>
        /// OmniBot hard linear cap, in m/s. Same constant as
        /// <see cref="RobotConfig.MAX_LINEAR_VEL"/>. Per-robot ceilings live on
        /// <see cref="RobotProfile.HardMaxLinVel"/>; this remains the OmniBot value
        /// callers and tests compare against.
        /// </summary>
        public const float HardTurboLinCapMps = RobotConfig.MAX_LINEAR_VEL;

        protected const float ZeroStopDelay = 0.1f;       // s before we stop publishing zeros
        protected const float IdleModeRevertDelay = 0.5f; // s idle → revert control mode
        protected const float PublishHz = 20f;

        protected RobotProfile Profile;
        protected float PublishInterval;
        protected float PublishTimer;

        // ── E-stop / mode state ──────────────────────────────────────────────────
        protected bool EmergencyStopActive;
        protected bool IsPublishing;
        protected float NoInputTimer;
        protected float NoInputModeTimer;
        protected bool InTeleopMode;

        // Operator clear is two more steps after the latch: an explicit Reset,
        // then the left-grip deadman must be released and pressed again.
        private bool _resetAccepted;
        private bool _deadmanReleasedAfterReset;

        public bool IsConfigured => Profile != null;
        public abstract string HudHint { get; }

        /// <summary>True while emergency stop is latched.</summary>
        public bool IsEmergencyStopActive => EmergencyStopActive;

        /// <summary>
        /// When true, the left grip must be held to drive. Releasing it publishes
        /// zero velocity on that same tick. Defaults to true. Assigning false is
        /// ignored unless the configured profile is a kind=sim session; call
        /// <see cref="DisableDeadmanForSimulation"/> to opt out explicitly.
        /// </summary>
        public bool RequireDeadman
        {
            get => _requireDeadman;
            set
            {
                if (!value && (Profile == null || Profile.Kind != SessionKind.Sim))
                {
                    _requireDeadman = true;
                    return;
                }
                _requireDeadman = value;
            }
        }

        private bool _requireDeadman = true;

        /// <summary>Linear speed scale (m/s) for the tick in progress, already under the hard cap.</summary>
        protected float ActiveLinVelLimit;

        /// <summary>Angular speed scale (rad/s) for the tick in progress, already under the hard cap.</summary>
        protected float ActiveAngVelLimit;

        /// <summary>
        /// HUD fragment shared by every scheme. Always states whether the deadman
        /// is on, whether turbo is available, and that e-stop is latched.
        /// </summary>
        protected string SafetyHudSuffix
        {
            get
            {
                string deadman = RequireDeadman
                    ? " · Deadman ON (left grip required)"
                    : " · Deadman OFF (sim session)";
                string turbo = Profile != null && Profile.Kind == SessionKind.Sim
                    ? $" · Right trigger: turbo (clamped to {LinearCapLabel()} m/s)"
                    : " · Turbo disabled (real robot)";
                return deadman + turbo
                    + " · A+B or HUD: e-stop (latched until Reset, then release and re-press the deadman)";
            }
        }

        /// <summary>Control-mode string published after the idle timeout.</summary>
        protected virtual string IdleMode => "nav2";

        /// <summary>Control-mode string published while the user is driving.</summary>
        protected virtual string ActiveMode => "teleop";

        public virtual void Configure(RobotProfile profile)
        {
            Profile = profile;
            PublishInterval = 1f / PublishHz;
            PublishTimer = 0f;
            EmergencyStopActive = false;
            _resetAccepted = false;
            _deadmanReleasedAfterReset = false;
            IsPublishing = false;
            NoInputTimer = 0f;
            NoInputModeTimer = 0f;
            InTeleopMode = false;
            if (profile != null) RobotClassCaps.Ensure(profile);
            // Null profile keeps the safe default (deadman on). False sticks only
            // when this profile is a kind=sim session.
            RequireDeadman = profile == null || profile.RequireDeadman;
            ActiveLinVelLimit = profile != null ? Mathf.Max(0f, profile.MaxLinVel) : 0f;
            ActiveAngVelLimit = profile != null ? Mathf.Max(0f, profile.MaxAngVel) : 0f;
        }

        /// <summary>
        /// Simulation-only deadman opt-out. No effect unless the profile's session
        /// kind is <see cref="SessionKind.Sim"/>.
        /// </summary>
        public void DisableDeadmanForSimulation()
        {
            if (Profile == null || Profile.Kind != SessionKind.Sim) return;
            Profile.DisableDeadmanForSimulation();
            _requireDeadman = false;
        }

        /// <summary>True when the left grip (the deadman) is held.</summary>
        public virtual bool IsDeadmanEngaged(in TeleopInput input)
        {
            return input.LeftGrip;
        }

        public void Tick(ref TeleopInput input, IRobotLink link)
        {
            if (!IsConfigured || Profile == null || !link.IsConnected) return;

            RobotClassCaps.Ensure(Profile);

            // ── Emergency stop: A+B or HUD, latched until Reset + deadman cycle ─
            HandleEmergencyStop(ref input, link);
            if (EmergencyStopActive)
            {
                if (IsPublishing)
                {
                    link.Publish(Profile.Topics.CmdVel, ZeroTwist());
                    IsPublishing = false;
                }
                UpdateControlMode(false, input.DeltaTime, link);
                return;
            }

            // ── Deadman: left grip. Release publishes zero on this same tick. ──
            bool deadmanEngaged = !RequireDeadman || IsDeadmanEngaged(input);
            if (!deadmanEngaged)
            {
                if (IsPublishing)
                {
                    link.Publish(Profile.Topics.CmdVel, ZeroTwist());
                    IsPublishing = false;
                }
                UpdateControlMode(false, input.DeltaTime, link);
                return;
            }

            // ── Thumbsticks (dead-zoned) ───────────────────────────────────────
            float lx = ApplyDeadZone(input.LeftStick.x);
            float ly = ApplyDeadZone(input.LeftStick.y);
            float rx = ApplyDeadZone(input.RightStick.x);
            float ry = ApplyDeadZone(input.RightStick.y);

            // Turbo is the right trigger, and only in a kind=sim session. Real
            // robots ignore it. The multiplier is applied by the scheme; the hard
            // cap below is applied after that so turbo cannot exceed the class ceiling.
            bool turbo = Profile.Kind == SessionKind.Sim && input.RightTrigger;
            float speedMult = turbo ? TurboMultiplier : 1.0f;

            float hardLin = Profile.HardMaxLinVel > 0f ? Profile.HardMaxLinVel : HardTurboLinCapMps;
            float hardAng = Profile.HardMaxAngVel > 0f ? Profile.HardMaxAngVel : RobotConfig.MAX_ANGULAR_VEL;
            float calibratedLin = Mathf.Min(Mathf.Max(0f, Profile.MaxLinVel), hardLin);
            float calibratedAng = Mathf.Min(Mathf.Max(0f, Profile.MaxAngVel), hardAng);
            ActiveLinVelLimit = calibratedLin;
            ActiveAngVelLimit = calibratedAng;

            // ── Scheme-specific velocity ───────────────────────────────────────
            bool hasInput;
            TwistMsg twist = ComputeVelocity(lx, ly, rx, ry, speedMult, out hasInput);
            // Hard caps run after calibration (already in the limits) and after turbo.
            ClampLinearSpeed(twist, hardLin);
            ClampAngularSpeed(twist, hardAng);

            // ── Control-mode management ────────────────────────────────────────
            UpdateControlMode(hasInput, input.DeltaTime, link);

            // ── Publish at 20 Hz ───────────────────────────────────────────────
            PublishTimer += input.DeltaTime;
            if (PublishTimer < PublishInterval) return;
            PublishTimer -= PublishInterval;

            bool shouldPublish = hasInput || NoInputTimer < ZeroStopDelay;
            if (shouldPublish)
            {
                link.Publish(Profile.Topics.CmdVel, twist);
                IsPublishing = true;
            }
            else if (IsPublishing)
            {
                link.Publish(Profile.Topics.CmdVel, ZeroTwist());
                IsPublishing = false;
            }
        }

        public void Stop(IRobotLink link)
        {
            if (!IsConfigured || Profile == null) return;
            if (link != null && link.IsConnected)
            {
                link.Publish(Profile.Topics.CmdVel, ZeroTwist());
                if (InTeleopMode) PublishControlMode(IdleMode, link);
            }
            InTeleopMode = false;
            IsPublishing = false;
            NoInputTimer = 0f;
            NoInputModeTimer = 0f;
        }

        // ── To override ──────────────────────────────────────────────────────────

        /// <summary>
        /// Map dead-zoned thumbstick values + speed multiplier → a Twist message.
        /// Scale linear axes by <see cref="ActiveLinVelLimit"/> and angular axes by
        /// <see cref="ActiveAngVelLimit"/>. Both are already at or under the class
        /// hard cap; <see cref="Tick"/> clamps again after this returns.
        /// <paramref name="speedMult"/> is 1.0, or 2.0 when sim turbo is held.
        /// </summary>
        protected abstract TwistMsg ComputeVelocity(
            float lx, float ly, float rx, float ry, float speedMult, out bool hasInput);

        // ── Shared helpers ────────────────────────────────────────────────────────

        public void TriggerEmergencyStop(IRobotLink link)
        {
            EmergencyStopActive = true;
            _resetAccepted = false;
            _deadmanReleasedAfterReset = false;
            if (link != null && link.IsConnected && Profile?.Topics != null)
            {
                if (!string.IsNullOrEmpty(Profile.Topics.EmergencyStop))
                    link.Publish(Profile.Topics.EmergencyStop, new BoolMsg(true));
                if (!string.IsNullOrEmpty(Profile.Topics.CmdVel))
                    link.Publish(Profile.Topics.CmdVel, ZeroTwist());
            }
            IsPublishing = false;
            VibrateControllers(1f);
        }

        /// <summary>
        /// System clear (session teardown, tests). The operator path does not call
        /// this: releasing the buttons does nothing, and a Reset only arms the
        /// deadman release/re-press cycle inside <see cref="HandleEmergencyStop"/>.
        /// </summary>
        public void ClearEmergencyStop(IRobotLink link)
        {
            EmergencyStopActive = false;
            _resetAccepted = false;
            _deadmanReleasedAfterReset = false;
            if (link != null && link.IsConnected && Profile?.Topics != null)
            {
                if (!string.IsNullOrEmpty(Profile.Topics.EmergencyStop))
                    link.Publish(Profile.Topics.EmergencyStop, new BoolMsg(false));
            }
            VibrateControllers(0f);
        }

        private void HandleEmergencyStop(ref TeleopInput input, IRobotLink link)
        {
            if (input.EmergencyStopPressed || input.HudEmergencyStop)
            {
                _resetAccepted = false;
                _deadmanReleasedAfterReset = false;
                if (!EmergencyStopActive)
                    TriggerEmergencyStop(link);
                else
                    Debug.Log($"[{GetType().Name}] Emergency stop still latched.");
                return;
            }

            if (!EmergencyStopActive) return;

            // Holding the e-stop chord blocks the clear.
            if (input.ButtonA && input.ButtonB) return;

            if (input.ResetEmergencyStop)
                _resetAccepted = true;

            // The release has to be observed after Reset. A grip that was already
            // up on the Reset tick counts; the re-press is a later tick.
            if (_resetAccepted && !input.LeftGrip)
                _deadmanReleasedAfterReset = true;

            if (_resetAccepted && _deadmanReleasedAfterReset && input.LeftGrip)
            {
                ClearEmergencyStop(link);
                Debug.Log($"[{GetType().Name}] Emergency stop reset after deadman re-press.");
            }
        }

        private string LinearCapLabel()
        {
            float cap = Profile != null && Profile.HardMaxLinVel > 0f
                ? Profile.HardMaxLinVel
                : HardTurboLinCapMps;
            return cap.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void VibrateControllers(float amplitude)
        {
            try
            {
                if (Application.isPlaying)
                {
                    OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch);
                    OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.RTouch);
                }
            }
            catch
            {
                // Ignored in test runner / headless mode where OVRPlugin is not loaded
            }
        }

        private void UpdateControlMode(bool hasInput, float dt, IRobotLink link)
        {
            if (hasInput)
            {
                NoInputTimer = 0f;
                NoInputModeTimer = 0f;
                if (!InTeleopMode)
                {
                    InTeleopMode = true;
                    PublishControlMode(ActiveMode, link);
                }
            }
            else
            {
                NoInputTimer += dt;
                NoInputModeTimer += dt;
                if (InTeleopMode && NoInputModeTimer >= IdleModeRevertDelay)
                {
                    InTeleopMode = false;
                    PublishControlMode(IdleMode, link);
                }
            }
        }

        protected void PublishControlMode(string mode, IRobotLink link)
            => link.Publish(Profile.Topics.ControlMode, new StringMsg(mode));

        protected static float ApplyDeadZone(float value)
        {
            if (Mathf.Abs(value) < DeadZone) return 0f;
            float sign = Mathf.Sign(value);
            float scaled = (Mathf.Abs(value) - DeadZone) / (1f - DeadZone);
            return sign * Mathf.Clamp01(scaled);
        }

        protected static TwistMsg ZeroTwist()
            => new TwistMsg(new Vector3Msg(0f, 0f, 0f), new Vector3Msg(0f, 0f, 0f));

        /// <summary>Clamp the linear vector so its components cannot exceed <paramref name="limit"/> m/s.</summary>
        protected static void ClampLinearSpeed(TwistMsg twist, float limit)
        {
            if (twist?.linear == null) return;
            float cap = Mathf.Abs(limit);
            twist.linear.x = Mathf.Clamp(twist.linear.x, -cap, cap);
            twist.linear.y = Mathf.Clamp(twist.linear.y, -cap, cap);
            twist.linear.z = Mathf.Clamp(twist.linear.z, -cap, cap);
        }

        /// <summary>Clamp the angular vector so its components cannot exceed <paramref name="limit"/> rad/s.</summary>
        protected static void ClampAngularSpeed(TwistMsg twist, float limit)
        {
            if (twist?.angular == null) return;
            float cap = Mathf.Abs(limit);
            twist.angular.x = Mathf.Clamp(twist.angular.x, -cap, cap);
            twist.angular.y = Mathf.Clamp(twist.angular.y, -cap, cap);
            twist.angular.z = Mathf.Clamp(twist.angular.z, -cap, cap);
        }
    }
}
