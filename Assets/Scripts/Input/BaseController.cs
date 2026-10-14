using UnityEngine;
using OmniBot.VR.Core;

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
    /// Deadman: hold the left grip. Release publishes zero on that tick.
    /// Turbo is disabled on this real-robot path.
    /// Emergency stop: A+B latches. Clearing takes left-controller X (Reset)
    /// plus a deadman release and re-press. Releasing A+B or the grips does not.
    ///
    /// Publishing behaviour:
    ///   - Publishes at CMD_VEL_PUBLISH_HZ while thumbsticks are active.
    ///   - Stops publishing zero-velocity commands after 0.1 s of no input.
    ///   - Sends /control_mode = "teleop" when driving,
    ///             /control_mode = "nav2"   after 0.5 s of no input.
    /// </summary>
    public class BaseController : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const float DeadZone            = 0.1f;
        private const float ZeroStopDelay       = 0.1f;  // s before we stop publishing zeros
        private const float NavModeRevertDelay  = 0.5f;  // s of no input → revert to nav2

        // ── State ────────────────────────────────────────────────────────────
        private bool  _emergencyStopActive  = false;
        private bool  _prevEmergencyChord   = false;
        private bool  _resetAccepted        = false;
        private bool  _deadmanReleasedAfterReset = false;
        private bool  _isPublishing         = false;
        private float _noInputTimer         = 0f;
        private float _noInputModeTimer     = 0f;
        private bool  _inTeleopMode         = false;

        // Publish timer
        private float _publishInterval;
        private float _publishTimer = 0f;

        // ── Unity lifecycle ──────────────────────────────────────────────────
        private void Awake()
        {
            _publishInterval = 1f / RobotConfig.CMD_VEL_PUBLISH_HZ;
        }

        private void Update()
        {
            if (!ROSBridgeClient.Instance.IsConnected) return;

            // ── Deadman is the left grip. A+B latches e-stop. ───────────────
            bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) ||
                            OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.8f;
            bool buttonA = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
            bool buttonB = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
            bool emergencyChord = buttonA && buttonB;

            if (emergencyChord && !_prevEmergencyChord)
            {
                _emergencyStopActive = true;
                _resetAccepted = false;
                _deadmanReleasedAfterReset = false;
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, new BoolMsg(true));
                ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_CMD_VEL, new TwistMsg(
                    new Vector3Msg(0f, 0f, 0f),
                    new Vector3Msg(0f, 0f, 0f)));
                _isPublishing = false;
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.RTouch);
                Debug.Log("[BaseController] Emergency stop LATCHED. Reset, then release and re-press the left grip.");
            }
            else if (_emergencyStopActive && !emergencyChord)
            {
                if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
                    _resetAccepted = true;
                if (_resetAccepted && !leftGrip)
                    _deadmanReleasedAfterReset = true;
                if (_resetAccepted && _deadmanReleasedAfterReset && leftGrip)
                {
                    _emergencyStopActive = false;
                    _resetAccepted = false;
                    _deadmanReleasedAfterReset = false;
                    ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, new BoolMsg(false));
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                    Debug.Log("[BaseController] Emergency stop reset after deadman re-press.");
                }
            }
            _prevEmergencyChord = emergencyChord;

            if (_emergencyStopActive) return;

            if (!leftGrip)
            {
                if (_isPublishing)
                {
                    ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_CMD_VEL, new TwistMsg(
                        new Vector3Msg(0f, 0f, 0f),
                        new Vector3Msg(0f, 0f, 0f)));
                    _isPublishing = false;
                }
                _noInputTimer += Time.deltaTime;
                _noInputModeTimer += Time.deltaTime;
                if (_inTeleopMode && _noInputModeTimer >= NavModeRevertDelay)
                {
                    _inTeleopMode = false;
                    PublishControlMode("nav2");
                }
                return;
            }

            // ── Read thumbsticks ─────────────────────────────────────────────
            Vector2 leftStick  = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick,   OVRInput.Controller.LTouch);
            Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);

            // Apply dead zone
            float lx = ApplyDeadZone(leftStick.x);
            float ly = ApplyDeadZone(leftStick.y);
            float rx = ApplyDeadZone(rightStick.x);

            bool hasInput = Mathf.Abs(lx) > 0f || Mathf.Abs(ly) > 0f || Mathf.Abs(rx) > 0f;

            // Turbo is disabled on this real-robot path. Speed stays inside the
            // OmniBot hard cap (0.2 m/s, 1.0 rad/s) with no multiplier.
            float linCap = RobotConfig.MAX_LINEAR_VEL;
            float angCap = RobotConfig.MAX_ANGULAR_VEL;

            // ── Compute velocity ─────────────────────────────────────────────
            float vx      = Mathf.Clamp(ly, -1f, 1f) * linCap;
            float vy      = Mathf.Clamp(lx, -1f, 1f) * linCap;
            float omega   = Mathf.Clamp(-rx, -1f, 1f) * angCap;

            // ── Control mode management ──────────────────────────────────────
            if (hasInput)
            {
                _noInputTimer     = 0f;
                _noInputModeTimer = 0f;
                if (!_inTeleopMode)
                {
                    _inTeleopMode = true;
                    PublishControlMode("teleop");
                }
            }
            else
            {
                _noInputTimer     += Time.deltaTime;
                _noInputModeTimer += Time.deltaTime;

                if (_inTeleopMode && _noInputModeTimer >= NavModeRevertDelay)
                {
                    _inTeleopMode = false;
                    PublishControlMode("nav2");
                }
            }

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

        /// <summary>Publishes a control_mode string to the robot.</summary>
        private static void PublishControlMode(string mode)
        {
            var msg = new StringMsg(mode);
            ROSBridgeClient.Instance.Publish(RobotConfig.TOPIC_CONTROL_MODE, msg);
            Debug.Log($"[BaseController] control_mode → {mode}");
        }
    }
}
