using System;

namespace OmniBot.VR.Latency
{
    /// <summary>
    /// Process-wide capture switch. When no session is active every method is a
    /// no-op, so teleop behaviour is unchanged unless <c>OHHO_LATENCY_LOG</c> is set
    /// or a caller starts a session.
    /// </summary>
    public static class LatencyCapture
    {
        public static LatencySession Active { get; private set; }
        public static bool IsEnabled => Active != null;

        public static void Start(LatencyProfile profile, ILatencyClock clock)
        {
            Active = new LatencySession(profile, clock ?? new SystemLatencyClock());
        }

        public static void Start(LatencyProfile profile)
        {
            Start(profile, new SystemLatencyClock());
        }

        /// <summary>
        /// Starts a session when <c>OHHO_LATENCY_LOG</c> is a directory path.
        /// <c>OHHO_LATENCY_PROFILE</c> is local, cloud, or real (default local).
        /// <c>OHHO_LATENCY_HARDWARE=1</c> marks the log as a human hardware run.
        /// An unset log path leaves capture off.
        /// </summary>
        public static bool TryStartFromEnvironment()
        {
            string dir = Environment.GetEnvironmentVariable("OHHO_LATENCY_LOG");
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string profileText = Environment.GetEnvironmentVariable("OHHO_LATENCY_PROFILE");
            LatencyProfile profile = LatencyProfile.Local;
            if (!string.IsNullOrWhiteSpace(profileText)
                && !LatencyBudget.TryParseProfile(profileText, out profile))
                return false;
            Start(profile, new SystemLatencyClock());
            Active.LogDirectory = dir.Trim();
            string hardware = Environment.GetEnvironmentVariable("OHHO_LATENCY_HARDWARE");
            Active.HardwareRun = hardware == "1" || string.Equals(hardware, "true", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public static void MarkInputSample()
        {
            if (Active == null) return;
            try { Active.MarkInput(); }
            catch (Exception) { /* recording must not affect teleop */ }
        }

        public static void ClearInputSample()
        {
            if (Active == null) return;
            try { Active.ClearInput(); }
            catch (Exception) { /* recording must not affect teleop */ }
        }

        public static void ObserveOutgoing(string topic, string json)
        {
            if (Active == null) return;
            try { Active.ObserveOutgoing(topic, json); }
            catch (Exception) { /* recording must not affect teleop */ }
        }

        public static void ObserveIncoming(string topic, string json)
        {
            if (Active == null) return;
            try { Active.ObserveIncoming(topic, json); }
            catch (Exception) { /* recording must not affect teleop */ }
        }

        public static void NoteVideoFrame(long? captureUnixMs, long displayedUnixMs, string transport)
        {
            if (Active == null) return;
            try { Active.NoteVideo(captureUnixMs, displayedUnixMs, transport); }
            catch (Exception) { /* recording must not affect teleop */ }
        }

        public static void StopAndWrite()
        {
            LatencySession session = Active;
            Active = null;
            if (session == null) return;
            if (string.IsNullOrEmpty(session.LogDirectory)) return;
            try
            {
                LatencyLog.Write(session.LogDirectory, session.BuildReport());
            }
            catch (Exception)
            {
                // A failed log write must not change shutdown of teleop.
            }
        }

        /// <summary>Drops the session without writing. Used by tests.</summary>
        public static void ResetForTests()
        {
            Active = null;
        }
    }
}
