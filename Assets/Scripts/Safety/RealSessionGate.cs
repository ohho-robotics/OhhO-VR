namespace OmniBot.VR.Safety
{
    /// <summary>
    /// Hook for the real-robot start path. Call <see cref="TryBegin"/> with
    /// <see cref="SessionTarget.Real"/> before any publish to a physical robot.
    /// Pass <see cref="SessionTarget.Sim"/> for a simulator; that call is allowed
    /// without acceptance.
    /// </summary>
    public static class RealSessionGate
    {
        /// <summary>
        /// OhhoVrApp.OnRobotPicked starts every garage pick through
        /// TeleopController.StartTeleop. That path has no sim-versus-real
        /// target (OHH-212 is not in this tree). Wiring the gate there would
        /// either block sim sessions or let a real robot start with no
        /// acknowledgement. Leave this hook for the caller that knows the target.
        /// </summary>
        public const string WiringTodo =
            "TODO(OHH-245): OhhoVrApp.OnRobotPicked has no sim-versus-real session target, " +
            "so RealSessionGate.TryBegin is not called. When a caller knows the target, " +
            "call TryBegin(SessionTarget.Real) and refuse StartTeleop while it returns false. " +
            "Pass SessionTarget.Sim for a simulator so sim sessions stay unblocked. " +
            "Do not treat a missing target as sim.";

        public static bool TryBegin(SessionTarget target, out string refusal)
        {
            if (!OperatorRulesAcknowledgement.Blocks(target))
            {
                refusal = null;
                return true;
            }

            refusal = OperatorRulesAcknowledgement.RefusalMessage;
            return false;
        }
    }
}
