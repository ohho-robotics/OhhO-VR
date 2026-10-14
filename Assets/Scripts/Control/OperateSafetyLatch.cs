namespace OmniBot.VR.Control
{
    /// <summary>
    /// Operate-screen e-stop latch. Grip e-stop in the drive schemes is unchanged
    /// when this is clear. While it is set, grip release does not clear the stop
    /// and autonomous mode changes are not published. Only an explicit reset
    /// clears it. This never turns a stop off by itself.
    /// </summary>
    public static class OperateSafetyLatch
    {
        public static bool UiEStopLatched { get; private set; }

        public static void Latch() => UiEStopLatched = true;

        public static void Clear() => UiEStopLatched = false;
    }

    /// <summary>
    /// Pure safety decisions shared by the drive scheme and the operate bar.
    /// </summary>
    public static class OperateSafetyRules
    {
        /// <summary>Grip release clears a grip stop only when the UI has not latched one.</summary>
        public static bool GripReleaseClearsStop(bool uiLatched) => !uiLatched;

        /// <summary>Nav2 / policy / teleop publishes wait until both stops are clear.</summary>
        public static bool ModePublishAllowed(bool uiLatched, bool gripStopActive)
            => !uiLatched && !gripStopActive;

        /// <summary>Reset cannot clear a stop while both grips are still held.</summary>
        public static bool ResetAllowed(bool gripHeld) => !gripHeld;
    }
}
