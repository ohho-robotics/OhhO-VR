using System.Collections.Generic;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;

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
        // SO-101 joint specs. Names, limits, link lengths, and kinematic zeros
        // are RobotDescriptor.So101. Homes add so101_joint_zeros.json offsets,
        // which ship as 0 until OHH-94 sets calibrated=true.
        public static JointSpec[] So101Joints => CreateSo101Joints();

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
            profile.Deployment = ArmDeploymentRules.FromGarageStatus(robot.UserRobot != null ? robot.UserRobot.Status : null);
            profile.MaxJointDeltaRad = RobotDescriptor.MaxJointDeltaRad;
            bool so101Family = modelId == "so101-arm" || modelId == "omnibot" || modelId == "omnibot-pro";
            profile.JointZerosCalibrated = so101Family && So101JointZeroConfig.Load().Calibrated;
            return profile;
        }

        /// <summary>
        /// SO-101 joints from <see cref="RobotDescriptor"/> plus the loaded
        /// radian offsets. Uncalibrated configs contribute 0.
        /// </summary>
        public static JointSpec[] CreateSo101Joints()
        {
            return CreateSo101Joints(So101JointZeroConfig.Load().OffsetsRad);
        }

        /// <summary>
        /// Builds SO-101 joints from <see cref="RobotDescriptor"/> and adds
        /// <paramref name="offsetsRad"/> to the kinematic zeros. Null offsets
        /// leave the URDF zero unchanged.
        /// </summary>
        public static JointSpec[] CreateSo101Joints(float[] offsetsRad)
        {
            var d = RobotDescriptor.So101;
            var joints = new JointSpec[d.JointCount];
            for (int i = 0; i < d.JointCount; i++)
            {
                float home = d.JointZeroRad[i];
                if (offsetsRad != null && i < offsetsRad.Length) home += offsetsRad[i];
                joints[i] = new JointSpec(d.JointNames[i], d.JointLabels[i], d.JointMin[i], d.JointMax[i], home);
            }
            return joints;
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
                HandWorkspaceRadius = RobotDescriptor.So101.HandWorkspaceRadius,
                ArmBaseHeight = hasArm ? RobotDescriptor.So101.BaseHeight : 0f,
                ArmMaxReach = hasArm ? RobotDescriptor.So101.Reach : 0f,
                ArmLinkLengths = hasArm ? (float[])RobotDescriptor.So101.LinkLengths.Clone() : null,
                MaxJointDeltaRad = RobotDescriptor.MaxJointDeltaRad,
                Deployment = ArmDeployment.Real,
            };
        }

        private static RobotTopics BuildTopics(int baseDof, bool hasArm, string category, string jointStates)
        {
            var topics = new RobotTopics
            {
                CmdVel        = baseDof > 0 ? "/cmd_vel/teleop" : "",
                ControlMode   = "/control_mode",
                ArmCommands   = hasArm ? "/arm/joint_commands" : "",
                ArmEnable     = hasArm ? "/arm/enable" : "",
                EmergencyStop = "/emergency_stop",
                IkTargetPose  = hasArm ? "/servo_server/target_pose" : "",
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
                    p.Joints = CreateSo101Joints();
                    p.ArmDof = 6;
                    p.MaxLinVel = 0.2f;
                    p.MaxAngVel = 1.0f;
                    p.ArmBaseHeight = RobotDescriptor.So101.BaseHeight;
                    p.ArmMaxReach = RobotDescriptor.So101.Reach;
                    p.ArmLinkLengths = (float[])RobotDescriptor.So101.LinkLengths.Clone();
                    p.IsDualArm = false;
                    // OmniBot publishes arm commands + enables on the arm_ topics.
                    p.Topics.ArmCommands = "/arm/joint_commands";
                    p.Topics.ArmEnable = "/arm/enable";
                    p.Topics.JointStates = "/arm/joint_states";
                    break;
                case "so101-arm":
                    p.Joints = CreateSo101Joints();
                    p.ArmDof = 6;
                    p.ArmBaseHeight = RobotDescriptor.So101.BaseHeight;
                    p.ArmMaxReach = RobotDescriptor.So101.Reach;
                    p.ArmLinkLengths = (float[])RobotDescriptor.So101.LinkLengths.Clone();
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
    }
}
