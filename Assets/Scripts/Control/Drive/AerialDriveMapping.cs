using System;
using Newtonsoft.Json;

namespace OmniBot.VR.Control.Drive
{
    /// <summary>
    /// What an Ohho e-stop does for this aircraft. X500 / PX4 lands.
    /// A Crazyflie profile may cut motors. The client never emits motor-kill,
    /// force-disarm, or flight termination; sim-serve <c>estop: true</c> is the
    /// Ohho e-stop, and the server applies this effect.
    /// </summary>
    public enum AerialEstopEffect
    {
        None = 0,
        Land = 1,
        MotorCut = 2,
    }

    /// <summary>One confirmed flight button. Absent on ordinary stick messages.</summary>
    public sealed class AerialFlightRequest
    {
        public string Request;
        public double? AltM;
        public string Confirm;
    }

    /// <summary>Result of a takeoff or land button. Refused results carry no request.</summary>
    public sealed class AerialButtonResult
    {
        public bool Accepted;
        public string Reason;
        public AerialFlightRequest Flight;
    }

    /// <summary>
    /// Mode-2 stick state plus the robot the message is for. Sticks are raw,
    /// before the dead zone. Speeds are the catalog caps on the profile.
    /// </summary>
    public struct AerialMappingInput
    {
        public string RobotId;
        public string RobotName;
        public float MaxLinVel;
        public float MaxAngVel;
        public float LeftX;
        public float LeftY;
        public float RightX;
        public float RightY;
        public bool LeftGrip;
        public bool RightGrip;
        public AerialFlightRequest PendingFlight;
    }

    /// <summary>Body-frame velocity after the dead zone. Yaw is nose-right positive.</summary>
    public struct AerialAxes
    {
        public float Vx;
        public float Vy;
        public float Vz;
        public float YawRate;
    }

    /// <summary>
    /// One sim-serve aircraft command. <see cref="Flight"/> is set only by a
    /// confirmed button, never by a stick.
    /// </summary>
    public sealed class AerialTeleopCommand
    {
        public float Vx;
        public float Vy;
        public float Vz;
        public float YawRate;
        public bool Deadman;
        public bool Estop;
        public AerialEstopEffect EstopEffect;
        public AerialFlightRequest Flight;
    }

    /// <summary>
    /// Mode-2 aerial mapping. Left Y up climbs (BODY_FRD vz negative). Left X
    /// right is a positive yaw rate (nose right), matching a mode-2 radio.
    /// Right Y is forward, right X is right. Dead zone is 0.1. There is no
    /// turbo. No grip publishes zeros and <c>deadman: false</c>. Both grips
    /// are the Ohho e-stop.
    /// </summary>
    public static class AerialDriveMapping
    {
        public const float DeadZone = 0.1f;
        public const string FrameBodyFrd = "BODY_FRD";

        public static float ApplyDeadZone(float value)
        {
            float abs = Math.Abs(value);
            if (abs < DeadZone) return 0f;
            float sign = value < 0f ? -1f : 1f;
            float scaled = (abs - DeadZone) / (1f - DeadZone);
            if (scaled > 1f) scaled = 1f;
            if (scaled < 0f) scaled = 0f;
            return sign * scaled;
        }

        /// <summary>
        /// Map raw sticks to BODY_FRD. Does not read grips and does not scale
        /// by a turbo multiplier.
        /// </summary>
        public static AerialAxes MapAxes(
            float leftX, float leftY, float rightX, float rightY, float maxLin, float maxAng)
        {
            return ScaleDeadzoned(
                ApplyDeadZone(leftX),
                ApplyDeadZone(leftY),
                ApplyDeadZone(rightX),
                ApplyDeadZone(rightY),
                maxLin,
                maxAng);
        }

        /// <summary>Sign and scale sticks that already had the dead zone applied.</summary>
        public static AerialAxes ScaleDeadzoned(
            float leftX, float leftY, float rightX, float rightY, float maxLin, float maxAng)
        {
            leftX = ClampUnit(leftX);
            leftY = ClampUnit(leftY);
            rightX = ClampUnit(rightX);
            rightY = ClampUnit(rightY);
            return new AerialAxes
            {
                Vx = PosZero(rightY * maxLin),
                Vy = PosZero(rightX * maxLin),
                Vz = PosZero(-leftY * maxLin),
                YawRate = PosZero(leftX * maxAng),
            };
        }

        static float PosZero(float value)
        {
            return value == 0f ? 0f : value;
        }

        /// <summary>
        /// Full teleop step. A held grip is the deadman. Both grips are e-stop
        /// and clear velocity. A pending flight request is attached only when
        /// the e-stop is not held.
        /// </summary>
        public static AerialTeleopCommand Map(AerialMappingInput input)
        {
            var cmd = new AerialTeleopCommand();
            bool both = input.LeftGrip && input.RightGrip;
            bool grip = input.LeftGrip || input.RightGrip;

            if (both)
            {
                cmd.Estop = true;
                cmd.Deadman = false;
                cmd.EstopEffect = AerialEstopProfile.EffectFor(input.RobotId, input.RobotName);
                return cmd;
            }

            cmd.Estop = false;
            cmd.EstopEffect = AerialEstopEffect.None;
            cmd.Flight = input.PendingFlight;
            if (!grip) return cmd;

            cmd.Deadman = true;
            AerialAxes axes = MapAxes(
                input.LeftX, input.LeftY, input.RightX, input.RightY,
                input.MaxLinVel, input.MaxAngVel);
            cmd.Vx = axes.Vx;
            cmd.Vy = axes.Vy;
            cmd.Vz = axes.Vz;
            cmd.YawRate = axes.YawRate;
            return cmd;
        }

