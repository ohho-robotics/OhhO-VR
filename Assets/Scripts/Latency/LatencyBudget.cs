namespace OmniBot.VR.Latency
{
    /// <summary>
    /// Where a latency budget applies. These are targets for later measurement,
    /// not results from a hardware run.
    /// </summary>
    /// <remarks>
    /// Local: headset and robot on the same LAN, direct ROSBridge, no cloud relay.
    /// Cloud: commands, telemetry, and video cross a cloud relay.
    /// Real: the physical robot (actuators, camera, radio), not a simulator.
    /// </remarks>
    public enum LatencyProfile
    {
        Local = 0,
        Cloud = 1,
        Real = 2
    }

    /// <summary>
    /// One measured interval. Heartbeat age is recorded beside the four
    /// end-to-end segments the budget table is built around.
    /// </summary>
    public enum LatencySegment
    {
        InputToCommand = 0,
        CommandToMotion = 1,
        GlassToGlass = 2,
        TelemetryAge = 3,
        HeartbeatAge = 4
    }

    /// <summary>
    /// p95 ceilings in milliseconds. Indexed by profile, then segment.
    /// The committed copy is <c>docs/latency-budgets.json</c>.
    /// </summary>
    public static class LatencyBudget
    {
        public const int ProfileCount = 3;
        public const int SegmentCount = 5;

        /// <summary>
        /// Optional robot → headset echo. The headset only subscribes while a
        /// capture session is on. It never publishes this topic.
        /// </summary>
        public const string CommandAckTopic = "/latency/command_ack";

        // input→command, command→motion, glass-to-glass, telemetry age, heartbeat age
        private static readonly int[,] P95Ms =
        {
            { 25, 100, 120, 50, 50 },
            { 80, 250, 300, 200, 200 },
            { 40, 200, 180, 80, 80 }
        };

        public static int P95Milliseconds(LatencyProfile profile, LatencySegment segment)
        {
            return P95Ms[(int)profile, (int)segment];
        }

        /// <summary>
        /// Cloud and real input→command count only a remote accept stamp.
        /// A local socket handoff is not that interval.
        /// </summary>
        public static bool RequiresRemoteCommandAck(LatencyProfile profile, LatencySegment segment)
        {
            return segment == LatencySegment.InputToCommand
                && profile != LatencyProfile.Local;
        }

        public static string ProfileId(LatencyProfile profile)
        {
            switch (profile)
            {
                case LatencyProfile.Cloud: return "cloud";
                case LatencyProfile.Real: return "real";
                default: return "local";
            }
        }

        public static string ProfileLabel(LatencyProfile profile)
        {
            switch (profile)
            {
                case LatencyProfile.Cloud: return "Cloud";
                case LatencyProfile.Real: return "Real";
                default: return "Local";
            }
        }

        public static string SegmentId(LatencySegment segment)
        {
            switch (segment)
            {
                case LatencySegment.InputToCommand: return "input_to_command";
                case LatencySegment.CommandToMotion: return "command_to_motion";
                case LatencySegment.GlassToGlass: return "glass_to_glass";
                case LatencySegment.TelemetryAge: return "telemetry_age";
                case LatencySegment.HeartbeatAge: return "heartbeat_age";
                default: return "unknown";
            }
        }

        public static bool TryParseProfile(string text, out LatencyProfile profile)
        {
            profile = LatencyProfile.Local;
            if (string.IsNullOrEmpty(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "local": profile = LatencyProfile.Local; return true;
                case "cloud": profile = LatencyProfile.Cloud; return true;
                case "real": profile = LatencyProfile.Real; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Canonical budgets file. Tests require <c>docs/latency-budgets.json</c>
        /// to match this text after newline normalization.
        /// </summary>
        public static string CanonicalJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"unit\": \"ms\",\n");
            sb.Append("  \"statistic\": \"p95\",\n");
            sb.Append("  \"profiles\": {\n");
            for (int p = 0; p < ProfileCount; p++)
            {
                var profile = (LatencyProfile)p;
                sb.Append("    \"").Append(ProfileId(profile)).Append("\": {\n");
                for (int s = 0; s < SegmentCount; s++)
                {
                    var segment = (LatencySegment)s;
                    sb.Append("      \"").Append(SegmentId(segment)).Append("\": ");
                    sb.Append(P95Ms[p, s]);
                    sb.Append(s == SegmentCount - 1 ? "\n" : ",\n");
                }
                sb.Append("    }");
                sb.Append(p == ProfileCount - 1 ? "\n" : ",\n");
            }
            sb.Append("  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }

    public static class LatencyKind
    {
        public const string Heartbeat = "heartbeat";
        public const string Command = "command";
        public const string Telemetry = "telemetry";
        public const string Video = "video";
    }

    public static class LatencyAck
    {
        public const string LocalSend = "local_send";
        public const string Remote = "remote";
        public const string RemoteUnstamped = "remote_unstamped";
        public const string Superseded = "superseded";
    }

    public static class LatencyVerdict
    {
        public const string Pass = "pass";
        public const string Fail = "fail";
        public const string NotMeasured = "not_measured";
        public const string Incomplete = "incomplete";
    }
}
