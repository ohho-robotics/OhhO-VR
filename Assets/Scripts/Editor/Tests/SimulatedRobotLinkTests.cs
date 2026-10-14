using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// EditMode coverage for the in-headset sim. Shared numbers come from
    /// <c>lib/connect/teleop.test.ts</c> and the 100 ms step in
    /// <c>lib/connect/simulated.ts</c>. No Quest and no robot PC.
    /// </summary>
    public class SimulatedRobotLinkTests
    {
        SimulatedRobotLink _link;

        [SetUp]
        public void SetUp()
        {
            _link = new SimulatedRobotLink();
            _link.Connect();
        }

        [TearDown]
        public void TearDown()
        {
            RobotLinkFactory.Select(RobotLinkTarget.RosBridge);
        }

        [Test]
        public void Gate_DeadmanReleased_ReturnsZero()
        {
            var gated = SimulatedTeleopGate.Apply(SimulatedVectors.DeadmanSample, deadmanHeld: false, estopLatched: false);
            AssertVelocity(SimVelocity.Zero, gated);
        }

        [Test]
        public void Gate_DeadmanHeld_ReturnsRequested()
        {
            var gated = SimulatedTeleopGate.Apply(SimulatedVectors.DeadmanSample, deadmanHeld: true, estopLatched: false);
            AssertVelocity(SimulatedVectors.DeadmanSample, gated);
        }

        [Test]
        public void Gate_EstopLatched_ReturnsZero()
        {
            var gated = SimulatedTeleopGate.Apply(SimulatedVectors.DeadmanSample, deadmanHeld: true, estopLatched: true);
            AssertVelocity(SimVelocity.Zero, gated);
        }

        [Test]
        public void ShouldPublish_MatchesBrowserEdges()
        {
            var release = SimulatedTeleopGate.ShouldPublish(false, false, false);
            Assert.IsTrue(release.Publish);
            Assert.IsTrue(release.ZeroAlreadySent);

            var quiet = SimulatedTeleopGate.ShouldPublish(false, false, true);
            Assert.IsFalse(quiet.Publish);
            Assert.IsTrue(quiet.ZeroAlreadySent);

            var live = SimulatedTeleopGate.ShouldPublish(true, false, true);
            Assert.IsTrue(live.Publish);
            Assert.IsFalse(live.ZeroAlreadySent);
        }

        [Test]
        public void SharedConstants_MatchBrowserSim()
        {
            Assert.AreEqual(0.2, SimulatedVectors.KeyLinearMps, 0);
            Assert.AreEqual(0.8, SimulatedVectors.KeyAngularRadPerSec, 0);
            Assert.AreEqual(0.87, SimulatedVectors.Battery, 0);
            Assert.AreEqual(0.1, SimulatedVectors.TickSeconds, 0);
            Assert.AreEqual(2, SimulatedVectors.ConnectedLatencyMs);
            Assert.AreEqual(20, SimulatedVectors.ConnectedMsgRateHz);
            Assert.AreEqual(0.2, SimulatedVectors.DeadmanSample.LinearX, 0);
            Assert.AreEqual(0.05, SimulatedVectors.DeadmanSample.LinearY, 0);
            Assert.AreEqual(-0.4, SimulatedVectors.DeadmanSample.AngularZ, 0);
            Assert.AreEqual(RobotConfig.MAX_LINEAR_VEL, (float)SimulatedSafetyCaps.HardMaxLinearMps);
            Assert.AreEqual(RobotConfig.MAX_ANGULAR_VEL, (float)SimulatedSafetyCaps.HardMaxAngularRadPerSec);
            Assert.AreEqual(RobotConfig.MAX_ANGULAR_VEL, (float)SimulatedSafetyCaps.HardMaxJointRateRadPerSec);
        }

        [Test]
        public void Kinematics_ForwardTicks_MatchSharedVectors()
        {
            var pose = new SimPose();
            for (int i = 0; i < SimulatedVectors.ForwardPoses.Length; i++)
            {
                pose = MecanumSimKinematics.Integrate(pose, SimulatedVectors.Forward, SimulatedVectors.TickSeconds);
                AssertPose(SimulatedVectors.ForwardPoses[i], pose);
            }
        }

        [Test]
        public void Kinematics_DeadmanSample_MatchesSharedVectors()
        {
            var pose = new SimPose();
            for (int i = 0; i < SimulatedVectors.DeadmanSamplePoses.Length; i++)
            {
                pose = MecanumSimKinematics.Integrate(pose, SimulatedVectors.DeadmanSample, SimulatedVectors.TickSeconds);
                AssertPose(SimulatedVectors.DeadmanSamplePoses[i], pose);
            }
        }

        [Test]
        public void Kinematics_StrafeAtYaw_MatchesSharedVector()
        {
            var start = new SimPose(0, 0, Math.PI / 2);
            var pose = MecanumSimKinematics.Integrate(start, new SimVelocity(0, 0.2, 0), SimulatedVectors.TickSeconds);
            AssertPose(SimulatedVectors.StrafeAtHalfPi, pose);
        }

        [Test]
        public void Kinematics_TurnStep_MatchesSharedVector()
        {
            var pose = MecanumSimKinematics.Integrate(default, new SimVelocity(0.2, 0, 1), SimulatedVectors.TickSeconds);
            AssertPose(SimulatedVectors.TurnStep, pose);
        }

        [Test]
        public void Link_WithoutDeadman_DoesNotMove()
        {
            _link.CommandVelocity(0.5, 0, 0.2);
            AssertVelocity(SimVelocity.Zero, _link.Applied);
            _link.Tick(1);
            AssertPose(default, _link.Pose);

            _link.SetDeadman(true);
            _link.Tick(1);
            AssertPose(default, _link.Pose);

            _link.CommandVelocity(SimulatedVectors.DeadmanSample.LinearX, SimulatedVectors.DeadmanSample.LinearY, SimulatedVectors.DeadmanSample.AngularZ);
            _link.Tick(SimulatedVectors.TickSeconds);
            AssertPose(SimulatedVectors.DeadmanSamplePoses[0], _link.Pose);
        }

        [Test]
        public void Link_DropDeadman_ZerosAndPublishesOneZero()
        {
            int cmdCount = 0;
            double lastX = double.NaN;
            _link.Subscribe(RobotConfig.TOPIC_CMD_VEL, "geometry_msgs/Twist", json =>
            {
                cmdCount++;
                lastX = JObject.Parse(json)["linear"]["x"].Value<double>();
                Assert.AreEqual(SimulatedRobotLink.Kind, (string)JObject.Parse(json)["kind"]);
            });

            _link.SetDeadman(true);
            _link.CommandVelocity(0.2, 0, 0);
            Assert.AreEqual(1, cmdCount);
            Assert.AreEqual(0.2, lastX, 1e-5);

            _link.Tick(SimulatedVectors.TickSeconds);
            double x = _link.Pose.X;
            Assert.Greater(x, 0);

            _link.Publish(SimulatedRobotLink.DeadmanTopic, new BoolMsg(false));
            Assert.IsFalse(_link.DeadmanHeld);
            AssertVelocity(SimVelocity.Zero, _link.Applied);
            Assert.AreEqual(2, cmdCount);
            Assert.AreEqual(0, lastX, 1e-6);

            _link.CommandVelocity(0.5, 0, 0);
            _link.Tick(1);
            Assert.AreEqual(2, cmdCount);
            Assert.AreEqual(x, _link.Pose.X, 1e-12);
        }

        [Test]
        public void Link_Estop_BlocksUntilClearedAndRepublished()
        {
            _link.SetDeadman(true);
            _link.CommandVelocity(0.2, 0, 0);
            _link.Tick(SimulatedVectors.TickSeconds);
            double x = _link.Pose.X;

            _link.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, new BoolMsg(true));
            Assert.IsTrue(_link.EstopLatched);
            AssertVelocity(SimVelocity.Zero, _link.Applied);

            _link.SetDeadman(true);
            _link.CommandVelocity(0.2, 0, 0);
            _link.Tick(1);
            Assert.AreEqual(x, _link.Pose.X, 1e-12);

            _link.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, new BoolMsg(false));
            Assert.IsFalse(_link.EstopLatched);
            _link.Tick(1);
            Assert.AreEqual(x, _link.Pose.X, 1e-12);

            _link.CommandVelocity(0.2, 0, 0);
            _link.Tick(SimulatedVectors.TickSeconds);
            Assert.Greater(_link.Pose.X, x);
        }

        [Test]
        public void Link_HardCaps_BoundOneSecondOfMotion()
        {
            var unclamped = SimulatedTeleopGate.Apply(new SimVelocity(5, -4, 3), true, false);
            Assert.AreEqual(5, unclamped.LinearX, 0);

            _link.SetDeadman(true);
            _link.Publish(RobotConfig.TOPIC_CMD_VEL, new TwistMsg(
                new Vector3Msg(5f, -4f, 0f),
                new Vector3Msg(0f, 0f, 3f)));
            Assert.AreEqual((double)RobotConfig.MAX_LINEAR_VEL, _link.Applied.LinearX, 1e-6);
            Assert.AreEqual(-(double)RobotConfig.MAX_LINEAR_VEL, _link.Applied.LinearY, 1e-6);
            Assert.AreEqual((double)RobotConfig.MAX_ANGULAR_VEL, _link.Applied.AngularZ, 1e-6);

            _link.CommandVelocity(5, 0, 0);
            _link.Tick(1);
            Assert.AreEqual((double)RobotConfig.MAX_LINEAR_VEL, _link.Pose.X, 1e-5);
            Assert.AreEqual(0, _link.Pose.Y, 1e-6);
        }

        [Test]
        public void Link_ProfileCannotLoosenCaps()
        {
            var loose = new SimulatedRobotLink(new RobotProfile
            {
                MaxLinVel = 0.5f,
                MaxAngVel = 2f,
                Topics = new RobotTopics { CmdVel = "/cmd_vel/custom" },
            });
            loose.Connect();
            loose.SetDeadman(true);
            loose.CommandVelocity(0.5, 0, 1.5);
            Assert.AreEqual((double)RobotConfig.MAX_LINEAR_VEL, loose.Applied.LinearX, 1e-6);
            Assert.AreEqual((double)RobotConfig.MAX_ANGULAR_VEL, loose.Applied.AngularZ, 1e-6);

            var tight = new SimulatedRobotLink(new RobotProfile
            {
                MaxLinVel = 0.1f,
                MaxAngVel = 0.4f,
                Topics = new RobotTopics { CmdVel = "/cmd_vel/custom" },
            });
            tight.Connect();
            int hits = 0;
            tight.Subscribe("/cmd_vel/custom", "geometry_msgs/Twist", _ => hits++);
            tight.SetDeadman(true);
            tight.CommandVelocity(0.2, 0, 0.8);
            Assert.AreEqual(0.1, tight.Applied.LinearX, 1e-6);
            Assert.AreEqual(0.4, tight.Applied.AngularZ, 1e-6);
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void Link_ArmSlewRespectsRateAndLimits()
        {
            _link.Publish(RobotConfig.TOPIC_ARM_COMMANDS, new JointStateMsg(
                RobotConfig.ARM_JOINT_NAMES,
                new[] { 1f, 0f, 0f, 0f, 0f, -5f }));

            _link.Tick(0.1);
            Assert.AreEqual(0.1, _link.GetJointPosition(0), 1e-9);
            Assert.AreEqual(0, _link.GetJointPosition(1), 1e-12);
            Assert.AreEqual((double)RobotConfig.ARM_JOINT_MIN[5], _link.GetJointPosition(5), 1e-6);

            _link.Tick(0.1);
            _link.Tick(0.1);
            Assert.AreEqual(0.3, _link.GetJointPosition(0), 1e-9);

            _link.CommandJoints(null, new[] { 10.0, 0, 0, 0, 0, -5 });
            _link.Tick(10);
            Assert.AreEqual((double)RobotConfig.ARM_JOINT_MAX[0], _link.GetJointPosition(0), 1e-5);
            Assert.AreEqual((double)RobotConfig.ARM_JOINT_MIN[5], _link.GetJointPosition(5), 1e-6);
        }

        [Test]
        public void Link_EstopFreezesArmUntilANewCommand()
        {
            _link.CommandJoints(new[] { "arm_shoulder_pan" }, new[] { 1.0 });
            _link.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, new BoolMsg(true));
            _link.Tick(1);
            Assert.AreEqual(0, _link.GetJointPosition(0), 1e-12);

            _link.CommandJoints(new[] { "arm_shoulder_pan" }, new[] { 1.0 });
            _link.Tick(1);
            Assert.AreEqual(0, _link.GetJointPosition(0), 1e-12);

            _link.ReleaseStop();
            _link.Tick(1);
            Assert.AreEqual(0, _link.GetJointPosition(0), 1e-12);

            _link.CommandJoints(new[] { "arm_shoulder_pan" }, new[] { 1.0 });
            _link.Tick(0.1);
            Assert.AreEqual(0.1, _link.GetJointPosition(0), 1e-9);
        }

        [Test]
        public void Link_TelemetryIsKindSim()
        {
            string odomJson = null;
            string jointJson = null;
            string imuJson = null;
            string telemetryJson = null;
            int odomAfterUnsub = 0;

            _link.Subscribe(RobotConfig.TOPIC_ODOM, "nav_msgs/Odometry", json => odomJson = json);
            _link.Subscribe(RobotConfig.TOPIC_ARM_STATES, "sensor_msgs/JointState", json => jointJson = json);
            _link.Subscribe(RobotConfig.TOPIC_IMU, "sensor_msgs/Imu", json => imuJson = json);
            _link.Subscribe(SimulatedRobotLink.TelemetryTopic, "ohho/SimTelemetry", json => telemetryJson = json);
            _link.Advertise(RobotConfig.TOPIC_CMD_VEL, "geometry_msgs/Twist");

            _link.SetDeadman(true);
            _link.CommandVelocity(0.2, 0, 0);
            _link.Tick(SimulatedVectors.TickSeconds);

            Assert.IsTrue(_link.IsAdvertised(RobotConfig.TOPIC_CMD_VEL));
            Assert.IsNotNull(odomJson);
            var odom = JObject.Parse(odomJson);
            Assert.AreEqual(SimulatedRobotLink.Kind, (string)odom["kind"]);
            Assert.AreEqual(0.02, odom["pose"]["pose"]["position"]["x"].Value<double>(), 1e-5);
            Assert.AreEqual("odom", (string)odom["header"]["frame_id"]);

            var joints = JObject.Parse(jointJson);
            Assert.AreEqual(SimulatedRobotLink.Kind, (string)joints["kind"]);
            Assert.AreEqual(RobotConfig.ARM_JOINT_NAMES.Length, ((JArray)joints["name"]).Count);
            Assert.AreEqual("arm_shoulder_pan", (string)joints["name"][0]);

            var imu = JObject.Parse(imuJson);
            Assert.AreEqual(SimulatedRobotLink.Kind, (string)imu["kind"]);
            Assert.AreEqual(0, imu["angular_velocity"]["z"].Value<double>(), 1e-6);

            var telemetry = JObject.Parse(telemetryJson);
            Assert.AreEqual("sim", (string)telemetry["kind"]);
            Assert.AreEqual("simulated", (string)telemetry["protocol"]);
            Assert.AreEqual(0.87, telemetry["battery"].Value<double>(), 1e-12);
            Assert.AreEqual(2, telemetry["latencyMs"].Value<int>());
            Assert.AreEqual(20, telemetry["msgRate"].Value<int>());
            Assert.IsTrue(telemetry["deadmanHeld"].Value<bool>());
            Assert.AreEqual(0.02, telemetry["x"].Value<double>(), 1e-9);

            string active = null;
            _link.Subscribe(RobotConfig.TOPIC_CONTROL_MODE_ACTIVE, "std_msgs/String", json => active = json);
            _link.Publish(RobotConfig.TOPIC_CONTROL_MODE, new StringMsg("teleop"));
            Assert.AreEqual("sim", (string)JObject.Parse(active)["kind"]);
            Assert.AreEqual("teleop", (string)JObject.Parse(active)["data"]);

            _link.Unsubscribe(RobotConfig.TOPIC_ODOM);
            _link.Subscribe(RobotConfig.TOPIC_ODOM, "nav_msgs/Odometry", _ => odomAfterUnsub++);
            _link.Unsubscribe(RobotConfig.TOPIC_ODOM);
            _link.Tick(SimulatedVectors.TickSeconds);
            Assert.AreEqual(0, odomAfterUnsub);
        }

        [Test]
        public void Link_Disconnected_IgnoresCommands()
        {
            var link = new SimulatedRobotLink();
            Assert.IsFalse(link.IsConnected);
            link.SetDeadman(true);
            link.CommandVelocity(0.2, 0, 0);
            link.Tick(1);
            AssertPose(default, link.Pose);

            link.Connect();
            link.SetDeadman(true);
            link.CommandVelocity(0.2, 0, 0);
            link.Tick(SimulatedVectors.TickSeconds);
            Assert.AreEqual(0.02, link.Pose.X, 1e-9);
        }

        [Test]
        public void Factory_SelectsInHeadsetOrRosBridge()
        {
            var sim = RobotLinkFactory.Select(RobotLinkTarget.InHeadset);
            Assert.IsInstanceOf<SimulatedRobotLink>(sim);
            Assert.IsTrue(sim.IsConnected);
            Assert.AreEqual(RobotLinkTarget.InHeadset, RobotLinkFactory.Selected);

            var ros = RobotLinkFactory.Select(RobotLinkTarget.RosBridge);
            Assert.AreSame(RosBridgeLink.Instance, ros);
            Assert.AreEqual(RobotLinkTarget.RosBridge, RobotLinkFactory.Selected);
        }

        [Test]
        public void Host_BuildsPlaceholderRoomAndTracksPose()
        {
            Assert.IsFalse(SimulatedTwinPlaceholder.IsUnityTwinPresent());

            var go = new GameObject("in-headset-sim-test");
            try
            {
                var host = go.AddComponent<InHeadsetSimHost>();
                Assert.IsTrue(host.Sim.IsConnected);
                Assert.IsNotNull(host.GetComponentInChildren<SimulatedTwinPlaceholder>());

                var floor = host.transform.Find(SimulatedTwinPlaceholder.RootName + "/SimFloor");
                Assert.IsNotNull(floor);
                Assert.AreEqual(SimulatedTwinPlaceholder.RoomSizeMeters, floor.localScale.x, 0.001f);

                host.Sim.SetDeadman(true);
                host.Sim.CommandVelocity(0.2, 0, 0);
                host.SimulateFrame(0.1f, true);

                Assert.AreEqual(0.02, host.Sim.Pose.X, 1e-9);
                var robot = host.transform.Find(SimulatedTwinPlaceholder.RootName + "/SimRobot");
                Assert.IsNotNull(robot);
                Assert.AreEqual(0.02f, robot.localPosition.z, 1e-4f);
                Assert.AreEqual(0f, robot.localPosition.x, 1e-4f);

                double x = host.Sim.Pose.X;
                host.SimulateFrame(0.1f, false);
                Assert.AreEqual(x, host.Sim.Pose.X, 1e-12);
                Assert.IsFalse(host.Sim.DeadmanHeld);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void AssertPose(SimPose expected, SimPose actual)
        {
            Assert.AreEqual(expected.X, actual.X, 1e-9, "x");
            Assert.AreEqual(expected.Y, actual.Y, 1e-9, "y");
            Assert.AreEqual(expected.Theta, actual.Theta, 1e-9, "theta");
        }

        static void AssertVelocity(SimVelocity expected, SimVelocity actual)
        {
            Assert.AreEqual(expected.LinearX, actual.LinearX, 1e-12, "vx");
            Assert.AreEqual(expected.LinearY, actual.LinearY, 1e-12, "vy");
            Assert.AreEqual(expected.AngularZ, actual.AngularZ, 1e-12, "wz");
        }
    }
}
