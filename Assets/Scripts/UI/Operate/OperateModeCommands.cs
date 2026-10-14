using OmniBot.VR.Control;

namespace OmniBot.VR.UI.Operate
{
    public enum OperateDriveMode
    {
        Teleop,
        Nav2,
        Policy,
    }

    public enum OperateDeadman
    {
        Open,
        Held,
        Stop,
    }

    public enum OperateRealm
    {
        Sim,
        Real,
    }

    /// <summary>
    /// Mode-bar decisions with no scene dependencies. Policy does not apply
    /// until confirmed. Reset does not run while a grip stop is held.
    /// </summary>
    public sealed class OperateModeCommands
    {
        public OperateDriveMode Mode { get; private set; } = OperateDriveMode.Teleop;
        public bool PolicyConfirmationPending { get; private set; }
        public bool EStopLatched { get; private set; }
        public bool Recording { get; private set; }

        public string ModeWireValue
        {
            get
            {
                switch (Mode)
                {
                    case OperateDriveMode.Nav2: return "nav2";
                    case OperateDriveMode.Policy: return "policy";
                    default: return "teleop";
                }
            }
        }

        /// <summary>
        /// Returns true when <paramref name="requested"/> is now the active mode.
        /// Policy returns false and waits for <see cref="ConfirmPolicy"/>.
        /// </summary>
        public bool Request(OperateDriveMode requested)
        {
            if (requested == OperateDriveMode.Policy && Mode != OperateDriveMode.Policy)
            {
                PolicyConfirmationPending = true;
                return false;
            }

            PolicyConfirmationPending = false;
            Mode = requested;
            return true;
        }

        public bool ConfirmPolicy()
        {
            if (!PolicyConfirmationPending) return false;
            PolicyConfirmationPending = false;
            Mode = OperateDriveMode.Policy;
            return true;
        }

        public void CancelPolicy() => PolicyConfirmationPending = false;

        public void LatchEStop() => EStopLatched = true;

        /// <summary>Clears the UI latch. Refuses while both grips are held.</summary>
        public bool TryReset(bool gripHeld)
        {
            if (!OperateSafetyRules.ResetAllowed(gripHeld)) return false;
            EStopLatched = false;
            return true;
        }

        public void SetRecording(bool recording) => Recording = recording;
    }

    public static class OperateReadout
    {
        public static OperateDeadman Deadman(bool anyGrip, bool bothGrips)
        {
            if (bothGrips) return OperateDeadman.Stop;
            if (anyGrip) return OperateDeadman.Held;
            return OperateDeadman.Open;
        }

        public static string DeadmanLabel(OperateDeadman state)
        {
            switch (state)
            {
                case OperateDeadman.Stop: return "DEADMAN STOP";
                case OperateDeadman.Held: return "DEADMAN HELD";
                default: return "DEADMAN OPEN";
            }
        }
    }

    /// <summary>
    /// SIM when the session says so, or when the host is loopback.
    /// Anything unresolved is REAL so the bar does not call a live robot a sim.
    /// </summary>
    public static class OperateRealmRules
    {
        public static OperateRealm Resolve(string label, string host)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                string s = label.Trim();
                if (IsSimLabel(s)) return OperateRealm.Sim;
                if (IsRealLabel(s)) return OperateRealm.Real;
            }

            if (IsLoopback(host)) return OperateRealm.Sim;
            return OperateRealm.Real;
        }

        public static bool IsLoopback(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            switch (host.Trim().ToLowerInvariant())
            {
                case "localhost":
                case "127.0.0.1":
                case "::1":
                case "0.0.0.0":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSimLabel(string s)
        {
            s = s.ToLowerInvariant();
            return s == "sim" || s == "simulate" || s == "simulation";
        }

        private static bool IsRealLabel(string s)
        {
            s = s.ToLowerInvariant();
            return s == "real" || s == "drive" || s == "hardware";
        }
    }
}
