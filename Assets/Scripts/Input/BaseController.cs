using UnityEngine;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Nav;

namespace OmniBot.VR.Input
{
    /// <summary>
    /// Maps VR controller thumbstick input to OmniBot base velocity commands.
    ///
    /// Axes:
    ///   Left  thumbstick Y  → linear.x  (forward / back)
    ///   Left  thumbstick X  → linear.y  (strafe left / right)
    ///   Right thumbstick X  → angular.z (rotate)
    ///
    /// Turbo mode: hold right grip → 2× speed (still clamped to MAX_*).
    /// Emergency stop: both grip buttons simultaneously.
    ///
    /// Publishing behaviour:
    ///   - Publishes at CMD_VEL_PUBLISH_HZ while thumbsticks are active.
    ///   - Stops publishing zero-velocity commands after 0.1 s of no input.
    ///   - Sends /control_mode = "teleop" when driving.
    ///   - Does not return to nav2 on idle. This legacy controller has no
    ///     simulator flag, so it follows kind=real: Nav2 is the mode button
    ///     or a goal. A stick while Nav2 is active cancels the goal.
    /// </summary>
    public class BaseController : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const float DeadZone            = 0.1f;
        private const float TurboMultiplier     = 2.0f;
        private const float ZeroStopDelay       = 0.1f;  // s before we stop publishing zeros
        // ── State ────────────────────────────────────────────────────────────
        private bool  _emergencyStopActive  = false;
        private bool  _prevBothGrips        = false;
        private bool  _isPublishing         = false;
        private float _noInputTimer         = 0f;
        private readonly ControlModeMachine _mode = new ControlModeMachine();

        // Publish timer
        private float _publishInterval;
        private float _publishTimer = 0f;

        // ── Unity lifecycle ──────────────────────────────────────────────────
        private void Awake()
        {
            _publishInterval = 1f / RobotConfig.CMD_VEL_PUBLISH_HZ;
            _mode.Reset("nav2", RobotDeployment.Real);
        }

        private void Update()
        {
            if (!ROSBridgeClient.Instance.IsConnected) return;

            // ── Emergency stop: both grip buttons ────────────────────────────
            bool leftGrip  = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger,  OVRInput.Controller.LTouch);
            bool rightGrip = OVRInput.Get(OVRInput.Button.SecondaryHandTrigger, OVRInput.Controller.RTouch);

            // Also accept grip axes for analog grip buttons
            leftGrip  = leftGrip  || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger,   OVRInput.Controller.LTouch) > 0.8f;
            rightGrip = rightGrip || OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger,  OVRInput.Controller.RTouch) > 0.8f;

            bool bothGrips = leftGrip && rightGrip;

            if (bothGrips && !_prevBothGrips)
            {
                // Rising edge: activate emergency stop
                _emergencyStopActive = true;
                var stopMsg = new BoolMsg(true);
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, stopMsg);
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.RTouch);
                Debug.Log("[BaseController] Emergency stop ACTIVATED.");
            }
            else if (!bothGrips && _prevBothGrips && _emergencyStopActive)
            {
                // Falling edge: release emergency stop
                _emergencyStopActive = false;
                var stopMsg = new BoolMsg(false);
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, stopMsg);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                Debug.Log("[BaseController] Emergency stop RELEASED.");
            }
            _prevBothGrips = bothGrips;

            if (_emergencyStopActive) return;

            // ── Read thumbsticks ─────────────────────────────────────────────
            Vector2 leftStick  = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick,   OVRInput.Controller.LTouch);
            Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);

            // Apply dead zone
            float lx = ApplyDeadZone(leftStick.x);
            float ly = ApplyDeadZone(leftStick.y);
            float rx = ApplyDeadZone(rightStick.x);

            bool hasInput = Mathf.Abs(lx) > 0f || Mathf.Abs(ly) > 0f || Mathf.Abs(rx) > 0f;

            // ── Turbo mode: right grip held (but NOT both grips) ─────────────
            bool turbo = rightGrip && !leftGrip;
            float speedMult = turbo ? TurboMultiplier : 1.0f;

            // ── Compute velocity ─────────────────────────────────────────────
            float vx      = Mathf.Clamp(ly * speedMult, -1f, 1f) * RobotConfig.MAX_LINEAR_VEL;
            float vy      = Mathf.Clamp(lx * speedMult, -1f, 1f) * RobotConfig.MAX_LINEAR_VEL;
            float omega   = Mathf.Clamp(-rx * speedMult, -1f, 1f) * RobotConfig.MAX_ANGULAR_VEL;

            // ── Control mode: no idle return to nav2 (explicit button / goal) ─
            if (hasInput) _noInputTimer = 0f;
            else _noInputTimer += Time.deltaTime;
            _mode.DeploymentKind = NavGoalSession.Active.DeploymentKind;
            _mode.SchemeIdleMode = "nav2";
            NavStickHandover.Apply(
                _mode,
                NavGoalSession.Active,
                RosBridgeLink.Instance,
                RobotConfig.TOPIC_CONTROL_MODE,
                hasInput,
                Time.deltaTime);

            // ── Publish timer ────────────────────────────────────────────────
            _publishTimer += Time.deltaTime;
            if (_publishTimer < _publishInterval) return;
            _publishTimer -= _publishInterval;

            // Publish cmd_vel: always while there's input; stop after ZeroStopDelay of no input
            bool shouldPublish = hasInput || _noInputTimer < ZeroStopDelay;
            if (shouldPublish)
            {
                var twist = new TwistMsg(
                    new Vector3Msg(vx, vy, 0f),
                    new Vector3Msg(0f, 0f, omega));
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_CMD_VEL, twist);
                _isPublishing = true;
            }
            else if (_isPublishing)
            {
                // Send one final zero to stop the robot
                var zeroTwist = new TwistMsg(
                    new Vector3Msg(0f, 0f, 0f),
                    new Vector3Msg(0f, 0f, 0f));
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_CMD_VEL, zeroTwist);
                _isPublishing = false;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>Applies a symmetric dead zone to a 1D axis value.</summary>
        private static float ApplyDeadZone(float value)
        {
            if (Mathf.Abs(value) < DeadZone) return 0f;
            // Rescale so output is 0..1 outside dead zone
            float sign  = Mathf.Sign(value);
            float scaled = (Mathf.Abs(value) - DeadZone) / (1f - DeadZone);
            return sign * Mathf.Clamp01(scaled);
        }

    }
}
