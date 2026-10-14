using System;
using OmniBot.VR.Core.Platform;

namespace OmniBot.VR.Nav
{
    /// <summary>
    /// Whether the connected robot is hardware or a simulator.
    /// Idle teleop must not publish <c>/control_mode</c> <c>nav2</c> when this is
    /// <see cref="Real"/> — that resume is explicit (mode button or a new goal).
    /// </summary>
    public static class RobotDeployment
    {
        public const string Real = "real";
        public const string Sim = "sim";

        public static bool IsReal(string kind) => Normalize(kind) != Sim;

        /// <summary>
        /// <c>sim</c>, <c>simulated</c>, and <c>gazebo</c> are simulators.
        /// Everything else, including a missing value, is <see cref="Real"/>.
        /// </summary>
        public static string Normalize(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return Real;
            switch (kind.Trim().ToLowerInvariant())
            {
                case "sim":
                case "simulated":
                case "gazebo":
                    return Sim;
                default:
                    return Real;
            }
        }

        /// <summary>
        /// Garage <c>config.kind</c> wins when it is set. Otherwise a
        /// <c>simulated</c> status is sim, and every other status is real.
        /// </summary>
        public static string FromGarage(string status, string configKind)
        {
            if (!string.IsNullOrWhiteSpace(configKind))
                return Normalize(configKind);
            if (string.Equals(status, "simulated", StringComparison.OrdinalIgnoreCase))
                return Sim;
            return Real;
        }

        public static string FromUserRobot(UserRobot robot)
        {
            string kind = null;
            if (robot?.Config != null && robot.Config.TryGetValue("kind", out var raw) && raw != null)
                kind = raw.ToString();
            return FromGarage(robot?.Status, kind);
        }
    }
}
