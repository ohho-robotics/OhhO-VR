namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// Wire names for the published ohho.telemetry.v1 HUD contract.
    /// Display only: nothing here arms, clears, or bypasses a safety gate.
    /// </summary>
    public static class TelemetryTopics
    {
        public const string Telemetry = "/ohho/telemetry";
        public const string SafetyState = "/ohho/safety/state";
        public const string HeartbeatEcho = "/ohho/heartbeat_echo";
        public const string StringType = "std_msgs/String";

        /// <summary>Aggregator publish rate. The HUD does not command at this rate.</summary>
        public const float TelemetryHz = 20f;

        /// <summary>Local command stream the headset already publishes.</summary>
        public const float EchoHz = 20f;

        /// <summary>Panel refresh. Quest frames stay free of text rebuilds between ticks.</summary>
        public const float UiHz = 10f;

        public const float UiPeriodSeconds = 0.1f;

        /// <summary>No sample inside this window means the aggregate topic is absent.</summary>
        public const double FreshSeconds = 1.0;
    }
}
