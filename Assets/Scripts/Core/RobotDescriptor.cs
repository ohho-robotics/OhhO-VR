namespace OmniBot.VR.Core
{
    /// <summary>
    /// Kinematic description of a robot arm. <see cref="So101"/> is the only
    /// place SO-101 link lengths, joint limits, and kinematic zeros are written
    /// down. <c>RobotProfileFactory</c> and <c>RobotConfig</c> both read it.
    ///
    /// Joint zeros here are radians in the URDF / <c>JointState</c> frame, not
    /// STS3215 servo ticks.
    ///
    /// TODO(OHH-94): Varun measures the real SO-101. The robot-side
    /// <c>omnibot_arm/config/arm_params.yaml</c> <c>home_ticks</c> are still the
    /// uncalibrated midpoint (2048 × 6). Do not invent tick offsets in this
    /// client. When that calibration exists, copy radian offsets into
    /// <c>Assets/Resources/so101_joint_zeros.json</c> and set <c>calibrated</c>
    /// true. Until then homes stay at 0 rad.
    ///
    /// TODO(OHH-221): record a real-arm video of enable, clutch, workspace, and
    /// a pick. This headset change is sim / editmode only and must not drive
    /// the physical arm to produce that video.
    /// </summary>
    public sealed class RobotDescriptor
    {
        /// <summary>
        /// Per-command joint step cap in radians. Matches
        /// <c>omnibot_arm</c> <c>arm_safety.MAX_JOINT_DELTA_RAD</c> (OHH-100).
        /// Do not increase.
        /// </summary>
        public const float MaxJointDeltaRad = 0.15f;

        /// <summary>
        /// Headset Cartesian slew cap (metres / second) applied before IK.
        /// Joint commands are still clamped to <see cref="MaxJointDeltaRad"/>.
        /// </summary>
        public const float MaxCartesianSpeedMps = 0.35f;

        /// <summary>
        /// Controller jog speed. A single published command is still clamped
        /// to <see cref="MaxJointDeltaRad"/>, including long frames.
        /// </summary>
        public const float JogRadPerSec = 0.6f;

        public readonly string Id;
        public readonly string[] JointNames;
        public readonly string[] JointLabels;
        public readonly float[] JointMin;
        public readonly float[] JointMax;

        /// <summary>Kinematic home in radians. Not servo ticks.</summary>
        public readonly float[] JointZeroRad;

        /// <summary>Link lengths in metres: upper arm, forearm, wrist.</summary>
        public readonly float[] LinkLengths;

        public readonly float BaseHeight;
        public readonly float HandWorkspaceRadius;

        public int JointCount => JointNames.Length;

        public float Reach
        {
            get
            {
                float sum = 0f;
                if (LinkLengths == null) return 0f;
                for (int i = 0; i < LinkLengths.Length; i++) sum += LinkLengths[i];
                return sum;
            }
        }

        private RobotDescriptor(
            string id,
            string[] names,
            string[] labels,
            float[] min,
            float[] max,
            float[] zeros,
            float[] links,
            float baseHeight,
            float workspaceRadius)
        {
            Id = id;
            JointNames = names;
            JointLabels = labels;
            JointMin = min;
            JointMax = max;
            JointZeroRad = zeros;
            LinkLengths = links;
            BaseHeight = baseHeight;
            HandWorkspaceRadius = workspaceRadius;
        }

        /// <summary>
        /// Returns <paramref name="requested"/> when it is a tighter positive
        /// cap, otherwise <see cref="MaxJointDeltaRad"/>. Never returns a
        /// larger step than the robot safety limit.
        /// </summary>
        public static float ClampJointDelta(float requested)
        {
            if (requested > 0f && requested < MaxJointDeltaRad) return requested;
            return MaxJointDeltaRad;
        }

        public static readonly RobotDescriptor So101 = new RobotDescriptor(
            "so101",
            new[]
            {
                "arm_shoulder_pan",
                "arm_shoulder_lift",
                "arm_elbow_flex",
                "arm_wrist_flex",
                "arm_wrist_roll",
                "arm_gripper",
            },
            new[] { "Pan", "Lift", "Elbow", "Wrist", "Roll", "Grip" },
            new[] { -3.14f, -1.57f, -1.57f, -1.57f, -3.14f, -0.1f },
            new[] { 3.14f, 1.57f, 1.57f, 1.57f, 3.14f, 0.8f },
            new[] { 0f, 0f, 0f, 0f, 0f, 0f },
            new[] { 0.117f, 0.133f, 0.080f },
            0.350f,
            0.40f);
    }
}
