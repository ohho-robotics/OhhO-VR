using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Twin;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// EditMode coverage for the URDF twin: joint-name mapping, revolute /
    /// continuous / prismatic motion, 1:1 and 0.25 scale, and stale samples.
    /// </summary>
    public class TwinAnimatorTests
    {
        const string FixtureUrdf =
            "<robot name=\"fixture\">" +
            "<link name=\"base\"/>" +
            "<link name=\"slider\"/>" +
            "<joint name=\"slide\" type=\"prismatic\">" +
            "<parent link=\"base\"/><child link=\"slider\"/>" +
            "<origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>" +
            "<axis xyz=\"1 0 0\"/>" +
            "<limit lower=\"-0.01\" upper=\"0.04\" effort=\"1\" velocity=\"1\"/>" +
            "</joint>" +
            "<link name=\"hinge_link\"/>" +
            "<joint name=\"hinge\" type=\"revolute\">" +
            "<parent link=\"base\"/><child link=\"hinge_link\"/>" +
            "<origin xyz=\"0 0 0.1\" rpy=\"0 0 0\"/>" +
            "<axis xyz=\"0 0 1\"/>" +
            "<limit lower=\"-1\" upper=\"1\" effort=\"1\" velocity=\"1\"/>" +
            "</joint>" +
            "<link name=\"spin_link\"/>" +
            "<joint name=\"spin\" type=\"continuous\">" +
            "<parent link=\"base\"/><child link=\"spin_link\"/>" +
            "<axis xyz=\"0 1 0\"/>" +
            "</joint>" +
            "<link name=\"sensor\"/>" +
            "<joint name=\"mount\" type=\"fixed\">" +
            "<parent link=\"base\"/><child link=\"sensor\"/>" +
            "<origin xyz=\"0 0 0.2\" rpy=\"0 0 0\"/>" +
            "</joint>" +
            "</robot>";

        [Test]
        public void RosPoint_MapsForwardLeftUpOntoUnity()
        {
            TwinPose pose = TwinMath.ToUnity(new RosTransform(1f, 2f, 3f, 0f, 0f, 0f, 1f));
            Assert.AreEqual(-2f, pose.X, 1e-5f);
            Assert.AreEqual(3f, pose.Y, 1e-5f);
            Assert.AreEqual(1f, pose.Z, 1e-5f);
            Assert.AreEqual(1f, pose.Qw, 1e-5f);
        }

        [Test]
        public void PositiveYawAboutZ_BecomesNegativeUnityYaw()
        {
            float s = (float)Math.Sin(Math.PI / 4.0);
            float c = (float)Math.Cos(Math.PI / 4.0);
            RosTransform ros = TwinMath.FromAxisAngle(0f, 0f, 1f, (float)(Math.PI / 2.0));
            TwinPose unity = TwinMath.ToUnity(ros);
            Assert.AreEqual(0f, unity.Qx, 1e-5f);
            Assert.AreEqual(-s, unity.Qy, 1e-5f);
            Assert.AreEqual(0f, unity.Qz, 1e-5f);
            Assert.AreEqual(c, unity.Qw, 1e-5f);
        }

        [Test]
        public void JointMap_AliasDrivesUrdfName_UnknownIsIgnored()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "jaw", "nope" }, new[] { 1f, 9f }), 1d);
            Assert.AreEqual(0.04f, animator.GetJointPosition("slide", false), 1e-5f);
            Assert.AreEqual(0f, animator.GetJointPosition("nope", false), 1e-5f);
            TwinPose slider;
            Assert.IsTrue(animator.TryGetLinkLocalPose("slider", false, out slider));
            Assert.AreEqual(0.04f, slider.Z, 1e-4f);
            Assert.AreEqual(0f, slider.X, 1e-4f);
            Assert.AreEqual(0f, slider.Y, 1e-4f);
        }

        [Test]
        public void Revolute_ClampsToLimits()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "hinge" }, new[] { 5f }), 1d);
            Assert.AreEqual(1f, animator.GetJointPosition("hinge", false), 1e-5f);
            TwinPose pose;
            Assert.IsTrue(animator.TryGetLinkLocalPose("hinge_link", false, out pose));
            Assert.AreEqual(0.1f, pose.Y, 1e-4f);
        }

        [Test]
        public void Continuous_IsNotClamped()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "spin" }, new[] { 10f }), 1d);
            Assert.AreEqual(10f, animator.GetJointPosition("spin", false), 1e-5f);
        }

        [Test]
        public void Prismatic_ClampsAndTranslatesAlongAxis()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "slide" }, new[] { -2f }), 1d);
            Assert.AreEqual(-0.01f, animator.GetJointPosition("slide", false), 1e-5f);
            TwinPose pose;
            animator.TryGetLinkLocalPose("slider", false, out pose);
            Assert.AreEqual(-0.01f, pose.Z, 1e-4f);
        }

        [Test]
        public void FixedJoint_IgnoresCommandAndKeepsOrigin()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "mount" }, new[] { 5f }), 1d);
            Assert.AreEqual(0f, animator.GetJointPosition("mount", false), 1e-5f);
            TwinPose pose;
            Assert.IsTrue(animator.TryGetLinkLocalPose("sensor", false, out pose));
            Assert.AreEqual(0.2f, pose.Y, 1e-4f);
        }

        [Test]
        public void ScaleModes_AreOneAndQuarter_AnglesUnchanged()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "hinge" }, new[] { 0.5f }), 1d);
            animator.ApplyOdometry(Odom(1f, 0f, 0f, 1, 0, "base", "odom"), 1d);

            animator.SetScale(TwinScaleMode.OneToOne);
            Assert.AreEqual(1f, animator.ScaleFactor, 1e-6f);
            TwinPose full;
            animator.TryGetLinkLocalPose("base", false, out full);
            TwinPose fullScaled = animator.GetScaledLinkLocalPose("base", false);
            Assert.AreEqual(full.Z, fullScaled.Z, 1e-5f);
            Assert.AreEqual(1f, full.Z, 1e-4f);

            animator.SetScale(TwinScaleMode.Tabletop);
            Assert.AreEqual(TwinScale.TabletopFactor, animator.ScaleFactor, 1e-6f);
            Assert.AreEqual(0.25f, animator.ScaleFactor, 1e-6f);
            Assert.AreEqual(0.5f, animator.GetJointPosition("hinge", false), 1e-5f);
            TwinPose table = animator.GetScaledLinkLocalPose("base", false);
            Assert.AreEqual(0.25f, table.Z, 1e-4f);
            TwinPose hinge = animator.GetScaledLinkLocalPose("hinge_link", false);
            Assert.AreEqual(0.025f, hinge.Y, 1e-4f);
        }

        [Test]
        public void StaleSamples_HoldTheLastPose()
        {
            TwinAnimator animator = FixtureAnimator();
            Assert.IsTrue(animator.AreJointsStale);
            Assert.IsTrue(animator.IsBaseStale);
            Assert.IsFalse(animator.GhostVisible);

            animator.ApplyJointStates(new JointStateMsg(new[] { "spin" }, new[] { 1.25f }), 10d);
            animator.ApplyJointCommands(new JointStateMsg(new[] { "hinge" }, new[] { 0.4f }), 10d);
            animator.ApplyOdometry(Odom(2f, 0f, 0f, 2, 0, "base", "odom"), 10d);
            animator.Tick(11d);
            Assert.IsFalse(animator.AreJointsStale);
            Assert.IsFalse(animator.IsBaseStale);
            Assert.IsTrue(animator.GhostVisible);

            animator.Tick(11.01d);
            Assert.IsTrue(animator.AreJointsStale);
            Assert.IsTrue(animator.IsBaseStale);
            Assert.IsTrue(animator.IsGhostStale);
            Assert.IsFalse(animator.GhostVisible);
            Assert.AreEqual(1.25f, animator.GetJointPosition("spin", false), 1e-5f);
            Assert.AreEqual(0.4f, animator.GetJointPosition("hinge", true), 1e-5f);
            TwinPose held;
            animator.TryGetLinkLocalPose("base", false, out held);
            Assert.AreEqual(2f, held.Z, 1e-4f);

            animator.ApplyJointStates(new JointStateMsg(new[] { "spin" }, new[] { 0.5f }), 12d);
            animator.Tick(12d);
            Assert.IsFalse(animator.AreJointsStale);
            Assert.IsTrue(animator.IsBaseStale);
            Assert.AreEqual(0.5f, animator.GetJointPosition("spin", false), 1e-5f);
        }

        [Test]
        public void Ghost_UsesCommand_MeasuredStaysPut()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyJointStates(new JointStateMsg(new[] { "hinge", "slide" }, new[] { 0.2f, 0.02f }), 1d);
            animator.ApplyJointCommands(new JointStateMsg(new[] { "hinge" }, new[] { 0.7f }), 1d);
            Assert.AreEqual(0.2f, animator.GetJointPosition("hinge", false), 1e-5f);
            Assert.AreEqual(0.7f, animator.GetJointPosition("hinge", true), 1e-5f);
            Assert.AreEqual(0.02f, animator.GetJointPosition("slide", true), 1e-5f);
            Assert.IsTrue(animator.GhostVisible);
        }

        [Test]
        public void OdomAndTf_NewerStampWins_WrongFrameIgnored()
        {
            TwinAnimator animator = FixtureAnimator();
            animator.ApplyOdometry(Odom(1f, 0f, 0f, 1, 500000000, "base", "odom"), 5d);
            animator.ApplyOdometry(Odom(9f, 0f, 0f, 1, 400000000, "base", "odom"), 6d);
            TwinPose pose;
            animator.TryGetLinkLocalPose("base", false, out pose);
            Assert.AreEqual(1f, pose.Z, 1e-4f);

            animator.ApplyTf(Tf(4f, 1, 600000000, "base", "odom"), 7d);
            animator.TryGetLinkLocalPose("base", false, out pose);
            Assert.AreEqual(4f, pose.Z, 1e-4f);

            animator.ApplyTf(Tf(8f, 3, 0, "base_link", "odom"), 8d);
            animator.ApplyOdometry(Odom(7f, 0f, 0f, 3, 0, "base", "map"), 8d);
            animator.TryGetLinkLocalPose("base", false, out pose);
            Assert.AreEqual(4f, pose.Z, 1e-4f);
        }

        [Test]
        public void Subscriber_IsDisplayOnly_AndUsesProfileTopics()
        {
            var link = new RecordingLink();
            var profile = new TwinProfile
            {
                BaseFrame = "base",
                OdomFrame = "odom",
                StaleTimeoutSeconds = 5f,
                Topics = new TwinTopics
                {
                    Odom = "/custom/odom",
                    Tf = "/custom/tf",
                    JointStates = new[] { "/custom/joints" },
                    JointCommands = new[] { "/custom/commands" },
                },
                JointMap = new List<TwinJointAlias> { new TwinJointAlias("jaw", "slide") },
            };
            var animator = new TwinAnimator(UrdfParser.Parse(FixtureUrdf), profile);
            double now = 3d;
            var subscriber = new TwinLinkSubscriber();
            subscriber.Attach(link, profile, animator, () => now);

            CollectionAssert.AreEqual(
                new[] { "/custom/odom", "/custom/tf", "/custom/joints", "/custom/commands" },
                link.Topics);
            Assert.AreEqual(0, link.Publishes);
            Assert.AreEqual(0, link.Advertises);
            Assert.AreEqual(0, link.Unsubscribes);

            link.Callbacks[2]("{\"name\":[\"jaw\"],\"position\":[0.02]}");
            Assert.AreEqual(0.02f, animator.GetJointPosition("slide", false), 1e-5f);
            link.Callbacks[2]("not-json");
            Assert.AreEqual(0.02f, animator.GetJointPosition("slide", false), 1e-5f);
            Assert.AreEqual(0, link.Publishes);

            subscriber.Detach();
            link.Callbacks[2]("{\"name\":[\"jaw\"],\"position\":[0.03]}");
            Assert.AreEqual(0.02f, animator.GetJointPosition("slide", false), 1e-5f);
            Assert.AreEqual(0, link.Unsubscribes);
            Assert.IsFalse(subscriber.IsAttached);
        }

        [Test]
        public void TopicLiterals_ExistOnlyOnTheProfile()
        {
            string[] banned =
            {
                "\"/odom\"",
                "\"/tf\"",
                "\"/arm/joint_states\"",
                "\"/joint_states\"",
                "\"/arm/joint_commands\"",
            };
            string directory = Path.Combine(Application.dataPath, "Twin", "Scripts");
            foreach (string file in Directory.GetFiles(directory, "*.cs"))
            {
                if (Path.GetFileName(file) == "TwinProfiles.cs")
                    continue;
                string text = File.ReadAllText(file);
                for (int i = 0; i < banned.Length; i++)
                    StringAssert.DoesNotContain(banned[i], text, Path.GetFileName(file));
            }
        }

        [Test]
        public void TwinScripts_DoNotPublishOrUnsubscribe()
        {
            string[] files =
            {
                Path.Combine(Application.dataPath, "Twin", "Scripts", "TwinLinkSubscriber.cs"),
                Path.Combine(Application.dataPath, "Twin", "Scripts", "TwinView.cs"),
                Path.Combine(Application.dataPath, "Twin", "Scripts", "TwinAnimator.cs"),
            };
            for (int i = 0; i < files.Length; i++)
            {
                string text = File.ReadAllText(files[i]);
                StringAssert.DoesNotContain("Publish(", text, files[i]);
                StringAssert.DoesNotContain("Advertise(", text, files[i]);
                StringAssert.DoesNotContain("Unsubscribe(", text, files[i]);
            }
        }

        [Test]
        public void ForRobot_CopiesTopics_OmniBotKeepsWheelTopic()
        {
            var custom = new RobotProfile
            {
                RobotId = "custom-bot",
                Topics = new RobotTopics
                {
                    Odom = "/custom/odom",
                    JointStates = "/custom/joints",
                    ArmCommands = "/custom/commands",
                },
                Joints = new[] { new JointSpec("elbow", "Elbow", -1f, 1f, 0f) },
            };
            TwinProfile generic = TwinProfiles.ForRobot(custom, "https://example.com/robot.urdf");
            Assert.AreEqual("https://example.com/robot.urdf", generic.UrdfPath);
            Assert.AreEqual("/custom/odom", generic.Topics.Odom);
            Assert.AreEqual("/tf", generic.Topics.Tf);
            Assert.AreEqual("/custom/joints", generic.Topics.JointStates[0]);
            Assert.AreEqual("/custom/commands", generic.Topics.JointCommands[0]);
            Assert.AreEqual("elbow", generic.ResolveJointName("elbow"));

            var omni = new RobotProfile
            {
                RobotId = "omnibot",
                Topics = new RobotTopics { Odom = "/nope", JointStates = "/nope", ArmCommands = "/nope" },
            };
            TwinProfile flagship = TwinProfiles.ForRobot(omni, null);
            Assert.AreEqual("/odom", flagship.Topics.Odom);
            Assert.AreEqual("/arm/joint_states", flagship.Topics.JointStates[0]);
            Assert.AreEqual("/joint_states", flagship.Topics.JointStates[1]);
            Assert.AreEqual("/arm/joint_commands", flagship.Topics.JointCommands[0]);
            Assert.AreEqual("arm_gripper", flagship.ResolveJointName("arm_gripper"));
            Assert.AreEqual(TwinProfiles.OmniBotUrdfPath, flagship.UrdfPath);
        }

        [Test]
        public void ExpandedUrdf_KeepsOmniBotJointNamesAndCalibratedOrigins()
        {
            UrdfDocument document = UrdfParser.Parse(File.ReadAllText(UrdfPath()));
            Assert.AreEqual("mecanum_bot", document.Name);
            Assert.IsFalse(document.TryGetLink("lidar_link", out _));

            ExpectJoint(document, "base_footprint_joint", UrdfJointType.Fixed, 0f, 0f, 0.073f);
            ExpectJoint(document, "top_plate_joint", UrdfJointType.Fixed, 0f, 0f, 0.166f);
            ExpectJoint(document, "arm_base_joint", UrdfJointType.Fixed, 0.055f, 0f, 0.003f);
            ExpectJoint(document, "front_left_wheel_joint", UrdfJointType.Continuous, 0.0825f, 0.1075f, -0.033f);
            UrdfJoint wheelJoint;
            Assert.IsTrue(document.TryGetJoint("front_left_wheel_joint", out wheelJoint));
            Assert.IsFalse(wheelJoint.HasLimits);
            Assert.AreEqual(0f, wheelJoint.AxisX, 1e-5f);
            Assert.AreEqual(1f, wheelJoint.AxisY, 1e-5f);
            UrdfJoint pan;
            Assert.IsTrue(document.TryGetJoint("arm_shoulder_pan", out pan));
            Assert.AreEqual(UrdfJointType.Revolute, pan.Type);
            Assert.AreEqual(0.0388353f, pan.OriginX, 1e-5f);
            Assert.AreEqual(0.0624f, pan.OriginZ, 1e-5f);
            Assert.AreEqual(-1.91986f, pan.Lower, 1e-5f);
            Assert.AreEqual(1.91986f, pan.Upper, 1e-5f);

            string[] arm =
            {
                "arm_shoulder_pan", "arm_shoulder_lift", "arm_elbow_flex",
                "arm_wrist_flex", "arm_wrist_roll", "arm_gripper",
            };
            var seen = new HashSet<string>();
            int wheels = 0;
            for (int i = 0; i < document.Joints.Count; i++)
            {
                UrdfJoint joint = document.Joints[i];
                Assert.IsFalse(seen.Contains(joint.Name), joint.Name);
                seen.Add(joint.Name);
                Assert.IsTrue(document.TryGetLink(joint.Parent, out _), joint.Name);
                Assert.IsTrue(document.TryGetLink(joint.Child, out _), joint.Name);
                if (joint.Name.EndsWith("_wheel_joint", StringComparison.Ordinal))
                    wheels++;
            }
            Assert.AreEqual(4, wheels);
            for (int i = 0; i < arm.Length; i++)
            {
                UrdfJoint joint;
                Assert.IsTrue(document.TryGetJoint(arm[i], out joint), arm[i]);
                Assert.AreEqual(UrdfJointType.Revolute, joint.Type, arm[i]);
                Assert.IsTrue(joint.HasLimits, arm[i]);
            }
            UrdfJoint gripper;
            document.TryGetJoint("arm_gripper", out gripper);
            Assert.AreEqual(-0.174533f, gripper.Lower, 1e-5f);
            Assert.AreEqual(1.74533f, gripper.Upper, 1e-4f);

            var animator = new TwinAnimator(document, TwinProfiles.OmniBot());
            Assert.AreEqual("base_footprint", animator.RootLink);
            animator.ApplyJointStates(new JointStateMsg(new[] { "front_left_wheel_joint" }, new[] { 10f }), 1d);
            Assert.AreEqual(10f, animator.GetJointPosition("front_left_wheel_joint", false), 1e-4f);
            animator.ApplyJointStates(new JointStateMsg(new[] { "arm_shoulder_pan" }, new[] { 3f }), 1d);
            Assert.AreEqual(1.91986f, animator.GetJointPosition("arm_shoulder_pan", false), 1e-4f);
        }

        [Test]
        public void QuestBudgets_PrimitivesAndDecimatedMeshesStayUnderFiftyThousand()
        {
            UrdfDocument document = UrdfParser.Parse(File.ReadAllText(UrdfPath()));
            int primitives = UrdfParser.CountVisualTriangles(document);
            Assert.Greater(primitives, 200);
            Assert.Less(primitives * 2, 50000);

            string meshDir = Path.Combine(Application.dataPath, "Twin", "Meshes", "so101");
            int faces = 0;
            foreach (string obj in Directory.GetFiles(meshDir, "*.obj"))
                faces += CountFaces(obj);
            string budget = File.ReadAllText(Path.Combine(meshDir, "budget.json"));
            Match match = Regex.Match(budget, "\"totalTriangles\"\\s*:\\s*(\\d+)");
            Assert.IsTrue(match.Success);
            Assert.AreEqual(faces, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.Less(faces, 50000);
            Assert.Greater(faces, 1000);

            string attribution = File.ReadAllText(Path.Combine(meshDir, "ATTRIBUTION.md"));
            StringAssert.Contains("Apache", attribution);
            StringAssert.Contains("TheRobotStudio", attribution);
            StringAssert.Contains("SO-ARM100", attribution);
        }

        [Test]
        public void Primitives_FaceOutward_AndObjVerticesConvert()
        {
            Assert.AreEqual(TwinPrimitives.BoxTriangles, TwinPrimitives.Box().triangles.Length / 3);
            Assert.AreEqual(TwinPrimitives.CylinderTriangles, TwinPrimitives.Cylinder().triangles.Length / 3);
            Assert.AreEqual(TwinPrimitives.SphereTriangles, TwinPrimitives.Sphere().triangles.Length / 3);
            AssertOutward(TwinPrimitives.Box());
            AssertOutward(TwinPrimitives.Cylinder());
            AssertOutward(TwinPrimitives.Sphere());

            Mesh mesh = TwinPrimitives.MeshFromObj("v 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 2 3\n", 1f, 1f, 1f);
            try
            {
                Assert.AreEqual(3, mesh.vertexCount);
                Assert.AreEqual(0f, mesh.vertices[0].x, 1e-4f);
                Assert.AreEqual(0f, mesh.vertices[0].y, 1e-4f);
                Assert.AreEqual(1f, mesh.vertices[0].z, 1e-4f);
                Assert.AreEqual(1, mesh.triangles.Length / 3);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Hierarchy_UsesLinkNames_AndAppliesJointPose()
        {
            UrdfDocument document = UrdfParser.Parse(FixtureUrdf);
            var anchor = new GameObject("twin-test-anchor");
            try
            {
                TwinBuiltHierarchy built = TwinHierarchyBuilder.Build(document, anchor.transform, false, null);
                var animator = new TwinAnimator(document, Profile());
                animator.ApplyOdometry(Odom(1f, 0f, 0f, 1, 0, "base", "odom"), 1d);
                animator.ApplyJointStates(new JointStateMsg(new[] { "slide" }, new[] { 0.02f }), 1d);
                for (int i = 0; i < built.Nodes.Count; i++)
                {
                    TwinPose pose;
                    if (animator.TryGetLinkLocalPose(built.Nodes[i].LinkName, false, out pose))
                        TwinPoseApplicator.Apply(built.Nodes[i].transform, pose);
                }
                built.Root.localScale = Vector3.one * TwinScale.TabletopFactor;

                Transform root = built.Root.Find("base");
                Assert.IsNotNull(root);
                Assert.AreEqual("base", root.GetComponent<TwinLinkNode>().LinkName);
                Assert.AreEqual(1f, root.localPosition.z, 1e-4f);

                Transform slider = root.Find("slider");
                Assert.IsNotNull(slider);
                Assert.AreEqual("slide", slider.GetComponent<TwinLinkNode>().JointName);
                Assert.AreEqual(0.02f, slider.localPosition.z, 1e-4f);
                Assert.AreEqual(0.25f, slider.lossyScale.z, 1e-3f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void OmniBotHierarchy_KeepsArmAndWheelLinkNames()
        {
            UrdfDocument document = UrdfParser.Parse(File.ReadAllText(UrdfPath()));
            var anchor = new GameObject("omnibot-twin-test");
            try
            {
                TwinBuiltHierarchy built = TwinHierarchyBuilder.Build(
                    document, anchor.transform, false, Path.Combine(Application.dataPath, "Twin", "URDF"));
                Assert.IsNotNull(built.Root.Find(
                    "base_footprint/base_link/top_plate_link/arm_base_link/arm_shoulder_link"));
                Transform wheel = built.Root.Find("base_footprint/base_link/front_left_wheel_link");
                Assert.IsNotNull(wheel);
                Assert.AreEqual("front_left_wheel_joint", wheel.GetComponent<TwinLinkNode>().JointName);
                Assert.AreEqual("arm_gripper_link", built.Root.Find(
                    "base_footprint/base_link/top_plate_link/arm_base_link/arm_shoulder_link/arm_upper_arm_link/arm_lower_arm_link/arm_wrist_link/arm_gripper_link").name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }

        static TwinAnimator FixtureAnimator()
        {
            return new TwinAnimator(UrdfParser.Parse(FixtureUrdf), Profile());
        }

        static TwinProfile Profile()
        {
            return new TwinProfile
            {
                BaseFrame = "base",
                OdomFrame = "odom",
                Scale = TwinScaleMode.OneToOne,
                StaleTimeoutSeconds = 1f,
                Topics = new TwinTopics(),
                JointMap = new List<TwinJointAlias> { new TwinJointAlias("jaw", "slide") },
            };
        }

        static OdometryMsg Odom(float x, float y, float z, int secs, int nsecs, string child, string frame)
        {
            var message = new OdometryMsg();
            message.header.frame_id = frame;
            message.header.stamp.secs = secs;
            message.header.stamp.nsecs = nsecs;
            message.child_frame_id = child;
            message.pose.pose.position = new Vector3Msg(x, y, z);
            message.pose.pose.orientation = new QuaternionMsg(0f, 0f, 0f, 1f);
            return message;
        }

        static TfMessage Tf(float x, int secs, int nsecs, string child, string frame)
        {
            var header = new HeaderMsg(0, frame);
            header.stamp.secs = secs;
            header.stamp.nsecs = nsecs;
            return new TfMessage
            {
                transforms = new[]
                {
                    new TfStamped
                    {
                        header = header,
                        child_frame_id = child,
                        transform = new TfTransform
                        {
                            translation = new Vector3Msg(x, 0f, 0f),
                            rotation = new QuaternionMsg(0f, 0f, 0f, 1f),
                        },
                    },
                },
            };
        }

        static void ExpectJoint(UrdfDocument document, string name, UrdfJointType type, float x, float y, float z)
        {
            UrdfJoint joint;
            Assert.IsTrue(document.TryGetJoint(name, out joint), name);
            Assert.AreEqual(type, joint.Type, name);
            Assert.AreEqual(x, joint.OriginX, 1e-4f, name);
            Assert.AreEqual(y, joint.OriginY, 1e-4f, name);
            Assert.AreEqual(z, joint.OriginZ, 1e-4f, name);
        }

        static string UrdfPath()
        {
            return Path.Combine(Application.dataPath, "Twin", "URDF", "omnibot.urdf.xml");
        }

        static int CountFaces(string path)
        {
            int count = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("f ", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        static void AssertOutward(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int checkedTriangles = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 center = (a + b + c) / 3f;
                if (normal.sqrMagnitude < 1e-8f || center.sqrMagnitude < 1e-8f)
                    continue;
                Assert.GreaterOrEqual(Vector3.Dot(normal, center), -1e-4f);
                checkedTriangles++;
            }
            Assert.Greater(checkedTriangles, 0);
        }

        sealed class RecordingLink : IRobotLink
        {
            public readonly List<string> Topics = new List<string>();
            public readonly List<Action<string>> Callbacks = new List<Action<string>>();
            public int Publishes;
            public int Advertises;
            public int Unsubscribes;
            public bool IsConnected => true;

            public void Advertise(string topic, string type) { Advertises++; }
            public void Publish<T>(string topic, T msg) { Publishes++; }

            public void Subscribe(string topic, string type, Action<string> callback)
            {
                Topics.Add(topic);
                Callbacks.Add(callback);
            }

            public void Unsubscribe(string topic) { Unsubscribes++; }
        }
    }
}
