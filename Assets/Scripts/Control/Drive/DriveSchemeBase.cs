using UnityEngine;
using OmniBot.VR.Core;
using OmniBot.VR.Nav;

namespace OmniBot.VR.Control.Drive
{
    /// <summary>
    /// Shared infrastructure for every <see cref="IDriveScheme"/> — the common
    /// plumbing that doesn't change between a mecanum base, a drone, or an
    /// underwater ROV: emergency-stop edge detection (both grips), turbo
    /// (right grip held alone), thumbstick dead-zone, 20 Hz publish-rate timing,
    /// zero-stop after 0.1 s idle, and the <c>/control_mode</c>
    /// teleop↔idle management. Real robots (<c>kind=real</c>) do not publish
    /// <c>nav2</c> on idle — that mode is the Nav2 button or a new goal.
    /// Any stick input while Nav2 is active cancels the goal and returns to teleop.
    ///
    /// Subclasses implement <see cref="ComputeVelocity"/> — the scheme-specific
    /// mapping of dead-zoned thumbsticks + turbo flag → a
    /// <see cref="TwistMsg"/>. Everything else is handled here.
    /// </summary>
    public abstract class DriveSchemeBase : IDriveScheme
    {
        protected const float DeadZone = 0.1f;
        protected const float TurboMultiplier = 2.0f;
        protected const float ZeroStopDelay = 0.1f;       // s before we stop publishing zeros
        protected const float IdleModeRevertDelay = 0.5f; // s idle → revert control mode (sim / non-nav2)
        protected const float PublishHz = 20f;

        protected RobotProfile Profile;
        protected float PublishInterval;
        protected float PublishTimer;

        // ── E-stop / mode state ──────────────────────────────────────────────────
        protected bool EmergencyStopActive;
        protected bool PrevBothGrips;
        protected bool IsPublishing;
        protected float NoInputTimer;

        private readonly ControlModeMachine _mode = new ControlModeMachine();

        public bool IsConfigured => Profile != null;
        public abstract string HudHint { get; }

        /// <summary>
        /// Control-mode string published after the idle timeout. Ground robots
        /// use "nav2", which is suppressed for kind=real so an old goal cannot
        /// resume on its own. Aerial/underwater robots override this (e.g.
        /// "offboard", or an empty string to leave the mode untouched).
        /// </summary>
        protected virtual string IdleMode => "nav2";

        /// <summary>Control-mode string published while the user is driving.</summary>
        protected virtual string ActiveMode => "teleop";

        public virtual void Configure(RobotProfile profile)
        {
            Profile = profile;
            PublishInterval = 1f / PublishHz;
            PublishTimer = 0f;
            EmergencyStopActive = false;
            PrevBothGrips = false;
            IsPublishing = false;
            NoInputTimer = 0f;
            _mode.IdleDelay = IdleModeRevertDelay;
            _mode.Reset(IdleMode, profile != null ? profile.DeploymentKind : RobotDeployment.Real);
            _mode.ActiveMode = ActiveMode;
            if (profile != null)
                NavGoalSession.Active.SetDeployment(profile.DeploymentKind);
        }

        public void Tick(ref TeleopInput input, IRobotLink link)
        {
            if (!IsConfigured || Profile == null || !link.IsConnected) return;

            // ── Emergency stop: both grips (rising/falling edge) ───────────────
            HandleEmergencyStop(ref input, link);
            if (EmergencyStopActive) return;

            // ── Thumbsticks (dead-zoned) ───────────────────────────────────────
            float lx = ApplyDeadZone(input.LeftStick.x);
            float ly = ApplyDeadZone(input.LeftStick.y);
            float rx = ApplyDeadZone(input.RightStick.x);
            float ry = ApplyDeadZone(input.RightStick.y);

            // Turbo: right grip held (but NOT both grips)
            bool turbo = input.RightGrip && !input.LeftGrip;
            float speedMult = turbo ? TurboMultiplier : 1.0f;

            // ── Scheme-specific velocity ───────────────────────────────────────
            bool hasInput;
            TwistMsg twist = ComputeVelocity(lx, ly, rx, ry, speedMult, out hasInput);

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
            bool cancelNav = RobotDeployment.IsReal(Profile.DeploymentKind)
                && (NavGoalSession.Active.GoalActive || NavGoalSession.Active.ModeIsNav2);
            if (cancelNav)
                NavGoalSession.Active.CancelFromButton(link);
            if (link != null && link.IsConnected)
            {
                link.Publish(Profile.Topics.CmdVel, ZeroTwist());
                string idle = _mode.Stop();
                if (cancelNav)
                {
                    // Leave the real robot in teleop so a late Nav2 command cannot run.
                    PublishControlMode(ActiveMode, link);
                    NavGoalSession.Active.NoteModePublished(ActiveMode);
                }
                else if (!string.IsNullOrEmpty(idle))
                {
                    PublishControlMode(idle, link);
                    NavGoalSession.Active.NoteModePublished(idle);
                }
            }
            else
            {
                _mode.Stop();
            }
            IsPublishing = false;
            NoInputTimer = 0f;
        }

        // ── To override ──────────────────────────────────────────────────────────

        /// <summary>
        /// Map dead-zoned thumbstick values + speed multiplier → a Twist message.
        /// <paramref name="lx"/>/<paramref name="ly"/> = left stick X/Y,
        /// <paramref name="rx"/>/<paramref name="ry"/> = right stick X/Y (all in
        /// [-1, 1]). <paramref name="speedMult"/> is 1.0 or 2.0 (turbo). Set
        /// <paramref name="hasInput"/> to false if no axis is active.
        /// </summary>
        protected abstract TwistMsg ComputeVelocity(
            float lx, float ly, float rx, float ry, float speedMult, out bool hasInput);

        // ── Shared helpers ────────────────────────────────────────────────────────

        private void HandleEmergencyStop(ref TeleopInput input, IRobotLink link)
        {
            if (input.BothGripsPressed)
            {
                EmergencyStopActive = true;
                link.Publish(Profile.Topics.EmergencyStop, new BoolMsg(true));
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.RTouch);
                Debug.Log($"[{GetType().Name}] Emergency stop ACTIVATED.");
            }
            else if (input.BothGripsReleased && EmergencyStopActive)
            {
                EmergencyStopActive = false;
                link.Publish(Profile.Topics.EmergencyStop, new BoolMsg(false));
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                Debug.Log($"[{GetType().Name}] Emergency stop RELEASED.");
            }
            PrevBothGrips = input.BothGrips;
        }

        private void UpdateControlMode(bool hasInput, float dt, IRobotLink link)
        {
            if (hasInput) NoInputTimer = 0f;
            else NoInputTimer += dt;

            _mode.ActiveMode = ActiveMode;
            NavStickHandover.Apply(
                _mode,
                NavGoalSession.Active,
                link,
                Profile.Topics.ControlMode,
                hasInput,
                dt);
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
    }
}
