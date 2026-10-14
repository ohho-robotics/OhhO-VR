using System.Collections.Generic;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Where IK is solved for the arm — determines whether the manipulation
    /// scheme streams joint angles (headset-side) or a Cartesian target pose
    /// (robot-side, solved by MoveIt 2 Servo with collision awareness).
    /// </summary>
    public enum IkLocation
    {
        /// <summary>Solve IK in Unity, stream joint angles. Lowest latency.</summary>
        Headset,
        /// <summary>Stream Cartesian target, let MoveIt Servo solve on the robot. Collision-aware.</summary>
        Robot,
    }

    /// <summary>
    /// Runtime capability profile for the robot the user picked in their garage —
    /// a C# mirror of <c>website/lib/garage/robot-config.ts</c>
    /// <c>RobotConfig</c>. Replaces the hardcoded <c>Core/RobotConfig.cs</c>
    /// constants for the teleop control layer: nothing about <i>which</i> robot or
    /// <i>what DOF</i> lives in C# constants any more, it comes from the catalog
    /// at runtime (see <see cref="RobotProfileFactory"/>).
    ///
    /// Built once when the user selects a robot, then handed to the
    /// <see cref="Drive.IDriveScheme"/> + <see cref="Manip.IManipulationScheme"/>
    /// which read their topics, joint limits and speed caps from it.
    /// </summary>
    public class RobotProfile
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string RobotId;          // hardware model id (e.g. "omnibot-pro")
        public string Name;
        public string Manufacturer;
        public string Category;         // catalog category id

        // ── Base / locomotion ─────────────────────────────────────────────────
        public DriveKind Drive;
        public string DriveLabel;
        public bool Holonomic;
        public int BaseDof;
        public string[] BaseActionLabels;
        public float MaxLinVel;         // m/s, forward / vertical
        public float MaxLatVel;         // m/s, lateral. 0 when the base cannot strafe
        public float MaxAngVel;         // rad/s

        /// <summary>Caps copied from the descriptor or the catalog before calibration. Calibration cannot exceed these.</summary>
        public float HardMaxLinVel;
        public float HardMaxLatVel;
        public float HardMaxAngVel;
        public float HardMaxAccelLin;   // m/s^2. 0 = no slew limit (catalog path)
        public float HardMaxAccelYaw;   // rad/s^2. 0 = no slew limit
        public float HardMaxJointStep;  // rad per command. 0 = use the 0.15 ceiling
        public JointSpec[] HardJoints;
        /// <summary>True when speed and topics came from a RobotDescriptor. Those speeds stay inside the OmniBot ceilings.</summary>
        public bool CapsFromDescriptor;

        // ── Manipulator ───────────────────────────────────────────────────────
        public bool HasArm;
        public int ArmDof;
        public JointSpec[] Joints;      // arm joints (names + limits); empty if HasArm is false

        // ── Capabilities (mirrors RobotConfig.capabilities) ───────────────────
        public bool CanNavigate;
        public bool CanManipulate;
        public bool IsAerial;
        public bool IsUnderwater;
        public bool IsStationary;
        public bool IsLegged;

        // ── ROS topics the teleop layer publishes / subscribes ────────────────
        public RobotTopics Topics;

        // ── Hand-tracking workspace (arm base in the MR scene) ────────────────
        /// <summary>Radius of the hand-tracking workspace sphere, metres.</summary>
        public float HandWorkspaceRadius;
        /// <summary>Arm shoulder height above the robot base, metres.</summary>
        public float ArmBaseHeight;
        /// <summary>Max arm reach (sum of link lengths), metres. 0 if no arm.</summary>
        public float ArmMaxReach;
        /// <summary>
        /// Link lengths between consecutive arm joints, metres. Used by the
        /// generic FABRIK solver for non-SO-101 arms. For SO-101 these are
        /// [0.117, 0.133, 0.080] (upper arm, forearm, wrist). Empty if the arm
        /// geometry is unknown — the solver falls back to equal-length segments.
        /// </summary>
        public float[] ArmLinkLengths;

        /// <summary>True if this is a dual-arm humanoid (two arms driven by two hands).</summary>
        public bool IsDualArm;

        // ── IK location (Phase 4) ──────────────────────────────────────────────
        /// <summary>
        /// Where inverse kinematics is solved. <c>Headset</c> = solve in Unity,
        /// stream joint angles (default, lowest latency). <c>Robot</c> = stream
        /// the Cartesian target pose, let MoveIt 2 Servo solve on the robot
        /// (collision-aware, higher latency but safer).
        /// </summary>
        public IkLocation IkLocation;

        /// <summary>package:// or http URL from the descriptor. Empty on the catalog path.</summary>
        public string UrdfUrl;

        public int TotalDof => ArmDof + BaseDof;
    }

    /// <summary>
    /// ROS topics for one robot — the teleop publish topics plus the telemetry
    /// subscriptions. For OmniBot these match <c>Core/RobotConfig.cs</c>; for
    /// other robots they come from the catalog derivation (see
    /// <see cref="RobotProfileFactory"/>).
    /// </summary>
    public class RobotTopics
    {
        // Publish (teleop → robot). Command topics come from the descriptor when connected.
        public string CmdVel;           // base velocity (Twist) — descriptor topics.cmd
        public string ControlMode;      // cmd_vel_mux mode string (not in the descriptor; catalog only)
        public string ArmCommands;      // arm joint positions (JointState) — headset IK
        public string ArmEnable;        // arm enable bool
        public string EmergencyStop;    // e-stop bool — descriptor topics.estop
        public string IkTargetPose;     // Cartesian EE target (PoseStamped) — robot IK (MoveIt Servo)
        public string RecordStart;      // dataset episode start (catalog; the descriptor does not name it)
        public string RecordStop;

        // Subscribe (robot → teleop)
        public string Odom;             // descriptor topics.state
        public string Imu;
        public string JointStates;
        public string ControlModeActive;
        public string Heartbeat;        // descriptor topics.heartbeat
        public string Telemetry;        // descriptor topics.telemetry
        public List<string> Images = new List<string>();
    }
}
