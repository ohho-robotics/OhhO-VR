using Newtonsoft.Json;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// One <c>/ohho/heartbeat</c> pulse. The ROS type is <c>std_msgs/Empty</c>
    /// (teleop_safety). <see cref="seq"/> and <see cref="t_client"/> identify the
    /// pulse for logs and tests and are omitted from the rosbridge payload so the
    /// robot subscription matches.
    /// </summary>
    public sealed class HeartbeatMsg
    {
        /// <summary>1-based count of pulses this session.</summary>
        [JsonIgnore]
        public uint seq;

        /// <summary>Client clock, seconds (Unix time unless a test clock is injected).</summary>
        [JsonIgnore]
        public double t_client;
    }
}
