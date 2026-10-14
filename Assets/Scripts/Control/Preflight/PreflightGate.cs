using OmniBot.VR.Control.Session;

namespace OmniBot.VR.Control.Preflight
{
    /// <summary>
    /// Whether <see cref="TeleopController"/> may start pumping sticks.
    /// Real sessions stay held until preflight reports no safety failure.
    /// Simulation is not held. This flag does not clear an e-stop, turn off
    /// the deadman, or change a speed cap.
    /// </summary>
    public static class PreflightGate
    {
        public static bool BlocksMotion { get; private set; }

        public static void Hold(SessionKind kind)
        {
            BlocksMotion = kind == SessionKind.Real;
        }

        public static void Allow()
        {
            BlocksMotion = false;
        }

        public static void Apply(PreflightReport report)
        {
            BlocksMotion = report != null && report.BlocksTeleop;
        }
    }
}
