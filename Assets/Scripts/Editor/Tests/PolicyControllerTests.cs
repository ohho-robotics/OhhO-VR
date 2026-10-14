using System;
using System.Collections.Generic;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Policy;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Policy start/stop, real-robot supervise handover, and telemetry parsing.
    /// The controller is exercised through a fake robot link — no scene.
    /// </summary>
    public class PolicyControllerTests
    {
        private const string TelemetryWithGuards =
            "{\"data\":\"{\\\"v\\\":\\\"ohho.telemetry.v1\\\",\\\"kind\\\":\\\"sim\\\","
            + "\\\"estop\\\":{\\\"active\\\":false,\\\"latched\\\":false,\\\"source\\\":\\\"none\\\"},"
            + "\\\"deadman\\\":false,"
            + "\\\"policy\\\":{\\\"name\\\":\\\"act\\\",\\\"state\\\":\\\"running\\\",\\\"latency_ms\\\":18.5,"
            + "\\\"guards\\\":{\\\"black_frame\\\":2,\\\"stale_inference\\\":1,\\\"clamped_deltas\\\":4}}}\"}";

        private const string TelemetryLatencyOnly =
            "{\"v\":\"ohho.telemetry.v1\",\"kind\":\"real\",\"deadman\":false,"
            + "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},"
            + "\"policy\":{\"name\":\"act\",\"state\":\"running\",\"latency_ms\":42}}";

        [Test]
        public void SimStart_AfterConfirm_PublishesVlaThenPolicyCanRun()
        {
            var link = new FakeLink();
            PolicyController controller = Loaded(link, PolicySessionKind.Sim);
            controller.Confirm();
            int mark = link.Published.Count;

            PolicyCommandResult result = controller.Start();

            Assert.IsTrue(result.Accepted, result.Reason);
            Assert.AreEqual(PolicyRunState.Running, controller.RunState);
            List<Pub> batch = link.Since(mark);
            Assert.AreEqual(3, batch.Count);
            Assert.AreEqual("/policy/enable", batch[0].Topic);
            Assert.IsTrue(((BoolMsg)batch[0].Msg).data);
            Assert.AreEqual("/arm/cmd_mode", batch[1].Topic);
            Assert.AreEqual("policy", ((StringMsg)batch[1].Msg).data);
            Assert.AreEqual("/control_mode", batch[2].Topic);
            Assert.AreEqual("vla", ((StringMsg)batch[2].Msg).data);
        }

        [Test]
        public void Start_WithoutConfirm_PublishesNothing()
        {
            var link = new FakeLink();
            PolicyController controller = Loaded(link, PolicySessionKind.Sim);
            int mark = link.Published.Count;

            PolicyCommandResult result = controller.Start();

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(PolicyRefuse.Confirmation, result.Reason);
            Assert.AreEqual(mark, link.Published.Count);
            Assert.AreEqual(PolicyRunState.Loaded, controller.RunState);
        }

        [Test]
        public void RealStart_WithoutSupervise_IsRefused()
        {
            var link = new FakeLink();
            PolicyController controller = Loaded(link, PolicySessionKind.Real);
            controller.Confirm();
            int mark = link.Published.Count;

            PolicyCommandResult result = controller.Start();

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(PolicyRefuse.Supervise, result.Reason);
            Assert.AreEqual(mark, link.Published.Count);
        }

        [Test]
        public void UnspecifiedBind_FailsClosedToReal()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Unspecified);

            Assert.AreEqual(PolicySessionKind.Real, controller.SessionKind);
            Assert.IsTrue(controller.RequiresSupervise);
        }

        [Test]
        public void RealStart_WithSuperviseHeld_IsAccepted()
        {
            var link = new FakeLink();
            PolicyController controller = Loaded(link, PolicySessionKind.Real);
            controller.SetSuperviseHeld(true);
            controller.Confirm();

            PolicyCommandResult result = controller.Start();

            Assert.IsTrue(result.Accepted, result.Reason);
            Assert.AreEqual(PolicyRunState.Running, controller.RunState);
        }

        [Test]
        public void SuperviseRelease_WhileRealRunning_PublishesTeleopAndZerosImmediately()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Real);
            int mark = link.Published.Count;

            controller.SetSuperviseHeld(false);

            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("supervise_release", controller.LastStopReason);
            List<Pub> batch = link.Since(mark);
            Assert.GreaterOrEqual(batch.Count, 5);
            Assert.AreEqual("/control_mode", batch[0].Topic);
            Assert.AreEqual("teleop", ((StringMsg)batch[0].Msg).data);
            Assert.IsFalse(Message<BoolMsg>(batch, "/policy/enable").data);
            AssertZeroTwist(Message<TwistMsg>(batch, "/cmd_vel/teleop"));
            AssertZeroTwist(Message<TwistMsg>(batch, "/cmd_vel/vla"));
            JointStateMsg joints = Message<JointStateMsg>(batch, "/arm/joint_commands/teleop");
            Assert.AreEqual("arm_shoulder_pan", joints.name[0]);
            Assert.AreEqual("arm_gripper", joints.name[5]);
            for (int i = 0; i < joints.position.Length; i++)
                Assert.AreEqual(0f, joints.position[i]);
            Assert.IsFalse(HasTopic(batch, "/arm/cmd_mode"));
        }

        [Test]
        public void SuperviseRelease_WhileSimRunning_KeepsPolicy()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Sim);
            controller.SetSuperviseHeld(true);
            int mark = link.Published.Count;

            controller.SetSuperviseHeld(false);

            Assert.AreEqual(PolicyRunState.Running, controller.RunState);
            Assert.AreEqual(mark, link.Published.Count);
        }

        [Test]
        public void SuperviseRelease_WhileIdle_PublishesNothing()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Real);
            link.Published.Clear();

            controller.SetSuperviseHeld(true);
            controller.SetSuperviseHeld(false);

            Assert.AreEqual(0, link.Published.Count);
        }

        [Test]
        public void Stop_AlwaysPublishesTeleopAndZeros()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Sim);
            link.Published.Clear();

            controller.Stop();

            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("teleop", Message<StringMsg>(link.Published, "/control_mode").data);
            AssertZeroTwist(Message<TwistMsg>(link.Published, "/cmd_vel/teleop"));
            Assert.AreEqual(0f, Message<JointStateMsg>(link.Published, "/arm/joint_commands/teleop").position[0]);
        }

        [Test]
        public void EstopWhileRunning_HandsBack_AndBlocksRestart()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Sim);
            int mark = link.Published.Count;

            controller.NotifyEmergencyStop(true);

            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("estop", controller.LastStopReason);
            Assert.AreEqual("teleop", ((StringMsg)link.Since(mark)[0].Msg).data);

            controller.SetSuperviseHeld(true);
            controller.Confirm();
            PolicyCommandResult blocked = controller.Start();
            Assert.AreEqual(PolicyRefuse.Estop, blocked.Reason);

            controller.NotifyEmergencyStop(false);
            controller.Confirm();
            Assert.IsTrue(controller.Start().Accepted, controller.LastRefusal);
        }

        [Test]
        public void TelemetryEstopFalse_DoesNotClearLocalEstop()
        {
            var link = new FakeLink();
            PolicyController controller = Loaded(link, PolicySessionKind.Real);
            controller.NotifyEmergencyStop(true);
            controller.IngestTelemetry(TelemetryLatencyOnly);

            Assert.IsTrue(controller.EmergencyStopActive);
            controller.SetSuperviseHeld(true);
            controller.Confirm();
            Assert.AreEqual(PolicyRefuse.Estop, controller.Start().Reason);
        }

        [Test]
        public void DeadmanWhileRunning_HandsBackEvenInSim()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Sim);
            int mark = link.Published.Count;

            controller.NotifyDeadman(true);

            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("deadman", controller.LastStopReason);
            Assert.AreEqual("teleop", ((StringMsg)link.Since(mark)[0].Msg).data);
            controller.Confirm();
            Assert.AreEqual(PolicyRefuse.Deadman, controller.Start().Reason);
        }

        [Test]
        public void RunningReal_CannotDowngradeToSim()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Real);
            int mark = link.Published.Count;

            controller.SetSessionKind(PolicySessionKind.Sim);

            Assert.AreEqual(PolicySessionKind.Real, controller.SessionKind);
            Assert.AreEqual(PolicyRunState.Running, controller.RunState);
            Assert.AreEqual(mark, link.Published.Count);
        }

        [Test]
        public void RunningSim_BecomesRealWithoutSupervise_HandsBack()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Sim);

            controller.SetSessionKind(PolicySessionKind.Real);

            Assert.AreEqual(PolicySessionKind.Real, controller.SessionKind);
            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("session_real", controller.LastStopReason);
        }

        [Test]
        public void ExternalTeleopMode_DisablesPolicyWithoutZeroingArm()
        {
            var link = new FakeLink();
            PolicyController controller = Running(link, PolicySessionKind.Sim);
            int mark = link.Published.Count;

            controller.NoteControlModeActive("teleop");

            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("external_mode", controller.LastStopReason);
            List<Pub> batch = link.Since(mark);
            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual("/policy/enable", batch[0].Topic);
            Assert.IsFalse(((BoolMsg)batch[0].Msg).data);
        }

        [Test]
        public void Telemetry_ParsesLatencyAndGuardTrips_AndBlocksAiStatus()
        {
            var controller = new PolicyController();
            controller.IngestTelemetry(TelemetryWithGuards);

            Assert.IsTrue(controller.TelemetrySeen);
            Assert.AreEqual("telemetry", controller.Status.Source);
            Assert.AreEqual("act", controller.Status.PolicyName);
            Assert.AreEqual(18.5d, controller.Status.LatencyMs.Value, 0.001d);
            Assert.AreEqual(2, controller.Status.Trips.BlackFrame);
            Assert.AreEqual(1, controller.Status.Trips.StaleInference);
            Assert.AreEqual(4, controller.Status.Trips.ClampedDeltas);
            Assert.AreEqual(PolicySessionKind.Sim, controller.SessionKind);

            controller.IngestAiStatus("latency_ms=99 black_frame=9 stale_inference=9 clamped_deltas=9");

            Assert.AreEqual(18.5d, controller.Status.LatencyMs.Value, 0.001d);
            Assert.AreEqual(2, controller.Status.Trips.BlackFrame);
            StringAssert.Contains("18.5 ms", controller.FormatStatus());
            StringAssert.Contains("black 2", controller.FormatStatus());
            StringAssert.Contains("[telemetry]", controller.FormatStatus());
        }

        [Test]
        public void TelemetryWithoutGuardCounters_DoesNotInventTrips()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Real);
            controller.IngestTelemetry(TelemetryLatencyOnly);

            Assert.AreEqual(42d, controller.Status.LatencyMs.Value, 0.001d);
            Assert.IsFalse(controller.Status.Trips.HasBlackFrame);
            Assert.IsFalse(controller.Status.Trips.HasStaleInference);
            Assert.IsFalse(controller.Status.Trips.HasClampedDeltas);
            Assert.AreEqual(PolicySessionKind.Real, controller.SessionKind);
            StringAssert.Contains("black —", controller.FormatStatus());
        }

        [Test]
        public void AiStatus_UsedWhenTelemetryAbsent()
        {
            var controller = new PolicyController();
            controller.IngestAiStatus("{\"data\":\"latency_ms=12.5 black_frame=1 stale_inference=0 clamped_deltas=3\"}");

            Assert.IsFalse(controller.TelemetrySeen);
            Assert.AreEqual("ai_status", controller.Status.Source);
            Assert.AreEqual(12.5d, controller.Status.LatencyMs.Value, 0.001d);
            Assert.AreEqual(1, controller.Status.Trips.BlackFrame);
            Assert.AreEqual(0, controller.Status.Trips.StaleInference);
            Assert.AreEqual(3, controller.Status.Trips.ClampedDeltas);
        }

        [Test]
        public void ExplicitReal_IgnoresTelemetryKindSim()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Real);
            controller.IngestTelemetry(TelemetryWithGuards);

            Assert.AreEqual(PolicySessionKind.Real, controller.SessionKind);
            Assert.IsTrue(controller.RequiresSupervise);
        }

        [Test]
        public void ServeModels_ReplaceParam_AndEmptyServeFallsBack()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Sim);
            controller.ApplyParamModels("smolvla,diffusion");
            controller.ApplyServeModelsJson(
                "{\"models\":[{\"id\":\"act\",\"name\":\"ACT\",\"checkpoint\":\"lerobot/act_aloha\"}]}");

            Assert.AreEqual(1, controller.Policies.Count);
            Assert.AreEqual("lerobot/act_aloha", controller.Policies[0].Checkpoint);
            Assert.IsTrue(controller.Select("act"));
            Assert.IsTrue(controller.Load().Accepted, controller.LastRefusal);
            Assert.AreEqual("lerobot/act_aloha", Message<StringMsg>(link.Published, "/policy/load").data);

            controller.IngestPolicyModels("diffusion");
            Assert.AreEqual("act", controller.Policies[0].Id);

            controller.ApplyServeModelsJson("not-json");
            Assert.AreEqual(1, controller.Policies.Count);
            Assert.AreEqual("diffusion", controller.Policies[0].Id);
        }

        [Test]
        public void Bind_SubscribesTelemetryAndAiStatus()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Sim);

            Assert.IsTrue(link.Subscriptions.Contains("/ohho/telemetry"));
            Assert.IsTrue(link.Subscriptions.Contains("/ai/status"));
            Assert.IsTrue(link.Subscriptions.Contains("/policy/models"));
            Assert.IsTrue(link.Subscriptions.Contains("/control_mode/active"));
        }

        [Test]
        public void Load_WhileDisconnected_DoesNotClaimLoaded()
        {
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(Profile(), link, PolicySessionKind.Sim);
            link.IsConnected = false;
            controller.Select("act");

            Assert.AreEqual(PolicyRefuse.NotConnected, controller.Load().Reason);
            Assert.IsNull(controller.LoadedId);
        }

        [Test]
        public void SessionKindForStatus_SimOnlyWhenSimulated()
        {
            Assert.AreEqual(PolicySessionKind.Sim, PolicyPanelController.SessionKindForStatus("simulated"));
            Assert.AreEqual(PolicySessionKind.Sim, PolicyPanelController.SessionKindForStatus("SIM"));
            Assert.AreEqual(PolicySessionKind.Real, PolicyPanelController.SessionKindForStatus("active"));
            Assert.AreEqual(PolicySessionKind.Real, PolicyPanelController.SessionKindForStatus("draft"));
            Assert.AreEqual(PolicySessionKind.Real, PolicyPanelController.SessionKindForStatus(null));
        }

        [Test]
        public void OmniBotProfile_ExposesPolicyTopicsWithoutMovingTeleopArm()
        {
            RobotProfile profile = RobotProfileFactory.FromGarageRobot(OmniBot("omnibot"));

            Assert.IsNotNull(profile);
            Assert.AreEqual("/arm/joint_commands", profile.Topics.ArmCommands);
            Assert.AreEqual(RobotConfig.TOPIC_ARM_COMMANDS, profile.Topics.ArmCommands);
            Assert.AreEqual("/control_mode", profile.Topics.ControlMode);
            Assert.AreEqual("/arm/cmd_mode", profile.Topics.ArmCmdMode);
            Assert.AreEqual("/policy/enable", profile.Topics.PolicyEnable);
            Assert.AreEqual("/policy/load", profile.Topics.PolicyLoad);
            Assert.AreEqual("/policy/models", profile.Topics.PolicyModels);
            Assert.AreEqual("/cmd_vel/vla", profile.Topics.PolicyCmdVel);
            Assert.AreEqual("/ohho/telemetry", profile.Topics.Telemetry);
            Assert.AreEqual("/ai/status", profile.Topics.AiStatus);
            Assert.AreEqual("/control_mode/active", profile.Topics.ControlModeActive);
            Assert.AreEqual(RobotProfileFactory.ActVrSimPickModels, profile.PolicyModelsFallback);
        }

        [Test]
        public void Sim_LoadsStartsStopsAndMonitorsActVrCheckpoint()
        {
            RobotProfile profile = RobotProfileFactory.FromGarageRobot(OmniBot("omnibot"));
            var link = new FakeLink();
            var controller = new PolicyController();
            controller.Bind(profile, link, PolicySessionKind.Sim);

            Assert.IsTrue(controller.Select(RobotProfileFactory.ActVrSimPickId));
            int loadMark = link.Published.Count;
            PolicyCommandResult loaded = controller.Load();
            Assert.IsTrue(loaded.Accepted, loaded.Reason);
            Assert.AreEqual(RobotProfileFactory.ActVrSimPickCheckpoint, controller.LoadedCheckpoint);
            Assert.AreEqual("/policy/load", link.Since(loadMark)[0].Topic);
            Assert.AreEqual(
                RobotProfileFactory.ActVrSimPickCheckpoint,
                ((StringMsg)link.Since(loadMark)[0].Msg).data);

            controller.Confirm();
            PolicyCommandResult started = controller.Start();
            Assert.IsTrue(started.Accepted, started.Reason);
            Assert.AreEqual(PolicyRunState.Running, controller.RunState);
            Assert.AreEqual("SIM — supervise not required", controller.FormatGate());

            controller.IngestTelemetry(
                "{\"v\":\"ohho.telemetry.v1\",\"kind\":\"sim\",\"deadman\":false,"
                + "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},"
                + "\"policy\":{\"name\":\"act-vr-sim-pick\",\"state\":\"running\",\"latency_ms\":12.5,"
                + "\"guards\":{\"black_frame\":0,\"stale_inference\":0,\"clamped_deltas\":1}}}");
            Assert.AreEqual("act-vr-sim-pick", controller.Status.PolicyName);
            Assert.AreEqual(12.5, controller.Status.LatencyMs);
            Assert.IsTrue(controller.Status.Trips.HasClampedDeltas);
            Assert.IsTrue(controller.FormatStatus().Contains("12.5"));

            int stopMark = link.Published.Count;
            controller.Stop();
            Assert.AreEqual(PolicyRunState.Stopped, controller.RunState);
            Assert.AreEqual("teleop", ((StringMsg)Message<StringMsg>(link.Since(stopMark), "/control_mode")).data);
            Assert.IsFalse(((BoolMsg)Message<BoolMsg>(link.Since(stopMark), "/policy/enable")).data);
        }

        private static PolicyController Loaded(FakeLink link, PolicySessionKind kind)
        {
            var controller = new PolicyController();
            controller.Bind(Profile(), link, kind);
            Assert.IsTrue(controller.Select("act"));
            PolicyCommandResult loaded = controller.Load();
            Assert.IsTrue(loaded.Accepted, loaded.Reason);
            return controller;
        }

        private static PolicyController Running(FakeLink link, PolicySessionKind kind)
        {
            PolicyController controller = Loaded(link, kind);
            if (kind != PolicySessionKind.Sim)
                controller.SetSuperviseHeld(true);
            controller.Confirm();
            PolicyCommandResult started = controller.Start();
            Assert.IsTrue(started.Accepted, started.Reason);
            return controller;
        }

        private static RobotProfile Profile()
        {
            return new RobotProfile
            {
                RobotId = "omnibot",
                Name = "OmniBot",
                HasArm = true,
                ArmDof = 6,
                PolicyModelsFallback = "act,smolvla",
                Joints = new[]
                {
                    new JointSpec("arm_shoulder_pan", "Pan", -3.14f, 3.14f, 0f),
                    new JointSpec("arm_shoulder_lift", "Lift", -1.57f, 1.57f, 0f),
                    new JointSpec("arm_elbow_flex", "Elbow", -1.57f, 1.57f, 0f),
                    new JointSpec("arm_wrist_flex", "Wrist", -1.57f, 1.57f, 0f),
                    new JointSpec("arm_wrist_roll", "Roll", -3.14f, 3.14f, 0f),
                    new JointSpec("arm_gripper", "Grip", -0.1f, 0.8f, 0f),
                },
                Topics = new RobotTopics
                {
                    CmdVel = "/cmd_vel/teleop",
                    ControlMode = "/control_mode",
                    ArmCommands = "/arm/joint_commands/teleop",
                    ArmCmdMode = "/arm/cmd_mode",
                    PolicyEnable = "/policy/enable",
                    PolicyLoad = "/policy/load",
                    PolicyModels = "/policy/models",
                    PolicyCmdVel = "/cmd_vel/vla",
                    AiStatus = "/ai/status",
                    Telemetry = "/ohho/telemetry",
                    ControlModeActive = "/control_mode/active",
                },
            };
        }

        private static GarageRobot OmniBot(string modelId)
        {
            return new GarageRobot
            {
                UserRobot = new UserRobot { Id = "user-robot", Name = "OmniBot" },
                Category = new VrCategory { Id = "mobile-manipulator", Label = "Mobile manipulator" },
                RobotType = new VrRobotType
                {
                    Id = "omnibot-type",
                    Name = "OmniBot",
                    Category = "mobile-manipulator",
                },
                HardwareModel = new VrHardwareModel
                {
                    Id = modelId,
                    Name = "OmniBot",
                    Locomotion = "mecanum holonomic",
                    HasArm = true,
                },
            };
        }

        private static void AssertZeroTwist(TwistMsg twist)
        {
            Assert.IsNotNull(twist);
            Assert.AreEqual(0f, twist.linear.x);
            Assert.AreEqual(0f, twist.linear.y);
            Assert.AreEqual(0f, twist.linear.z);
            Assert.AreEqual(0f, twist.angular.x);
            Assert.AreEqual(0f, twist.angular.y);
            Assert.AreEqual(0f, twist.angular.z);
        }

        private static T Message<T>(List<Pub> batch, string topic) where T : class
        {
            for (int i = batch.Count - 1; i >= 0; i--)
            {
                if (batch[i].Topic == topic && batch[i].Msg is T typed)
                    return typed;
            }
            Assert.Fail("No " + typeof(T).Name + " on " + topic);
            return null;
        }

        private static bool HasTopic(List<Pub> batch, string topic)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                if (batch[i].Topic == topic) return true;
            }
            return false;
        }

        private struct Pub
        {
            public string Topic;
            public object Msg;
        }

        private sealed class FakeLink : IRobotLink
        {
            public bool IsConnected = true;
            public readonly List<Pub> Published = new List<Pub>();
            public readonly List<string> Subscriptions = new List<string>();

            bool IRobotLink.IsConnected => IsConnected;

            public void Advertise(string topic, string type) { }

            public void Publish<T>(string topic, T msg)
            {
                Published.Add(new Pub { Topic = topic, Msg = msg });
            }

            public void Subscribe(string topic, string type, Action<string> callback)
            {
                Subscriptions.Add(topic);
            }

            public void Unsubscribe(string topic) { }

            public List<Pub> Since(int mark)
            {
                var batch = new List<Pub>();
                for (int i = mark; i < Published.Count; i++)
                    batch.Add(Published[i]);
                return batch;
            }
        }
    }
}
