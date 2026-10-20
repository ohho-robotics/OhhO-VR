namespace OmniBot.VR.AprilTagSpike
{
    /// <summary>
    /// Spike targets from OHH-213. Distances are the measurement protocol.
    /// The 1.5 m gate lives on <see cref="AprilTagPoseError"/>.
    /// </summary>
    public static class AprilTagSpikeConfig
    {
        public const string Family = "tag36h11";

        public const int CaptureWidth = 1280;
        public const int CaptureHeight = 720;

        public const float MinRangeMeters = 0.5f;
        public const float MaxRangeMeters = 3f;

        public const string StubDetectorName = "stub-tag36h11";
        public const string ManualBaselineName = "manual-placement";

        /// <summary>Tape distances for the Quest measurement script, in meters.</summary>
        public static readonly float[] ProtocolRangeMeters =
        {
            0.5f,
            1f,
            1.5f,
            2f,
            3f,
        };
    }
}
