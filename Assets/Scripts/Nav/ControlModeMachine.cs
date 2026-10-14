using System;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Nav
{
    /// <summary>
    /// Teleop ↔ idle control-mode decisions for one drive scheme.
    /// Real robots (<c>kind=real</c>) do not publish <c>nav2</c> when the sticks
    /// go idle. Simulated robots keep the previous 0.5 s revert. Aerial and
    /// thruster idle modes (<c>offboard</c>) are unchanged.
    /// </summary>
    public sealed class ControlModeMachine
    {
        public const string TeleopMode = "teleop";
        public const float DefaultIdleDelay = 0.5f;

        public float IdleDelay = DefaultIdleDelay;
        public string ActiveMode = TeleopMode;
        public string SchemeIdleMode = "nav2";
        public bool InTeleop { get; private set; }

        private string _deploymentKind = RobotDeployment.Real;
        private float _modeTimer;
        private bool _navEngaged;

        public string DeploymentKind
        {
            get => _deploymentKind;
            set => _deploymentKind = RobotDeployment.Normalize(value);
        }

        public struct Decision
        {
            public string PublishMode;
            public bool CancelNavGoal;
            public bool IdleHeldTeleop;
        }

        public void Reset(string schemeIdleMode, string deploymentKind)
        {
            SchemeIdleMode = schemeIdleMode ?? "";
            DeploymentKind = deploymentKind;
            ActiveMode = TeleopMode;
            InTeleop = false;
            _modeTimer = 0f;
            _navEngaged = false;
        }

        /// <summary>Latest knowledge from <see cref="NavGoalSession"/> before <see cref="Tick"/>.</summary>
        public void Observe(bool navEngaged) => _navEngaged = navEngaged;

        public static string ResolveIdle(string schemeIdleMode, string deploymentKind)
        {
            if (string.IsNullOrEmpty(schemeIdleMode)) return null;
            if (string.Equals(schemeIdleMode, "nav2", StringComparison.OrdinalIgnoreCase)
                && RobotDeployment.IsReal(deploymentKind))
                return null;
            return schemeIdleMode;
        }

        public Decision Tick(bool hasInput, float dt)
        {
            if (hasInput)
            {
                _modeTimer = 0f;
                bool cancel = _navEngaged;
                bool publish = !InTeleop || cancel;
                InTeleop = true;
                if (cancel) _navEngaged = false;
                return new Decision
                {
                    PublishMode = publish ? ActiveMode : null,
                    CancelNavGoal = cancel,
                };
            }

            if (dt > 0f) _modeTimer += dt;
            if (InTeleop && _modeTimer >= IdleDelay)
            {
                InTeleop = false;
                _modeTimer = 0f;
                string idle = ResolveIdle(SchemeIdleMode, _deploymentKind);
                return new Decision
                {
                    PublishMode = idle,
                    CancelNavGoal = false,
                    IdleHeldTeleop = idle == null
                        && string.Equals(SchemeIdleMode, "nav2", StringComparison.OrdinalIgnoreCase),
                };
            }
            return default;
        }

        /// <summary>Mode to publish when teleop stops, or null to leave the mode alone.</summary>
        public string Stop()
        {
            bool wasTeleop = InTeleop;
            InTeleop = false;
            _modeTimer = 0f;
            _navEngaged = false;
            if (!wasTeleop) return null;
            return ResolveIdle(SchemeIdleMode, _deploymentKind);
        }
    }

    /// <summary>
    /// Applies one frame of <see cref="ControlModeMachine"/> to the robot link:
    /// stick-during-nav2 cancels the goal and publishes teleop; idle publishes
    /// the resolved mode (nothing for real + nav2).
    /// </summary>
    public static class NavStickHandover
    {
        public static void Apply(
            ControlModeMachine mode,
            NavGoalSession session,
            IRobotLink link,
            string controlModeTopic,
            bool hasInput,
            float dt)
        {
            if (mode == null || session == null) return;
            mode.Observe(session.ModeIsNav2 || session.GoalActive);
            var decision = mode.Tick(hasInput, dt);
            if (decision.CancelNavGoal)
                session.CancelForOverride(link);
            if (!string.IsNullOrEmpty(decision.PublishMode) && link != null && link.IsConnected
                && !string.IsNullOrEmpty(controlModeTopic))
            {
                link.Publish(controlModeTopic, new StringMsg(decision.PublishMode));
                session.NoteModePublished(decision.PublishMode);
            }
            else if (decision.IdleHeldTeleop)
            {
                UnityEngine.Debug.Log("[Nav] Idle on a real robot — not switching to nav2.");
            }
        }
    }
}
