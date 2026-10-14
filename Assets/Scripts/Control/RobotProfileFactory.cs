using System;
using System.Collections.Generic;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Descriptor;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Builds a <see cref="RobotProfile"/> from a robot the user selected in
    /// their garage — the C# counterpart of
    /// <c>website/lib/garage/robot-config.ts</c> <c>deriveRobotConfig()</c> +
    /// <c>FLAGSHIP_OVERRIDES</c>. Pure data: no Unity, no ROS — just turns the
    /// catalog <see cref="GarageRobot"/> into the structured profile the control
    /// schemes read.
    ///
    /// Derivation is deterministic and offline (the catalog JSON is already
    /// fetched by <see cref="OhhoCatalog"/>). Flagship overrides pin precision for
    /// marquee robots — today OmniBot (SO-101 6-DOF arm + mecanum base) so the
    /// headset's teleop matches the old hardcoded <c>Core/RobotConfig.cs</c>
    /// exactly.
    /// </summary>
    public static class RobotProfileFactory
    {
        // ── SO-101 6-DOF arm — the OhhO reference arm (mirrors robot-config.ts) ──
        public static readonly JointSpec[] So101Joints =
        {
            new JointSpec("arm_shoulder_pan",  "Pan",   -3.14f, 3.14f, 0f),
            new JointSpec("arm_shoulder_lift", "Lift",  -1.57f, 1.57f, 0f),
            new JointSpec("arm_elbow_flex",    "Elbow", -1.57f, 1.57f, 0f),
            new JointSpec("arm_wrist_flex",    "Wrist", -1.57f, 1.57f, 0f),
            new JointSpec("arm_wrist_roll",    "Roll",  -3.14f, 3.14f, 0f),
            new JointSpec("arm_gripper",       "Grip",  -0.1f,  0.8f,  0f),
        };

        // SO-101 link lengths (metres, from CAD) — used for the hand→arm reach mapping.
        private const float So101Link1 = 0.117f;
        private const float So101Link2 = 0.133f;
        private const float So101Link3 = 0.080f;
        private const float So101BaseHeight = 0.350f;

        // SO-101 link lengths array (for the generic FABRIK solver).
        private static readonly float[] So101LinkLengths = { So101Link1, So101Link2, So101Link3 };

        // Generic default link length when a flagship arm's geometry is unknown.
        private const float GenericLinkLength = 0.10f;

        // Hand-tracking workspace radius (matches the old RobotConfig constant).
        private const float HandWorkspaceRadius = 0.40f;

        /// <summary>
        /// Derive a profile from a resolved garage robot. Returns null only if the
        /// catalog entry is missing. Apply the flagship override for OmniBot so the
        /// reference robot is pinned to the exact 9-DOF stack every console used to
        /// hardcode.
        /// </summary>
        public static RobotProfile FromGarageRobot(GarageRobot robot)
        {
            if (robot?.HardwareModel == null) return null;

            var hw = robot.HardwareModel;
            string category = robot.Category?.Id ?? "unknown";
            string modelId = hw.Id ?? "";

            var profile = Derive(hw, robot.RobotType, category);
            ApplyFlagshipOverride(profile, modelId, category);
            SealCatalogCaps(profile);
            return profile;
        }

        /// <summary>
        /// Build a profile from a connected robot's latched descriptor. This is
        /// preferred over <see cref="FromGarageRobot"/> whenever a document has
        /// been received. <paramref name="catalogFallback"/> supplies fields the
        /// descriptor does not name (arm command topics, link lengths) for the
        /// same robot, and any tighter speed it already has is kept.
        /// Returns the fallback when the descriptor is missing its hard caps —
        /// missing caps are not treated as unlimited.
        /// </summary>
        public static RobotProfile FromDescriptor(RobotDescriptor descriptor)
            => FromDescriptor(descriptor, null);

        public static RobotProfile FromDescriptor(RobotDescriptor descriptor, RobotProfile catalogFallback)
        {
            if (descriptor?.Safety?.Hard == null || descriptor.Drive?.Caps == null || descriptor.Topics == null)
                return catalogFallback;

            var hard = descriptor.Safety.Hard;
            var caps = descriptor.Drive.Caps;
            var drive = ParseDescriptorDrive(descriptor.Drive.Kind);
            bool fixedBase = drive == DriveKind.FixedBase;
            bool sameRobot = SameRobot(catalogFallback, descriptor);

            float maxLin = fixedBase ? 0f : Cap(caps.VxMps, hard.MaxVxMps, RobotConfig.MAX_LINEAR_VEL);
            float maxLat = fixedBase ? 0f : Cap(caps.VyMps, hard.MaxVyMps, RobotConfig.MAX_LINEAR_VEL);
            float maxAng = fixedBase ? 0f : Cap(caps.WzRadS, hard.MaxWzRadS, RobotConfig.MAX_ANGULAR_VEL);
            if (!descriptor.Drive.Holonomic) maxLat = 0f;

            if (sameRobot)
            {
                maxLin = MinPositive(maxLin, catalogFallback.HardMaxLinVel > 0f
                    ? catalogFallback.HardMaxLinVel : catalogFallback.MaxLinVel);
                maxAng = MinPositive(maxAng, catalogFallback.HardMaxAngVel > 0f
                    ? catalogFallback.HardMaxAngVel : catalogFallback.MaxAngVel);
                if (descriptor.Drive.Holonomic)
                    maxLat = MinPositive(maxLat, catalogFallback.HardMaxLatVel > 0f
                        ? catalogFallback.HardMaxLatVel : catalogFallback.MaxLatVel);
            }

            var joints = JointsFromDescriptor(descriptor);
            if (sameRobot && catalogFallback.HardJoints != null &&
                catalogFallback.HardJoints.Length == joints.Length)
            {
                joints = IntersectJoints(joints, catalogFallback.HardJoints);
            }

            var spec = DriveSpecs.Get(drive);
            bool hasArm = joints.Length > 0;
            // Arm-only: fixed base and every drive cap at zero. topics.cmd is the
            // arm command and topics.state is joint state. A fixed-base document
            // that still advertises drive caps keeps those topics off the arm.
            bool armOnly = fixedBase
                && caps.VxMps <= 0
                && caps.VyMps <= 0
                && caps.WzRadS <= 0
                && hasArm;
            var topics = TopicsFromDescriptor(descriptor, fixedBase, armOnly);
            if (sameRobot && catalogFallback.Topics != null)
                CopyCatalogOnlyTopics(topics, catalogFallback.Topics, armOnly);

            float[] linkLengths = null;
            float armBase = 0f;
            float armReach = 0f;
            float workspace = HandWorkspaceRadius;
            var ik = IkLocation.Headset;
            if (sameRobot)
            {
                linkLengths = catalogFallback.ArmLinkLengths;
                armBase = catalogFallback.ArmBaseHeight;
                armReach = catalogFallback.ArmMaxReach;
                if (catalogFallback.HandWorkspaceRadius > 0f)
                    workspace = catalogFallback.HandWorkspaceRadius;
                ik = catalogFallback.IkLocation;
            }

            if (hasArm && (linkLengths == null || linkLengths.Length == 0) && IsSo101(descriptor))
            {
                linkLengths = (float[])So101LinkLengths.Clone();
                armBase = So101BaseHeight;
                armReach = So101Link1 + So101Link2 + So101Link3;
            }

            return new RobotProfile
            {
                RobotId = descriptor.Id,
                Name = descriptor.Model,
                Manufacturer = sameRobot ? catalogFallback.Manufacturer : "",
                Category = sameRobot ? catalogFallback.Category : "",
                Drive = drive,
                DriveLabel = spec.Label,
                Holonomic = descriptor.Drive.Holonomic && !fixedBase,
                BaseDof = fixedBase ? 0 : spec.BaseDof,
                BaseActionLabels = spec.ActionLabels,
                MaxLinVel = maxLin,
                MaxLatVel = maxLat,
                MaxAngVel = maxAng,
                HardMaxLinVel = maxLin,
                HardMaxLatVel = maxLat,
                HardMaxAngVel = maxAng,
                HardMaxAccelLin = fixedBase ? 0f : Cap(
                    Math.Min(hard.MaxAccelXMps2, hard.MaxAccelYMps2),
                    Math.Min(hard.MaxAccelXMps2, hard.MaxAccelYMps2),
                    DescriptorCeilings.MaxAccelLinMps2),
                HardMaxAccelYaw = fixedBase ? 0f : Cap(hard.MaxAccelWzRadS2, hard.MaxAccelWzRadS2, DescriptorCeilings.MaxAccelYawRadS2),
                HardMaxJointStep = Cap(hard.MaxJointStepRad, hard.MaxJointStepRad, DescriptorCeilings.MaxJointStepRad),
                HasArm = hasArm,
                ArmDof = joints.Length,
                Joints = joints,
                HardJoints = CloneJoints(joints),
                CanNavigate = !fixedBase && spec.BaseDof > 0,
                CanManipulate = hasArm,
                IsAerial = false,
                IsUnderwater = sameRobot && catalogFallback.IsUnderwater,
                IsStationary = fixedBase || spec.BaseDof == 0,
                IsLegged = drive == DriveKind.Quadruped || drive == DriveKind.Bipedal,
                IsDualArm = descriptor.Arms != null && descriptor.Arms.Length > 1,
                IkLocation = ik,
                Topics = topics,
                HandWorkspaceRadius = workspace,
                ArmBaseHeight = armBase,
                ArmMaxReach = armReach,
                ArmLinkLengths = linkLengths,
                CapsFromDescriptor = true,
                UrdfUrl = descriptor.UrdfUrl,
                TelemetryFields = CloneFields(descriptor.Telemetry?.Fields),
            };
        }

        // ── Derivation (mirrors deriveRobotConfig) ──────────────────────────────

        private static RobotProfile Derive(VrHardwareModel hw, VrRobotType type, string category)
        {
            var drive = DriveSpecs.ParseDrive(hw.Locomotion, category);
            var spec = DriveSpecs.Get(drive);

            bool hasArm = hw.HasArm;
            int armDof = hasArm ? ParseDof(hw.Specs) ?? 6 : 0;
            var joints = hasArm ? GenericJoints(armDof) : System.Array.Empty<JointSpec>();

            float speed = ParseSpeed(hw.Specs) ?? -1f;
            float maxLinVel = speed >= 0f ? speed : spec.MaxLinVel;
            float maxAngVel = spec.MaxAngVel;

            bool canNavigate = spec.BaseDof > 0;
            bool isAerial = drive == DriveKind.Quadrotor || drive == DriveKind.Hexacopter ||
                            drive == DriveKind.Vtol || drive == DriveKind.FixedWing;
            bool isUnderwater = category == "underwater-rov";
            bool isStationary = spec.BaseDof == 0;
            bool isLegged = drive == DriveKind.Quadruped || drive == DriveKind.Hexapod ||
                            drive == DriveKind.Bipedal;

            string jointStatesTopic = (hasArm || isLegged || category == "humanoid") &&
                (category == "mobile-manipulator" || category == "wheeled")
                    ? "/arm/joint_states" : "/joint_states";

            return new RobotProfile
            {
                RobotId = hw.Id,
                Name = hw.Name,
                Manufacturer = hw.Manufacturer,
                Category = category,
                Drive = drive,
                DriveLabel = spec.Label,
                Holonomic = spec.Holonomic,
                BaseDof = spec.BaseDof,
                BaseActionLabels = spec.ActionLabels,
                MaxLinVel = maxLinVel,
                MaxAngVel = maxAngVel,
                HasArm = hasArm,
                ArmDof = armDof,
                Joints = joints,
                CanNavigate = canNavigate,
                CanManipulate = hasArm,
                IsAerial = isAerial,
                IsUnderwater = isUnderwater,
                IsStationary = isStationary,
                IsLegged = isLegged,
                IsDualArm = category == "humanoid",
                IkLocation = IkLocation.Headset, // default: solve in Unity
                Topics = BuildTopics(spec.BaseDof, hasArm, category, jointStatesTopic),
                HandWorkspaceRadius = HandWorkspaceRadius,
                ArmBaseHeight = hasArm ? So101BaseHeight : 0f,
                ArmMaxReach = hasArm ? So101Link1 + So101Link2 + So101Link3 : 0f,
                ArmLinkLengths = hasArm ? (float[])So101LinkLengths.Clone() : null,
            };
        }

        private static RobotTopics BuildTopics(int baseDof, bool hasArm, string category, string jointStates)
        {
            var topics = new RobotTopics
            {
                CmdVel        = baseDof > 0 ? "/cmd_vel/teleop" : "",
                ControlMode   = "/control_mode",
                ControlModeActive = "/control_mode/active",
                ArmCommands   = hasArm ? "/arm/joint_commands" : "",
                ArmEnable     = hasArm ? "/arm/enable" : "",
                EmergencyStop = "/emergency_stop",
                IkTargetPose  = hasArm ? "/servo_server/target_pose" : "",
                RecordStart   = "/vr/record_start",
                RecordStop    = "/vr/record_stop",
                Odom          = baseDof > 0 ? "/odom" : "",
                Imu           = "/imu/data",
                JointStates   = jointStates,
            };
            // Image topics by category (mirrors deriveSensors image list)
            if (category == "mobile-manipulator")
                topics.Images.AddRange(new[] { "/camera/front/image_raw", "/camera/wrist/image_raw", "/camera/base/bev/image_raw" });
            else if (category == "drones")
                topics.Images.AddRange(new[] { "/camera/fpv/image_raw", "/camera/down/image_raw" });
            else
                topics.Images.Add("/camera/front/image_raw");
            return topics;
        }

        // ── Flagship overrides (mirrors FLAGSHIP_OVERRIDES) ──────────────────────

        private static void ApplyFlagshipOverride(RobotProfile p, string modelId, string category)
        {
            switch (modelId)
            {
                case "omnibot":
                case "omnibot-pro":
                    p.Joints = (JointSpec[])So101Joints.Clone();
                    p.ArmDof = 6;
                    p.MaxLinVel = 0.2f;
                    p.MaxAngVel = 1.0f;
                    p.ArmBaseHeight = So101BaseHeight;
                    p.ArmMaxReach = So101Link1 + So101Link2 + So101Link3;
                    p.ArmLinkLengths = (float[])So101LinkLengths.Clone();
                    p.IsDualArm = false;
                    // OmniBot publishes arm commands + enables on the arm_ topics.
                    p.Topics.ArmCommands = "/arm/joint_commands";
                    p.Topics.ArmEnable = "/arm/enable";
                    p.Topics.JointStates = "/arm/joint_states";
                    break;
                case "so101-arm":
                    p.Joints = (JointSpec[])So101Joints.Clone();
                    p.ArmDof = 6;
                    p.ArmBaseHeight = So101BaseHeight;
                    p.ArmMaxReach = So101Link1 + So101Link2 + So101Link3;
                    p.ArmLinkLengths = (float[])So101LinkLengths.Clone();
                    break;
                case "ur5e":
                    // UR5e: 6-DOF, 850 mm reach. Link lengths approximate from UR spec.
                    p.ArmDof = 6;
                    p.ArmBaseHeight = 0.180f;
                    p.ArmMaxReach = 0.850f;
                    p.ArmLinkLengths = new float[] { 0.425f, 0.392f, 0.033f };
                    p.IsDualArm = false;
                    // UR5e runs with MoveIt 2 Servo → solve IK on the robot (collision-aware)
                    p.IkLocation = IkLocation.Robot;
                    p.Topics.IkTargetPose = "/servo_server/target_pose";
                    break;
                case "unitree-g1":
                case "unitree-h1":
                case "figure-02":
                    // Humanoid dual-arm: two 7-DOF arms driven by two hands.
                    p.IsDualArm = true;
                    p.ArmDof = 14;
                    p.ArmBaseHeight = 1.20f;
                    p.ArmMaxReach = 0.70f;
                    p.ArmLinkLengths = new float[] { 0.20f, 0.20f, 0.15f, 0.05f, 0.05f, 0.05f };
                    break;
            }
        }

        // ── Spec-string parsers (mirror parseDof / parseSpeed) ───────────────────

        private static int? ParseDof(Dictionary<string, string> specs)
        {
            if (specs == null) return null;
            string raw = null;
            if (!specs.TryGetValue("dof", out raw)) specs.TryGetValue("DOF", out raw);
            if (string.IsNullOrEmpty(raw)) specs.TryGetValue("joints", out raw);
            if (string.IsNullOrEmpty(raw)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"\d+");
            return match.Success ? int.Parse(match.Value) : (int?)null;
        }

        private static float? ParseSpeed(Dictionary<string, string> specs)
        {
            if (specs == null) return null;
            string raw = null;
            if (!specs.TryGetValue("speed", out raw)) specs.TryGetValue("topSpeed", out raw);
            if (string.IsNullOrEmpty(raw)) specs.TryGetValue("maxSpeed", out raw);
            if (string.IsNullOrEmpty(raw)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                raw, @"([\d.]+)\s*(km/h|kmh|m/s)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            if (!float.TryParse(m.Groups[1].Value, out float val)) return null;
            bool kmh = m.Groups[2].Success &&
                m.Groups[2].Value.ToLowerInvariant().Contains("km");
            return kmh ? val / 3.6f : val;
        }

        private static JointSpec[] GenericJoints(int n)
        {
            string[] labels = { "Base", "Shoulder", "Elbow", "Wrist 1", "Wrist 2", "Wrist 3", "Wrist 4" };
            var outJ = new JointSpec[n];
            for (int i = 0; i < n; i++)
                outJ[i] = new JointSpec($"joint_{i + 1}", i < labels.Length ? labels[i] : $"J{i + 1}", -3.14f, 3.14f, 0f);
            return outJ;
        }

        // ── Descriptor helpers ────────────────────────────────────────────────────

        private static void SealCatalogCaps(RobotProfile profile)
        {
            if (profile == null) return;
            profile.HardMaxLinVel = profile.MaxLinVel;
            profile.MaxLatVel = profile.Holonomic ? profile.MaxLinVel : 0f;
            profile.HardMaxLatVel = profile.MaxLatVel;
            profile.HardMaxAngVel = profile.MaxAngVel;
            if (profile.HardMaxJointStep <= 0f)
                profile.HardMaxJointStep = (float)DescriptorCeilings.MaxJointStepRad;
            profile.HardJoints = CloneJoints(profile.Joints);
            profile.CapsFromDescriptor = false;
        }

        private static bool SameRobot(RobotProfile catalog, RobotDescriptor descriptor)
        {
            if (catalog == null || descriptor == null || string.IsNullOrEmpty(catalog.RobotId))
                return false;
            if (string.Equals(catalog.RobotId, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                return true;
            return descriptor.Id == "omnibot" &&
                   (catalog.RobotId == "omnibot" || catalog.RobotId == "omnibot-pro");
        }

        private static DriveKind ParseDescriptorDrive(string kind)
        {
            switch (kind)
            {
                case "mecanum": return DriveKind.Mecanum;
                case "differential": return DriveKind.Differential;
                case "ackermann": return DriveKind.Ackermann;
                case "skid-steer": return DriveKind.SkidSteer;
                case "tracked": return DriveKind.Tracked;
                case "quadruped": return DriveKind.Quadruped;
                case "bipedal": return DriveKind.Bipedal;
                case "fixed-base": return DriveKind.FixedBase;
                default: return DriveKind.Unknown;
            }
        }

        private static float Cap(double advertised, double hard, double ceiling)
        {
            double value = advertised;
            if (hard < value) value = hard;
            if (ceiling < value) value = ceiling;
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            // Snap values that sit on a ceiling so a binary 0.2 cannot round above the cap.
            if (ceiling - value <= 1e-6) return (float)ceiling;
            return (float)value;
        }

        private static float MinPositive(float value, float other)
        {
            if (other > 0f && other < value) return other;
            return value;
        }

        private static JointSpec[] JointsFromDescriptor(RobotDescriptor descriptor)
        {
            if (descriptor.Arms == null || descriptor.Arms.Length == 0)
                return Array.Empty<JointSpec>();

            int count = 0;
            foreach (var arm in descriptor.Arms)
                if (arm?.Joints != null) count += arm.Joints.Length;

            var joints = new JointSpec[count];
            int index = 0;
            foreach (var arm in descriptor.Arms)
            {
                if (arm?.Joints == null) continue;
                foreach (var joint in arm.Joints)
                {
                    float min = (float)joint.Min;
                    float max = (float)joint.Max;
                    if (min > max)
                    {
                        float swap = min;
                        min = max;
                        max = swap;
                    }

                    float home = MathfClamp((float)joint.Home, min, max);
                    joints[index++] = new JointSpec(joint.Name, joint.Label, min, max, home);
                }
            }

            return joints;
        }

        private static JointSpec[] IntersectJoints(JointSpec[] descriptorJoints, JointSpec[] tighter)
        {
            var result = new JointSpec[descriptorJoints.Length];
            for (int i = 0; i < descriptorJoints.Length; i++)
            {
                var joint = descriptorJoints[i];
                float min = joint.Min;
                float max = joint.Max;
                if (i < tighter.Length)
                {
                    if (tighter[i].Min > min) min = tighter[i].Min;
                    if (tighter[i].Max < max) max = tighter[i].Max;
                    if (min > max)
                    {
                        min = tighter[i].Min;
                        max = tighter[i].Max;
                    }
                }

                float home = MathfClamp(joint.Home, min, max);
                result[i] = new JointSpec(joint.Name, joint.Label, min, max, home);
            }

            return result;
        }

        private static JointSpec[] CloneJoints(JointSpec[] joints)
        {
            if (joints == null || joints.Length == 0) return Array.Empty<JointSpec>();
            var copy = new JointSpec[joints.Length];
            Array.Copy(joints, copy, joints.Length);
            return copy;
        }

        private static RobotTopics TopicsFromDescriptor(RobotDescriptor descriptor, bool fixedBase, bool armOnly)
        {
            var images = new List<string>();
            if (descriptor.Topics.Images != null)
                images.AddRange(descriptor.Topics.Images);

            string cmd = descriptor.Topics.Cmd;
            string state = descriptor.Topics.State;
            return new RobotTopics
            {
                // fixed-base must not be commanded, even if the document names a cmd topic.
                CmdVel = fixedBase ? "" : cmd,
                // Arm-only reuses the two required topic slots: cmd is the arm, state is joints.
                ArmCommands = armOnly ? cmd : "",
                JointStates = armOnly ? state : "",
                Odom = armOnly ? "" : state,
                EmergencyStop = descriptor.Topics.Estop,
                Heartbeat = descriptor.Topics.Heartbeat,
                Telemetry = descriptor.Topics.Telemetry,
                Images = images,
            };
        }

        private static void CopyCatalogOnlyTopics(RobotTopics target, RobotTopics catalog, bool armOnly)
        {
            // The base mux is not part of an arm-only document.
            if (!armOnly)
            {
                target.ControlMode = catalog.ControlMode;
                target.ControlModeActive = catalog.ControlModeActive;
            }

            if (!armOnly || string.IsNullOrEmpty(target.ArmCommands))
                target.ArmCommands = catalog.ArmCommands;
            target.ArmEnable = catalog.ArmEnable;
            target.IkTargetPose = catalog.IkTargetPose;
            target.Imu = catalog.Imu;
            if (!armOnly || string.IsNullOrEmpty(target.JointStates))
                target.JointStates = catalog.JointStates;
            target.RecordStart = catalog.RecordStart;
            target.RecordStop = catalog.RecordStop;
        }

        private static string[] CloneFields(string[] fields)
        {
            if (fields == null || fields.Length == 0) return Array.Empty<string>();
            var copy = new string[fields.Length];
            Array.Copy(fields, copy, fields.Length);
            return copy;
        }

        private static bool IsSo101(RobotDescriptor descriptor)
        {
            if (descriptor.Arms == null) return false;
            foreach (var arm in descriptor.Arms)
            {
                if (arm != null && string.Equals(arm.Name, "so101", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static float MathfClamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
