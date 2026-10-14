namespace OmniBot.VR.Control.Safety
{
    /// <summary>
    /// Client side of <c>teleop_safety</c> (OHH-194). OHH-193 is skipped, so the
    /// heartbeat on the wire is <c>std_msgs/Empty</c> at 10 Hz — that is the type
    /// the watchdog subscribes to. <see cref="HeartbeatMsg"/> still carries
    /// <c>seq</c> and <c>t_client</c> for the session log and tests; those fields
    /// are not part of the ROS message.
    ///
    /// A missing heartbeat for <see cref="WatchdogSec"/> seconds zeros the base
    /// and does not latch. The stop is inside <see cref="StopBudgetSec"/> seconds.
    /// <c>/ohho/safety/state</c> is <c>std_msgs/String</c> JSON
    /// <c>{"armed","latched","reason"}</c>. <c>/ohho/estop_reset</c>
    /// (<c>std_srvs/Trigger</c>) is the only clear for a latched e-stop.
    /// </summary>
    public static class OhhoSafetyContract
    {
        public const string HeartbeatTopic = "/ohho/heartbeat";
        public const string HeartbeatType = "std_msgs/Empty";
        public const float HeartbeatHz = 10f;
        public const double HeartbeatPeriodSec = 0.1;

        /// <summary>teleop_gate.HEARTBEAT_TIMEOUT_S. Do not raise.</summary>
        public const double WatchdogSec = 0.300;

        /// <summary>Watchdog plus one 10 ms gate tick. Do not raise.</summary>
        public const double StopBudgetSec = 0.350;

        public const string SafetyStateTopic = "/ohho/safety/state";
        public const string SafetyStateType = "std_msgs/String";

        public const string EstopResetService = "/ohho/estop_reset";
        public const string EstopResetType = "std_srvs/Trigger";
    }
}
