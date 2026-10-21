using System;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Body-frame velocity in the same shape as
    /// <c>ohho-robotics.com lib/connect/types.ts</c> <c>Velocity</c>
    /// (linearX, linearY, angularZ).
    /// </summary>
    public struct SimVelocity
    {
        public double LinearX;
        public double LinearY;
        public double AngularZ;

        public SimVelocity(double linearX, double linearY, double angularZ)
        {
            LinearX = linearX;
            LinearY = linearY;
            AngularZ = angularZ;
        }

        public static SimVelocity Zero => new SimVelocity(0, 0, 0);
    }

    /// <summary>Planar pose integrated by <see cref="MecanumSimKinematics"/>.</summary>
    public struct SimPose
    {
        public double X;
        public double Y;
        public double Theta;

        public SimPose(double x, double y, double theta)
        {
            X = x;
            Y = y;
            Theta = theta;
        }
    }

    /// <summary>
    /// Result of <c>lib/connect/teleop.ts</c> <c>shouldPublishCmdVel</c>.
    /// A released deadman publishes one zero, then stays quiet.
    /// </summary>
    public readonly struct CmdPublishDecision
    {
        public readonly bool Publish;
        public readonly bool ZeroAlreadySent;

        public CmdPublishDecision(bool publish, bool zeroAlreadySent)
        {
            Publish = publish;
            ZeroAlreadySent = zeroAlreadySent;
        }
    }

    /// <summary>
    /// Hard caps for the in-headset sim. Linear and yaw use
    /// <see cref="RobotConfig"/> so a profile cannot loosen the limits the
    /// rest of the headset already publishes. Joint slew uses the same yaw
    /// cap: the app has no separate joint-speed constant, and the arm must
    /// not outrun it.
    /// </summary>
    public static class SimulatedSafetyCaps
    {
        public static double HardMaxLinearMps => RobotConfig.MAX_LINEAR_VEL;

        public static double HardMaxAngularRadPerSec => RobotConfig.MAX_ANGULAR_VEL;

        /// <summary>Per-joint slew, rad/s. Equal to the base yaw cap.</summary>
        public static double HardMaxJointRateRadPerSec => RobotConfig.MAX_ANGULAR_VEL;

        /// <summary>
        /// A missing profile uses <paramref name="hardMax"/>. A profile value
        /// at or below zero is a real zero cap (fixed base). A larger profile
        /// value is pulled down to the hard cap.
        /// </summary>
        public static double Resolve(float profileMax, float hardMax, bool hasProfile)
        {
            double hard = hardMax;
            if (!hasProfile) return hard;
            if (profileMax <= 0f) return 0;
            return Math.Min((double)profileMax, hard);
        }

        public static double ClampSigned(double value, double limit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (limit < 0 || double.IsNaN(limit)) limit = 0;
            if (value > limit) return limit;
            if (value < -limit) return -limit;
            return value;
        }

        /// <summary>
        /// Per-axis clamp, matching <c>BaseController</c> / <c>MecanumDriveScheme</c>
        /// and <see cref="RobotConfig.MAX_LINEAR_VEL"/> on x and y separately.
        /// </summary>
        public static SimVelocity ClampBase(SimVelocity velocity, double maxLin, double maxAng)
        {
            return new SimVelocity(
                ClampSigned(velocity.LinearX, maxLin),
                ClampSigned(velocity.LinearY, maxLin),
                ClampSigned(velocity.AngularZ, maxAng));
        }
    }

    /// <summary>
    /// Deadman gate copied from <c>lib/connect/teleop.ts</c>
    /// <c>applyTeleopCommand</c> / <c>shouldPublishCmdVel</c>.
    /// Velocity is forwarded only while the deadman is held and e-stop is clear.
    /// This function does not apply speed caps; <see cref="SimulatedRobotLink"/> does.
    /// </summary>
    public static class SimulatedTeleopGate
    {
        public static SimVelocity Apply(SimVelocity requested, bool deadmanHeld, bool estopLatched)
        {
            if (estopLatched || !deadmanHeld) return SimVelocity.Zero;
            return new SimVelocity(
                Finite(requested.LinearX),
                Finite(requested.LinearY),
                Finite(requested.AngularZ));
        }

        public static CmdPublishDecision ShouldPublish(bool deadmanHeld, bool estopLatched, bool zeroAlreadySent)
        {
            bool live = deadmanHeld && !estopLatched;
            if (live) return new CmdPublishDecision(true, false);
            if (zeroAlreadySent) return new CmdPublishDecision(false, true);
            return new CmdPublishDecision(true, true);
        }

        static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return value;
        }
    }

    /// <summary>
    /// Holonomic mecanum step copied from <c>lib/connect/simulated.ts</c>
    /// <c>_tick</c>: yaw integrates first, then body vx/vy rotate into the world.
    /// The browser timer is 100 ms (<see cref="SimulatedVectors.TickSeconds"/>).
    /// </summary>
    public static class MecanumSimKinematics
    {
        public static SimPose Integrate(SimPose pose, SimVelocity body, double dt)
        {
            if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return pose;

            double theta = pose.Theta + body.AngularZ * dt;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            double x = pose.X + (body.LinearX * c - body.LinearY * s) * dt;
            double y = pose.Y + (body.LinearX * s + body.LinearY * c) * dt;
            return new SimPose(x, y, theta);
        }
    }

    /// <summary>
    /// Numeric vectors shared with the website sim.
    /// <list type="bullet">
    /// <item><c>lib/connect/teleop.ts</c> KEY_LIN = 0.2, KEY_ANG = 0.8.</item>
    /// <item><c>lib/connect/teleop.test.ts</c> deadman sample
    /// <c>{ linearX: 0.2, linearY: 0.05, angularZ: -0.4 }</c>.</item>
    /// <item><c>lib/connect/simulated.ts</c> telemetry battery 0.87, connected
    /// latency 2 ms, msgRate 20, and the 0.1 s holonomic step.</item>
    /// </list>
    /// Pose rows are that step evaluated from the origin (yaw first).
    /// </summary>
    public static class SimulatedVectors
    {
        public const double TickSeconds = 0.1;
        public const double KeyLinearMps = 0.2;
        public const double KeyAngularRadPerSec = 0.8;
        public const double Battery = 0.87;
        public const int ConnectedLatencyMs = 2;
        public const int ConnectedMsgRateHz = 20;

        public static readonly SimVelocity DeadmanSample = new SimVelocity(0.2, 0.05, -0.4);

        public static readonly SimVelocity Forward = new SimVelocity(KeyLinearMps, 0, 0);

        public static readonly SimPose[] ForwardPoses =
        {
            new SimPose(0.02, 0, 0),
            new SimPose(0.04, 0, 0),
            new SimPose(0.06, 0, 0),
        };

        public static readonly SimPose[] DeadmanSamplePoses =
        {
            new SimPose(0.020183948804, 0.004196213850, -0.04),
            new SimPose(0.040519556400, 0.007581928502, -0.08),
            new SimPose(0.060974290154, 0.010151727535, -0.12),
        };

        /// <summary>One step of body vy = 0.2 at yaw = π/2. World x becomes -0.02.</summary>
        public static readonly SimPose StrafeAtHalfPi = new SimPose(-0.02, 0, 1.5707963267948966);

        /// <summary>One step of vx = 0.2, yaw rate = 1 rad/s, dt = 0.1.</summary>
        public static readonly SimPose TurnStep = new SimPose(0.019900083306, 0.001996668333, 0.1);
    }
}