        static float ClampUnit(float value)
        {
            if (value > 1f) return 1f;
            if (value < -1f) return -1f;
            return value;
        }
    }

    /// <summary>X500 / PX4 e-stop lands. Crazyflie e-stop may cut motors.</summary>
    public static class AerialEstopProfile
    {
        public static bool IsCrazyflie(string robotId, string robotName = null)
        {
            return MentionsCrazyflie(robotId) || MentionsCrazyflie(robotName);
        }

        public static AerialEstopEffect EffectFor(string robotId, string robotName = null)
        {
            if (IsCrazyflie(robotId, robotName)) return AerialEstopEffect.MotorCut;
            return AerialEstopEffect.Land;
        }

        static bool MentionsCrazyflie(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string id = value.Trim().ToLowerInvariant().Replace('_', '-');
            return id.Contains("crazyflie") || id.Contains("crazyswarm") || id.Contains("cf21");
        }
    }

    /// <summary>
    /// Takeoff and land buttons. Both require the SafetyGate arm phrase.
    /// Stick deflection never reaches this type.
    /// </summary>
    public static class AerialFlightConfirmation
    {
        public const string Phrase =
            "I am physically present, the area is clear, the remote is in my hand";

        public const double DefaultTakeoffAltitudeM = 2.5;
        public const double TeleopTakeoffCeilingM = 3.0;

        public const string ReasonNotConfirmed = "not_confirmed";
        public const string ReasonBadAltitude = "bad_altitude";
        public const string ReasonAltitude = "altitude";
        public const string ReasonEstop = "estop";

        public static bool Matches(string typed)
        {
            return Normalize(typed) == Normalize(Phrase);
        }

        /// <summary>Same fold as sim-serve <c>normalize_phrase</c>: whitespace, case, trailing period.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string[] parts = value.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string joined = string.Join(" ", parts).ToLowerInvariant();
            while (joined.EndsWith(".", StringComparison.Ordinal))
                joined = joined.Substring(0, joined.Length - 1);
            return joined;
        }

        public static AerialButtonResult Takeoff(string confirmation, double altM = DefaultTakeoffAltitudeM)
        {
            if (!Matches(confirmation)) return Refuse(ReasonNotConfirmed);
            if (double.IsNaN(altM) || double.IsInfinity(altM) || altM <= 0.0)
                return Refuse(ReasonBadAltitude);
            if (altM > TeleopTakeoffCeilingM) return Refuse(ReasonAltitude);
            return Accept(new AerialFlightRequest
            {
                Request = "takeoff",
                AltM = altM,
                Confirm = Phrase,
            });
        }

        public static AerialButtonResult Land(string confirmation)
        {
            if (!Matches(confirmation)) return Refuse(ReasonNotConfirmed);
            return Accept(new AerialFlightRequest
            {
                Request = "land",
                Confirm = Phrase,
            });
        }

        static AerialButtonResult Accept(AerialFlightRequest flight)
        {
            return new AerialButtonResult { Accepted = true, Reason = "", Flight = flight };
        }

        static AerialButtonResult Refuse(string reason)
        {
            return new AerialButtonResult { Accepted = false, Reason = reason, Flight = null };
        }
    }

    /// <summary>D-05 sim-serve JSON. Field names match <c>ohho/sim_serve.py</c>.</summary>
    public static class SimServeAerialJson
    {
        public static string Serialize(AerialTeleopCommand cmd)
        {
            if (cmd == null) cmd = new AerialTeleopCommand();
            var wire = new WireMessage
            {
                velocity = new WireVelocity
                {
                    linear = new WireVector { x = cmd.Vx, y = cmd.Vy, z = cmd.Vz },
                    angular = new WireYaw { z = cmd.YawRate },
                },
                frame = AerialDriveMapping.FrameBodyFrd,
                deadman = cmd.Deadman,
                estop = cmd.Estop,
            };
            if (cmd.Flight != null)
            {
                wire.flight = new WireFlight
                {
                    request = cmd.Flight.Request,
                    alt_m = cmd.Flight.AltM,
                    confirm = cmd.Flight.Confirm,
                };
            }
            return JsonConvert.SerializeObject(wire);
        }

        sealed class WireMessage
        {
            [JsonProperty("velocity")] public WireVelocity velocity;
            [JsonProperty("frame")] public string frame;
            [JsonProperty("deadman")] public bool deadman;
            [JsonProperty("estop")] public bool estop;
            [JsonProperty("flight", NullValueHandling = NullValueHandling.Ignore)]
            public WireFlight flight;
        }

        sealed class WireVelocity
        {
            [JsonProperty("linear")] public WireVector linear;
            [JsonProperty("angular")] public WireYaw angular;
        }

        sealed class WireVector
        {
            [JsonProperty("x")] public float x;
            [JsonProperty("y")] public float y;
            [JsonProperty("z")] public float z;
        }

        sealed class WireYaw
        {
            [JsonProperty("z")] public float z;
        }

        sealed class WireFlight
        {
            [JsonProperty("request")] public string request;
            [JsonProperty("alt_m", NullValueHandling = NullValueHandling.Ignore)]
            public double? alt_m;
            [JsonProperty("confirm", NullValueHandling = NullValueHandling.Ignore)]
            public string confirm;
        }
    }
}
