using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Control.Manip;
using OmniBot.VR.Core;
using OmniBot.VR.Descriptor;
using OmniBot.VR.Recording;
using OmniBot.VR.UI;
using OmniBot.VR.Video;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class RobotDescriptorProfileTests
    {
        private static string Fixture(string name)
        {
            return Path.Combine(Application.dataPath, "Scripts", "Editor", "Tests", "Fixtures", name);
        }

        private static RobotDescriptor Load(string fileName)
        {
            string json = File.ReadAllText(Fixture(fileName));
            Assert.IsTrue(RobotDescriptor.TryParse(json, out var descriptor, out var error), error);
            return descriptor;
        }

        [Test]
        public void TryParse_GoldenOmniBot_AcceptsDocument()
        {
            var descriptor = Load("omnibot.descriptor.json");
            Assert.AreEqual("1.0.0", descriptor.V);
            Assert.AreEqual("omnibot", descriptor.Id);
            Assert.AreEqual("OmniBot", descriptor.Model);
            Assert.AreEqual("mecanum", descriptor.Drive.Kind);
            Assert.IsTrue(descriptor.Drive.Holonomic);
            Assert.AreEqual(0.2, descriptor.Safety.Hard.MaxVxMps, 1e-9);
            Assert.AreEqual(0.15, descriptor.Safety.Hard.MaxJointStepRad, 1e-9);
            Assert.AreEqual(6, descriptor.Arms[0].Joints.Length);
            Assert.AreEqual(4, descriptor.Cameras.Length);
        }

        [Test]
        public void TryParse_RosStringWrapper_AcceptsLatchedMessage()
        {
            string document = File.ReadAllText(Fixture("omnibot.descriptor.json"));
            string message = new JObject { ["data"] = document }.ToString();
            Assert.IsTrue(RobotDescriptor.TryParse(message, out var descriptor, out var error), error);
            Assert.AreEqual("omnibot", descriptor.Id);
            Assert.AreEqual("/cmd_vel", descriptor.Topics.Cmd);
        }

        [Test]
        public void TryParse_UnknownField_IsRejected()
        {
            var obj = JObject.Parse(File.ReadAllText(Fixture("omnibot.descriptor.json")));
            obj["extra"] = 1;
            Assert.IsFalse(RobotDescriptor.TryParse(obj.ToString(), out _, out _));
        }

        [Test]
        public void TryParse_CapAboveCeiling_IsRejected()
        {
            var obj = JObject.Parse(File.ReadAllText(Fixture("omnibot.descriptor.json")));
            obj["safety"]["hard"]["max_vx_mps"] = 0.3;
            Assert.IsFalse(RobotDescriptor.TryParse(obj.ToString(), out _, out _));
        }

        [Test]
        public void FromDescriptor_GoldenOmniBot_UsesDescriptorTopicsAndCaps()
        {
            var profile = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"));
            Assert.IsNotNull(profile);
            Assert.IsTrue(profile.CapsFromDescriptor);
            Assert.AreEqual("omnibot", profile.RobotId);
            Assert.AreEqual("OmniBot", profile.Name);
            Assert.AreEqual(DriveKind.Mecanum, profile.Drive);
            Assert.IsTrue(profile.Holonomic);
            Assert.AreEqual(6, profile.ArmDof);
            Assert.AreEqual("arm_shoulder_pan", profile.Joints[0].Name);
            Assert.AreEqual(-3.14f, profile.Joints[0].Min, 0.001f);
            Assert.AreEqual(3.14f, profile.Joints[0].Max, 0.001f);
            Assert.AreEqual("arm_gripper", profile.Joints[5].Name);
            Assert.AreEqual(-0.1f, profile.Joints[5].Min, 0.001f);
            Assert.AreEqual(0.8f, profile.Joints[5].Max, 0.001f);

            Assert.AreEqual(0.2f, profile.MaxLinVel, 0.0001f);
            Assert.AreEqual(0.2f, profile.MaxLatVel, 0.0001f);
            Assert.AreEqual(1.0f, profile.MaxAngVel, 0.0001f);
            Assert.LessOrEqual(profile.HardMaxLinVel, RobotConfig.MAX_LINEAR_VEL);
            Assert.LessOrEqual(profile.HardMaxAngVel, RobotConfig.MAX_ANGULAR_VEL);
            Assert.AreEqual(0.15f, profile.HardMaxJointStep, 0.0001f);
            Assert.AreEqual(0.5f, profile.HardMaxAccelLin, 0.0001f);

            Assert.AreEqual("/cmd_vel", profile.Topics.CmdVel);
            Assert.AreEqual("/odom", profile.Topics.Odom);
            Assert.AreEqual("/emergency_stop", profile.Topics.EmergencyStop);
            Assert.AreEqual("/ohho/heartbeat", profile.Topics.Heartbeat);
            Assert.AreEqual("/ohho/telemetry", profile.Topics.Telemetry);
            CollectionAssert.AreEqual(new[]
            {
                "/camera/front/image_raw",
                "/camera/wrist/image_raw",
                "/camera/depth/image_raw",
                "/camera/bev/image_raw",
            }, profile.Topics.Images);
        }

        [Test]
        public void FromDescriptor_Null_ReturnsCatalogFallback()
        {
            var catalog = new RobotProfile
            {
                RobotId = "offline",
                Name = "Offline",
                MaxLinVel = 0.2f,
                Topics = new RobotTopics { CmdVel = "/cmd_vel/teleop" },
            };

            Assert.AreSame(catalog, RobotProfileFactory.FromDescriptor(null, catalog));
        }

        [Test]
        public void FromDescriptor_MissingHardCaps_ReturnsCatalogFallback()
        {
            var catalog = new RobotProfile { RobotId = "offline", MaxLinVel = 0.2f };
            var broken = new RobotDescriptor { Id = "omnibot", Model = "OmniBot" };
            Assert.AreSame(catalog, RobotProfileFactory.FromDescriptor(broken, catalog));
        }

        [Test]
        public void FromDescriptor_SameRobot_KeepsCatalogArmTopicsAndDescriptorCommand()
        {
            var descriptor = Load("omnibot.descriptor.json");
            var catalog = new RobotProfile
            {
                RobotId = "omnibot-pro",
                MaxLinVel = 0.2f,
                HardMaxLinVel = 0.2f,
                MaxLatVel = 0.2f,
                HardMaxLatVel = 0.2f,
                MaxAngVel = 1.0f,
                HardMaxAngVel = 1.0f,
                Topics = new RobotTopics
                {
                    CmdVel = "/cmd_vel/teleop",
                    ArmCommands = "/arm/joint_commands",
                    JointStates = "/arm/joint_states",
                    ControlMode = "/control_mode",
                    RecordStart = "/vr/record_start",
                    RecordStop = "/vr/record_stop",
                },
            };

            var profile = RobotProfileFactory.FromDescriptor(descriptor, catalog);
            Assert.AreEqual("/cmd_vel", profile.Topics.CmdVel);
            Assert.AreEqual("/odom", profile.Topics.Odom);
            Assert.AreEqual("/arm/joint_commands", profile.Topics.ArmCommands);
            Assert.AreEqual("/arm/joint_states", profile.Topics.JointStates);
            Assert.AreEqual("/vr/record_start", profile.Topics.RecordStart);
        }

        [Test]
        public void FromDescriptor_OtherRobot_DoesNotCopyCatalogCommandTopics()
        {
            var catalog = new RobotProfile
            {
                RobotId = "ur5e",
                Topics = new RobotTopics { ArmCommands = "/arm/joint_commands", CmdVel = "/cmd_vel/teleop" },
            };

            var profile = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"), catalog);
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.ArmCommands));
            Assert.AreEqual("/cmd_vel", profile.Topics.CmdVel);
        }

        [Test]
        public void FromDescriptor_TighterCatalogCap_IsNotRaised()
        {
            var catalog = new RobotProfile
            {
                RobotId = "omnibot",
                MaxLinVel = 0.12f,
                HardMaxLinVel = 0.12f,
                MaxAngVel = 0.5f,
                HardMaxAngVel = 0.5f,
            };

            var profile = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"), catalog);
            Assert.AreEqual(0.12f, profile.MaxLinVel, 0.0001f);
            Assert.AreEqual(0.5f, profile.MaxAngVel, 0.0001f);
        }

        [Test]
        public void FromDescriptor_MutatedHighCaps_StayInsideCeilings()
        {
            var descriptor = Load("omnibot.descriptor.json");
            descriptor.Drive.Caps.VxMps = 5;
            descriptor.Drive.Caps.VyMps = 5;
            descriptor.Drive.Caps.WzRadS = 9;
            descriptor.Safety.Hard.MaxVxMps = 5;
            descriptor.Safety.Hard.MaxVyMps = 5;
            descriptor.Safety.Hard.MaxWzRadS = 9;

            var profile = RobotProfileFactory.FromDescriptor(descriptor);
            Assert.LessOrEqual(profile.MaxLinVel, 0.2f);
            Assert.LessOrEqual(profile.MaxLatVel, 0.2f);
            Assert.LessOrEqual(profile.MaxAngVel, 1.0f);
        }

        [Test]
        public void FromDescriptor_FixedBase_DoesNotCommandTheBase()
        {
            var descriptor = Load("omnibot.descriptor.json");
            descriptor.Drive.Kind = "fixed-base";
            var profile = RobotProfileFactory.FromDescriptor(descriptor);
            Assert.AreEqual(DriveKind.FixedBase, profile.Drive);
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.CmdVel));
            Assert.AreEqual(0f, profile.MaxLinVel);
            Assert.AreEqual(0f, profile.MaxAngVel);
            // Non-zero drive caps mean this is not arm-only. Do not retarget
            // the base command topic onto the arm.
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.ArmCommands));
        }

        [Test]
        public void FromDescriptor_So101ArmOnly_ProfileHudSchemeAndRecorderComeFromTheDocument()
        {
            var descriptor = Load("so101_arm_only.descriptor.json");
            var profile = RobotProfileFactory.FromDescriptor(descriptor);

            Assert.IsNotNull(profile);
            Assert.IsTrue(profile.CapsFromDescriptor);
            Assert.AreEqual("so101-arm", profile.RobotId);
            Assert.AreEqual("SO-101", profile.Name);
            Assert.AreEqual(DriveKind.FixedBase, profile.Drive);
            Assert.IsFalse(profile.Holonomic);
            Assert.AreEqual(0, profile.BaseDof);
            Assert.IsTrue(profile.IsStationary);
            Assert.IsFalse(profile.CanNavigate);
            Assert.AreEqual(0f, profile.MaxLinVel, 0.0001f);
            Assert.AreEqual(0f, profile.MaxLatVel, 0.0001f);
            Assert.AreEqual(0f, profile.MaxAngVel, 0.0001f);
            Assert.AreEqual(0.15f, profile.HardMaxJointStep, 0.0001f);
            Assert.LessOrEqual(profile.HardMaxLinVel, RobotConfig.MAX_LINEAR_VEL);

            Assert.IsTrue(profile.HasArm);
            Assert.AreEqual(6, profile.ArmDof);
            Assert.AreEqual("arm_shoulder_pan", profile.Joints[0].Name);
            Assert.AreEqual("arm_gripper", profile.Joints[5].Name);
            Assert.AreEqual(-0.1f, profile.Joints[5].Min, 0.001f);
            Assert.AreEqual(0.8f, profile.Joints[5].Max, 0.001f);
            Assert.AreEqual(0.117f, profile.ArmLinkLengths[0], 0.0001f);
            Assert.AreEqual(0.133f, profile.ArmLinkLengths[1], 0.0001f);
            Assert.AreEqual(0.080f, profile.ArmLinkLengths[2], 0.0001f);

            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.CmdVel));
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.Odom));
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.ControlMode));
            Assert.AreEqual("/arm/joint_commands", profile.Topics.ArmCommands);
            Assert.AreEqual("/arm/joint_states", profile.Topics.JointStates);
            Assert.AreEqual("/emergency_stop", profile.Topics.EmergencyStop);
            Assert.AreEqual(descriptor.Topics.Cmd, profile.Topics.ArmCommands);
            Assert.AreEqual(descriptor.Topics.State, profile.Topics.JointStates);
            CollectionAssert.AreEqual(descriptor.Topics.Images, profile.Topics.Images);
            CollectionAssert.AreEqual(
                new[] { "/camera/wrist/image_raw" },
                CameraFeedController.TopicsFor(profile));

            CollectionAssert.AreEqual(
                new[] { TeleopHudPlan.Arm, TeleopHudPlan.Gripper },
                TeleopHudPlan.Rows(profile));
            Assert.IsFalse(TeleopHudPlan.Has(TeleopHudPlan.Rows(profile), TeleopHudPlan.Pose));
            Assert.IsFalse(TeleopHudPlan.Has(TeleopHudPlan.Rows(profile), TeleopHudPlan.Velocity));
            Assert.IsFalse(TeleopHudPlan.Has(TeleopHudPlan.Rows(profile), TeleopHudPlan.Mode));

            Assert.IsNull(ControlSchemeFactory.CreateDriveScheme(profile));
            var manip = ControlSchemeFactory.CreateManipulationScheme(profile, null);
            Assert.IsInstanceOf<HandIK6DofScheme>(manip);
            Assert.IsTrue(manip.HudHint.Contains("gripper"));

            var recorded = RecorderTopicSet.FromProfile(profile);
            Assert.AreEqual("/arm/joint_states", recorded.JointStates);
            Assert.AreEqual("/arm/joint_commands", recorded.ArmCommands);
            Assert.AreEqual("", recorded.Odom);
            Assert.AreEqual("", recorded.CmdVel);
            Assert.IsFalse(recorded.CmdVel.Contains("cmd_vel"));
            Assert.IsFalse(recorded.Odom.Contains("odom"));
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.RecordStart));
        }

        [Test]
        public void FromDescriptor_So101ArmOnly_DoesNotReplaceArmTopicsWithTheCatalog()
        {
            var catalog = new RobotProfile
            {
                RobotId = "so101-arm",
                MaxLinVel = 0.2f,
                HardMaxLinVel = 0.2f,
                Topics = new RobotTopics
                {
                    CmdVel = "/cmd_vel/teleop",
                    ArmCommands = "/catalog/arm",
                    JointStates = "/joint_states",
                    ControlMode = "/control_mode",
                    ControlModeActive = "/control_mode/active",
                    RecordStart = "/vr/record_start",
                    RecordStop = "/vr/record_stop",
                    ArmEnable = "/arm/enable",
                },
            };

            var profile = RobotProfileFactory.FromDescriptor(Load("so101_arm_only.descriptor.json"), catalog);
            Assert.AreEqual("/arm/joint_commands", profile.Topics.ArmCommands);
            Assert.AreEqual("/arm/joint_states", profile.Topics.JointStates);
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.CmdVel));
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.ControlMode));
            Assert.AreEqual("/vr/record_start", profile.Topics.RecordStart);
            Assert.AreEqual("/arm/enable", profile.Topics.ArmEnable);
            Assert.AreEqual(0f, profile.MaxLinVel, 0.0001f);
        }

        [Test]
        public void Calibration_CannotRaiseArmOnlySpeed()
        {
            var profile = RobotProfileFactory.FromDescriptor(Load("so101_arm_only.descriptor.json"));
            CalibrationManager.ApplyTo(profile, new RobotCalibration
            {
                maxLinVelOverride = 0.5f,
                maxAngVelOverride = 2.0f,
            });
            Assert.AreEqual(0f, profile.MaxLinVel, 0.0001f);
            Assert.AreEqual(0f, profile.MaxAngVel, 0.0001f);
            Assert.AreEqual(0f, profile.MaxLatVel, 0.0001f);
        }

        [Test]
        public void FromDescriptor_GoldenOmniBot_KeepsBaseHudDriveAndRecorder()
        {
            var profile = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"));
            Assert.AreEqual(DriveKind.Mecanum, profile.Drive);
            Assert.AreEqual(3, profile.BaseDof);
            Assert.AreEqual("/cmd_vel", profile.Topics.CmdVel);
            Assert.AreEqual("/odom", profile.Topics.Odom);
            Assert.IsTrue(string.IsNullOrEmpty(profile.Topics.ArmCommands));

            var rows = TeleopHudPlan.Rows(profile);
            Assert.IsTrue(TeleopHudPlan.Has(rows, TeleopHudPlan.Pose));
            Assert.IsTrue(TeleopHudPlan.Has(rows, TeleopHudPlan.Velocity));
            Assert.IsTrue(TeleopHudPlan.Has(rows, TeleopHudPlan.Mode));
            Assert.IsTrue(TeleopHudPlan.Has(rows, TeleopHudPlan.Arm));

            var drive = ControlSchemeFactory.CreateDriveScheme(profile);
            Assert.IsInstanceOf<MecanumDriveScheme>(drive);

            var recorded = RecorderTopicSet.FromProfile(profile);
            Assert.AreEqual("/odom", recorded.Odom);
            Assert.AreEqual("/cmd_vel", recorded.CmdVel);
        }

        [Test]
        public void FromDescriptor_NoArm_HasNoJointsAndNoStrafe()
        {
            var profile = RobotProfileFactory.FromDescriptor(Load("no_arm.descriptor.json"));
            Assert.IsFalse(profile.HasArm);
            Assert.AreEqual(0, profile.ArmDof);
            Assert.AreEqual(DriveKind.Differential, profile.Drive);
            Assert.AreEqual(0f, profile.MaxLatVel);
            Assert.AreEqual("/cmd_vel", profile.Topics.CmdVel);
            Assert.AreEqual(1, profile.Topics.Images.Count);
        }

        [Test]
        public void Calibration_CannotRaiseDescriptorSpeed_AndCanLowerIt()
        {
            var raised = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"));
            CalibrationManager.ApplyTo(raised, new RobotCalibration
            {
                maxLinVelOverride = 0.5f,
                maxAngVelOverride = 2.0f,
            });
            Assert.AreEqual(0.2f, raised.MaxLinVel, 0.0001f);
            Assert.AreEqual(1.0f, raised.MaxAngVel, 0.0001f);
            Assert.LessOrEqual(raised.MaxLatVel, 0.2f);

            var lowered = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"));
            CalibrationManager.ApplyTo(lowered, new RobotCalibration
            {
                maxLinVelOverride = 0.05f,
                maxAngVelOverride = 0.25f,
            });
            Assert.AreEqual(0.05f, lowered.MaxLinVel, 0.0001f);
            Assert.AreEqual(0.25f, lowered.MaxAngVel, 0.0001f);
            Assert.AreEqual(0.05f, lowered.MaxLatVel, 0.0001f);
        }

        [Test]
        public void Calibration_CannotWidenDescriptorJoints_AndCanTightenThem()
        {
            var profile = RobotProfileFactory.FromDescriptor(Load("omnibot.descriptor.json"));
            float hardMin = profile.HardJoints[0].Min;
            float hardMax = profile.HardJoints[0].Max;

            CalibrationManager.ApplyTo(profile, new RobotCalibration
            {
                jointMinOverride = new[] { -4f },
                jointMaxOverride = new[] { 4f },
            });
            Assert.AreEqual(hardMin, profile.Joints[0].Min, 0.0001f);
            Assert.AreEqual(hardMax, profile.Joints[0].Max, 0.0001f);

            CalibrationManager.ApplyTo(profile, new RobotCalibration
            {
                jointMinOverride = new[] { -1f },
                jointMaxOverride = new[] { 1f },
            });
            Assert.AreEqual(-1f, profile.Joints[0].Min, 0.0001f);
            Assert.AreEqual(1f, profile.Joints[0].Max, 0.0001f);
            Assert.GreaterOrEqual(profile.Joints[0].Min, hardMin);
            Assert.LessOrEqual(profile.Joints[0].Max, hardMax);
        }

        [Test]
        public void SafetyCaps_ClampSpeedAndJointStep()
        {
            Assert.AreEqual(0.2f, SafetyCaps.LimitAxis(0f, 1f, 0.2f, 0f, 0.05f), 0.0001f);
            Assert.AreEqual(0.025f, SafetyCaps.LimitAxis(0f, 0.2f, 0.2f, 0.5f, 0.05f), 0.0001f);
            Assert.AreEqual(0f, SafetyCaps.LimitAxis(0.2f, 0f, 0.2f, 0.5f, 0.05f), 0.0001f);
            Assert.AreEqual(0.15f, SafetyCaps.ClampJoint(0f, 1f, -3f, 3f, 0.15f), 0.0001f);
            Assert.AreEqual(1f, SafetyCaps.ClampJoint(0.9f, 2f, -1f, 1f, 0.15f), 0.0001f);
            Assert.LessOrEqual(SafetyCaps.JointStepLimit(null), 0.15f);
        }

        [Test]
        public void NoCodeReferencesRobotConfigTopicConstants()
        {
            string needle = "RobotConfig." + "TOPIC_";
            string root = Directory.GetParent(Application.dataPath).FullName;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Packages/") || normalized.Contains("/Library/") ||
                    normalized.Contains("/Temp/") || normalized.Contains("/Logs/") ||
                    normalized.Contains("/obj/"))
                    continue;

                string text = File.ReadAllText(file);
                Assert.IsFalse(text.Contains(needle), file);
            }

            foreach (var field in typeof(RobotConfig).GetFields(BindingFlags.Public | BindingFlags.Static))
                Assert.IsFalse(field.Name.StartsWith("TOPIC_"), field.Name);
        }
    }
}
