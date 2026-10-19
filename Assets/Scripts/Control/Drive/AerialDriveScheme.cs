using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control.Drive
{
    /// <summary>
    /// Quadrotor / hexacopter / VTOL teleop. Mode 2: left Y climbs, left X
    /// yaws nose-right, right stick translates. A grip is the deadman. Both
    /// grips are the Ohho e-stop (land on X500, motor-cut allowed on
    /// Crazyflie). Takeoff and land are <see cref="TryTakeoff"/> /
    /// <see cref="TryLand"/>, which require the confirmation phrase. There is
    /// no stick gesture and no turbo.
    ///
    /// Commands go to ohho sim-serve as the D-05 JSON message, not a ROS twist.
    /// Catalog speed caps on the profile are left as they are; the Python
    /// SafetyGate is the cap that matters.
    /// </summary>
    public sealed class AerialDriveScheme : DriveSchemeBase
    {
        public override string HudHint =>
            "Left Y: climb · Left X: yaw nose-right · Right stick: forward/right · Grip: deadman · Both grips: e-stop · Takeoff/Land: buttons";

        protected override string IdleMode => "offboard";

        ISimServeSender _sender;
        AerialFlightRequest _pending;
        bool _prevDeadman;
        bool _prevEstop;
        bool _estopAnnounced;
        float _timer;

        public AerialTeleopCommand LastCommand { get; private set; }
        public string LastRefusal { get; private set; }

        public void UseSender(ISimServeSender sender)
        {
            _sender = sender;
        }

        public override void Configure(RobotProfile profile)
        {
            base.Configure(profile);
            _pending = null;
            LastCommand = null;
            LastRefusal = null;
            _prevDeadman = false;
            _prevEstop = false;
            _estopAnnounced = false;
            _timer = 0f;
        }

        /// <summary>
        /// Queue a takeoff. Returns false until the confirmation phrase matches.
        /// A stick position cannot call this.
        /// </summary>
        public bool TryTakeoff(string confirmation, double altM = AerialFlightConfirmation.DefaultTakeoffAltitudeM)
        {
            if (LastCommand != null && LastCommand.Estop)
            {
                LastRefusal = AerialFlightConfirmation.ReasonEstop;
                return false;
            }
            AerialButtonResult result = AerialFlightConfirmation.Takeoff(confirmation, altM);
            if (!result.Accepted)
            {
                LastRefusal = result.Reason;
                return false;
            }
            _pending = result.Flight;
            LastRefusal = null;
            return true;
        }

        /// <summary>Queue a land. Returns false until the confirmation phrase matches.</summary>
        public bool TryLand(string confirmation)
        {
            if (LastCommand != null && LastCommand.Estop)
            {
                LastRefusal = AerialFlightConfirmation.ReasonEstop;
                return false;
            }
            AerialButtonResult result = AerialFlightConfirmation.Land(confirmation);
            if (!result.Accepted)
            {
                LastRefusal = result.Reason;
                return false;
            }
            _pending = result.Flight;
            LastRefusal = null;
            return true;
        }

        public override void Tick(ref TeleopInput input, IRobotLink link)
        {
            if (!IsConfigured || Profile == null) return;

            var mapped = new AerialMappingInput
            {
                RobotId = Profile.RobotId,
                RobotName = Profile.Name,
                MaxLinVel = Profile.MaxLinVel,
                MaxAngVel = Profile.MaxAngVel,
                LeftX = input.LeftStick.x,
                LeftY = input.LeftStick.y,
                RightX = input.RightStick.x,
                RightY = input.RightStick.y,
                LeftGrip = input.LeftGrip,
                RightGrip = input.RightGrip,
                PendingFlight = _pending,
            };
            AerialTeleopCommand cmd = AerialDriveMapping.Map(mapped);
            if (cmd.Estop)
                _pending = null;
            LastCommand = cmd;

            if (cmd.Estop && !_estopAnnounced)
            {
                string effect = cmd.EstopEffect == AerialEstopEffect.MotorCut ? "motor-cut allowed" : "land";
                Debug.Log("[AerialDriveScheme] E-stop (" + effect + ").");
            }
            _estopAnnounced = cmd.Estop;

            _timer += input.DeltaTime;
            bool due = _timer >= PublishInterval;
            if (due)
            {
                _timer -= PublishInterval;
                if (_timer > PublishInterval) _timer = 0f;
            }
            bool changed = cmd.Estop != _prevEstop || cmd.Deadman != _prevDeadman || cmd.Flight != null;
            if ((due || changed) && _sender != null && _sender.IsConnected)
            {
                _sender.Send(SimServeAerialJson.Serialize(cmd));
                _prevEstop = cmd.Estop;
                _prevDeadman = cmd.Deadman;
                if (cmd.Flight != null) _pending = null;
            }
        }

        public override void Stop(IRobotLink link)
        {
            _pending = null;
            var released = new AerialMappingInput
            {
                RobotId = Profile != null ? Profile.RobotId : null,
                RobotName = Profile != null ? Profile.Name : null,
                MaxLinVel = Profile != null ? Profile.MaxLinVel : 0f,
                MaxAngVel = Profile != null ? Profile.MaxAngVel : 0f,
            };
            AerialTeleopCommand cmd = AerialDriveMapping.Map(released);
            LastCommand = cmd;
            _prevDeadman = false;
            _prevEstop = false;
            _estopAnnounced = false;
            _timer = 0f;
            if (_sender != null && _sender.IsConnected)
                _sender.Send(SimServeAerialJson.Serialize(cmd));
        }

        protected override TwistMsg ComputeVelocity(
            float lx, float ly, float rx, float ry, float speedMult, out bool hasInput)
        {
            // Base.Tick dead-zones first and would apply turbo. Aerial Tick does
            // not call it. This override keeps the abstract contract on the same
            // signs, still with no turbo.
            _ = speedMult;
            AerialAxes axes = AerialDriveMapping.ScaleDeadzoned(
                lx, ly, rx, ry, Profile.MaxLinVel, Profile.MaxAngVel);
            hasInput = axes.Vx != 0f || axes.Vy != 0f || axes.Vz != 0f || axes.YawRate != 0f;
            return new TwistMsg(
                new Vector3Msg(axes.Vx, axes.Vy, axes.Vz),
                new Vector3Msg(0f, 0f, axes.YawRate));
        }
    }
}
