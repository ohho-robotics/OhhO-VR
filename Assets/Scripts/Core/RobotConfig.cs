namespace OmniBot.VR.Core
{
    /// <summary>
    /// Compile-time constants for OmniBot VR: topic names, physical limits,
    /// arm geometry, and hand-tracking workspace parameters.
    /// </summary>
    public static class RobotConfig
    {
        // ── Publish topic names ──────────────────────────────────────────────
        public const string TOPIC_CMD_VEL         = "/cmd_vel/teleop";
        public const string TOPIC_CONTROL_MODE    = "/control_mode";
        public const string TOPIC_ARM_COMMANDS    = "/arm/joint_commands";
        public const string TOPIC_ARM_ENABLE      = "/arm/enable";
        public const string TOPIC_EMERGENCY_STOP  = "/emergency_stop";
        public const string TOPIC_MISSION_COMMAND = "/mission/command";
        public const string TOPIC_AI_COMMAND      = "/ai/command";
        public const string TOPIC_VR_RECORD_START = "/vr/record_start";
        public const string TOPIC_VR_RECORD_STOP  = "/vr/record_stop";

        // ── Subscribe topic names ────────────────────────────────────────────
        public const string TOPIC_ODOM                = "/odom";
        public const string TOPIC_IMU                 = "/imu/data";
        public const string TOPIC_ARM_STATES          = "/arm/joint_states";
        public const string TOPIC_MISSION_STATUS      = "/mission/status";
        public const string TOPIC_AI_STATUS           = "/ai/status";
        public const string TOPIC_AI_RESPONSE_NEEDED  = "/ai/response_needed";
        public const string TOPIC_CONTROL_MODE_ACTIVE = "/control_mode/active";
        public const string TOPIC_ARM_MODE_ACTIVE     = "/arm/cmd_mode/active";

        // ── Physical limits ──────────────────────────────────────────────────
        /// <summary>Maximum base linear velocity in m/s.</summary>
        public const float MAX_LINEAR_VEL  = 0.2f;

        /// <summary>Maximum base angular velocity in rad/s.</summary>
        public const float MAX_ANGULAR_VEL = 1.0f;

        /// <summary>Rate at which /cmd_vel/teleop is published (Hz).</summary>
        public const float CMD_VEL_PUBLISH_HZ = 20f;

        /// <summary>Rate at which /arm/joint_commands is published (Hz).</summary>
        public const float ARM_PUBLISH_HZ = 20f;

        // ── Arm geometry ─────────────────────────────────────────────────────
        // Single source is RobotDescriptor.So101. These fields are copies so
        // existing callers keep compiling. Do not introduce a second set of
        // literals here. Kinematic zeros are 0 rad; OHH-94 owns servo ticks.
        public static readonly string[] ARM_JOINT_NAMES = (string[])RobotDescriptor.So101.JointNames.Clone();
        public static readonly float[] ARM_JOINT_MIN = (float[])RobotDescriptor.So101.JointMin.Clone();
        public static readonly float[] ARM_JOINT_MAX = (float[])RobotDescriptor.So101.JointMax.Clone();

        /// <summary>Upper-arm link length (shoulder to elbow), metres.</summary>
        public static readonly float ARM_LINK1_LENGTH = RobotDescriptor.So101.LinkLengths[0];

        /// <summary>Forearm link length (elbow to wrist), metres.</summary>
        public static readonly float ARM_LINK2_LENGTH = RobotDescriptor.So101.LinkLengths[1];

        /// <summary>Wrist/end-effector link length, metres.</summary>
        public static readonly float ARM_LINK3_LENGTH = RobotDescriptor.So101.LinkLengths[2];

        /// <summary>Height of arm shoulder above robot base, metres.</summary>
        public static readonly float ARM_BASE_HEIGHT = RobotDescriptor.So101.BaseHeight;

        /// <summary>
        /// Radius of the VR hand-tracking workspace sphere, in metres.
        /// Hand position within this sphere is mapped linearly to the arm workspace.
        /// </summary>
        public static readonly float HAND_WORKSPACE_RADIUS = RobotDescriptor.So101.HandWorkspaceRadius;
    }
}
