namespace OmniBot.VR.Core
{
    /// <summary>
    /// CPU, GPU, and fixed-foveation choices for the OHH-231 budget.
    /// SustainedHigh keeps clocks up for passthrough + video + HUD without the
    /// Boost level, which is more likely to trip a thermal warning in a 30-minute
    /// soak. Fixed Medium foveation (dynamic off) cuts peripheral fill and leaves
    /// the center of the lens, where the video and panels sit, at full resolution.
    /// </summary>
    public static class QuestGraphicsPlan
    {
        public const OVRManager.ProcessorPerformanceLevel CpuLevel =
            OVRManager.ProcessorPerformanceLevel.SustainedHigh;

        public const OVRManager.ProcessorPerformanceLevel GpuLevel =
            OVRManager.ProcessorPerformanceLevel.SustainedHigh;

        public const OVRManager.FoveatedRenderingLevel Foveation =
            OVRManager.FoveatedRenderingLevel.Medium;

        public static OVRManager.FoveatedRenderingLevel ResolveFoveation(
            bool applyFixedFoveation,
            OVRManager.FoveatedRenderingLevel fixedFoveation,
            bool legacyFfrEnabled)
        {
            if (applyFixedFoveation) return fixedFoveation;
            return legacyFfrEnabled
                ? OVRManager.FoveatedRenderingLevel.Low
                : OVRManager.FoveatedRenderingLevel.Off;
        }
    }
}
