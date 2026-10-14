namespace OmniBot.VR.Core
{
    /// <summary>
    /// Physical limits and arm geometry. Topic names are not constants:
    /// connected robots take them from the latched RobotDescriptor, and offline
    /// browsing takes them from the catalog profile.
    /// </summary>
    public static class RobotConfig
    {
        // ── Physical limits ──────────────────────────────────────────────────
        /// <summary>Maximum base linear velocity in m/s.</summary>
        public const float MAX_LINEAR_VEL  = 0.2f;

        /// <summary>Maximum base angular velocity in rad/s.</summary>
        public const float MAX_ANGULAR_VEL = 1.0f;

        /// <summary>Rate at which /cmd_vel/teleop is published (Hz).</summary>
        public const float CMD_VEL_PUBLISH_HZ = 20f;

        /// <summary>Rate at which /arm/joint_commands is published (Hz).</summary>
        public const float ARM_PUBLISH_HZ = 20f;

        // ── Arm joint names (must match omnibot_arm and smolvla_node) ────────
        public static readonly string[] ARM_JOINT_NAMES =
        {
            "arm_shoulder_pan",
            "arm_shoulder_lift",
            "arm_elbow_flex",
            "arm_wrist_flex",
            "arm_wrist_roll",
            "arm_gripper"
        };

        // ── Arm joint limits (radians) ────────────────────────────────────────
        public static readonly float[] ARM_JOINT_MIN =
        {
            -3.14f,  // arm_shoulder_pan
            -1.57f,  // arm_shoulder_lift
            -1.57f,  // arm_elbow_flex
            -1.57f,  // arm_wrist_flex
            -3.14f,  // arm_wrist_roll
            -0.1f    // arm_gripper
        };

        public static readonly float[] ARM_JOINT_MAX =
        {
            3.14f,  // arm_shoulder_pan
            1.57f,  // arm_shoulder_lift
            1.57f,  // arm_elbow_flex
            1.57f,  // arm_wrist_flex
            3.14f,  // arm_wrist_roll
            0.8f    // arm_gripper
        };

        // ── SO-101 arm link lengths (metres, from CAD) ───────────────────────
        /// <summary>Upper-arm link length (shoulder to elbow), metres.</summary>
        public const float ARM_LINK1_LENGTH = 0.117f;

        /// <summary>Forearm link length (elbow to wrist), metres.</summary>
        public const float ARM_LINK2_LENGTH = 0.133f;

        /// <summary>Wrist/end-effector link length, metres.</summary>
        public const float ARM_LINK3_LENGTH = 0.080f;

        /// <summary>Height of arm shoulder above robot base, metres.</summary>
        public const float ARM_BASE_HEIGHT = 0.350f;

        // ── Hand tracking workspace ──────────────────────────────────────────
        /// <summary>
        /// Radius of the VR hand-tracking workspace sphere, in metres.
        /// Hand position within this sphere is mapped linearly to the arm workspace.
        /// Roughly equal to maximum arm reach (L1+L2+L3 ≈ 0.33 m with margin).
        /// </summary>
        public const float HAND_WORKSPACE_RADIUS = 0.40f;
    }
}
