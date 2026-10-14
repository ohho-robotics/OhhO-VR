using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Nav;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Nav2 headset goals: real robots do not resume nav2 on idle, a stick
    /// during nav2 cancels the goal, and waypoint / plan / map payloads match
    /// the rosbridge contract.
    /// </summary>
    public class NavGoalTests
    {
        private NavGoalSession _session;
        private FakeTransport _transport;
        private FakeLink _link;

        [SetUp]
        public void SetUp()
        {
            _session = NavGoalSession.Active;
            _session.ResetForTests();
            _transport = new FakeTransport();
            _link = new FakeLink();
            _session.Transport = _transport;
        }

        [TearDown]
        public void TearDown()
        {
            _session.ResetForTests();
        }

        // ── Idle policy ──────────────────────────────────────────────────────

        [Test]
        public void ResolveIdle_Real_SuppressesNav2()
        {
            Assert.IsNull(ControlModeMachine.ResolveIdle("nav2", "real"));
            Assert.IsNull(ControlModeMachine.ResolveIdle("nav2", "REAL"));
            Assert.IsNull(ControlModeMachine.ResolveIdle("nav2", null));
        }

        [Test]
        public void ResolveIdle_Sim_KeepsNav2()
        {
            Assert.AreEqual("nav2", ControlModeMachine.ResolveIdle("nav2", "sim"));
            Assert.AreEqual("nav2", ControlModeMachine.ResolveIdle("nav2", "gazebo"));
        }

        [Test]
        public void ResolveIdle_Real_KeepsOffboard()
        {
            Assert.AreEqual("offboard", ControlModeMachine.ResolveIdle("offboard", "real"));
        }

        [Test]
        public void ResolveIdle_Empty_IsNull()
        {
            Assert.IsNull(ControlModeMachine.ResolveIdle("", "sim"));
        }

        [Test]
        public void Deployment_ExplicitKindBeatsSimulatedStatus()
        {
            Assert.AreEqual(RobotDeployment.Real, RobotDeployment.FromGarage("simulated", "real"));
        }

        [Test]
        public void Deployment_SimulatedStatus_IsSim()
        {
            Assert.AreEqual(RobotDeployment.Sim, RobotDeployment.FromGarage("simulated", null));
            Assert.AreEqual(RobotDeployment.Sim, RobotDeployment.FromGarage("active", "gazebo"));
        }

        [Test]
        public void Deployment_Missing_IsReal()
        {
            Assert.AreEqual(RobotDeployment.Real, RobotDeployment.FromGarage("active", null));
            Assert.AreEqual(RobotDeployment.Real, RobotDeployment.FromGarage(null, "hardware"));
        }

        [Test]
        public void Machine_RealIdle_DoesNotPublishNav2()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            mode.Tick(true, 0.1f);
            var idle = mode.Tick(false, 0.6f);
            Assert.IsFalse(mode.InTeleop);
            Assert.IsNull(idle.PublishMode);
            Assert.IsTrue(idle.IdleHeldTeleop);
            Assert.IsFalse(idle.CancelNavGoal);
        }

        [Test]
        public void Machine_SimIdle_PublishesNav2()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Sim);
            mode.Tick(true, 0.1f);
            var early = mode.Tick(false, 0.49f);
            Assert.IsNull(early.PublishMode);
            var idle = mode.Tick(false, 0.02f);
            Assert.AreEqual("nav2", idle.PublishMode);
        }

        [Test]
        public void Machine_RealStop_DoesNotReturnNav2()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            mode.Tick(true, 0.1f);
            Assert.IsNull(mode.Stop());
        }

        [Test]
        public void Machine_SimStop_ReturnsNav2()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Sim);
            mode.Tick(true, 0.1f);
            Assert.AreEqual("nav2", mode.Stop());
        }

        [Test]
        public void Machine_StickDuringNav2_CancelsOnce()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            mode.Observe(true);
            var first = mode.Tick(true, 0.02f);
            Assert.IsTrue(first.CancelNavGoal);
            Assert.AreEqual("teleop", first.PublishMode);
            var second = mode.Tick(true, 0.02f);
            Assert.IsFalse(second.CancelNavGoal);
            Assert.IsNull(second.PublishMode);
        }

        [Test]
        public void Machine_StickDuringTeleop_DoesNotCancel()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            mode.Observe(false);
            mode.Tick(true, 0.02f);
            var again = mode.Tick(true, 0.02f);
            Assert.IsFalse(again.CancelNavGoal);
            Assert.IsNull(again.PublishMode);
        }

        // ── Handover on the link ─────────────────────────────────────────────

        [Test]
        public void Handover_RealIdle_DoesNotPublishNav2()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", true, 0.02f);
            _link.Published.Clear();
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", false, 0.6f);
            Assert.IsFalse(_link.Published.Exists(p => p.topic == "/control_mode" && p.json.Contains("nav2")));
        }

        [Test]
        public void Handover_SimIdleThenStick_CancelsAndReturnsTeleop()
        {
            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Sim);
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", true, 0.02f);
            _link.Published.Clear();
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", false, 0.6f);
            Assert.IsTrue(_link.Published.Exists(p => p.topic == "/control_mode" && p.json.Contains("nav2")));
            Assert.IsTrue(_session.ModeIsNav2);

            _link.Published.Clear();
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", true, 0.02f);
            Assert.IsTrue(_link.Published.Exists(p => p.topic == RobotConfig.TOPIC_MISSION_CANCEL));
            Assert.IsTrue(_link.Published.Exists(p => p.topic == "/control_mode" && p.json.Contains("teleop")));
            Assert.IsFalse(_session.ModeIsNav2);
        }

        [Test]
        public void Handover_GoalThenStick_CancelsAction()
        {
            var kitchen = _session.Waypoints.First(w => w.Name == "kitchen");
            Assert.IsTrue(_session.SendGoal(kitchen));
            string goalJson = _transport.Raw.Find(j => j.Contains("send_action_goal"));
            var goal = JObject.Parse(goalJson);
            Assert.AreEqual("navigate_to_pose", goal["action"].ToString());
            Assert.AreEqual("nav2_msgs/action/NavigateToPose", goal["action_type"].ToString());
            Assert.AreEqual(-1.9, goal["args"]["pose"]["pose"]["position"]["x"].Value<double>(), 1e-6);
            Assert.AreEqual("map", goal["args"]["pose"]["header"]["frame_id"].ToString());

            var mode = new ControlModeMachine();
            mode.Reset("nav2", RobotDeployment.Real);
            NavStickHandover.Apply(mode, _session, _link, "/control_mode", true, 0.02f);

            Assert.IsFalse(_session.GoalActive);
            Assert.IsTrue(_link.Published.Exists(p => p.topic == "/mission/cancel" && p.json.Contains("cancel")));
            Assert.IsTrue(_transport.Raw.Exists(j => j.Contains("cancel_action_goal") && j.Contains(goal["id"].ToString())));
            Assert.IsTrue(_link.Published.Exists(p => p.topic == "/control_mode" && p.json.Contains("teleop")));
        }

        // ── Waypoints ────────────────────────────────────────────────────────

        [Test]
        public void Contract_KitchenPose_MatchesNavigationYaml()
        {
            var kitchen = WaypointCatalog.ApartmentContract().First(w => w.Name == "kitchen");
            Assert.AreEqual(-1.9, kitchen.X, 1e-9);
            Assert.AreEqual(0.7, kitchen.Y, 1e-9);
            Assert.AreEqual(1.0, kitchen.Qz, 1e-9);
            Assert.AreEqual(0.0, kitchen.Qw, 1e-9);
            Assert.AreEqual("map", kitchen.FrameId);
        }

        [Test]
        public void Catalog_YamlKitchen_MatchesContract()
        {
            const string yaml = @"
frame_id: map
waypoints:
  - name: ""kitchen""
    yaw_deg: 180.0
    position:
      x: -1.9
      y: 0.7
      z: 0.0
    orientation:
      x: 0.0
      y: 0.0
      z: 1.0
      w: 0.0
";
            var parsed = WaypointCatalog.Parse(yaml);
            Assert.AreEqual(1, parsed.Count);
            var contract = WaypointCatalog.ApartmentContract().First(w => w.Name == "kitchen");
            Assert.AreEqual(contract.X, parsed[0].X, 1e-6);
            Assert.AreEqual(contract.Y, parsed[0].Y, 1e-6);
            Assert.AreEqual(contract.Qz, parsed[0].Qz, 1e-6);
            Assert.AreEqual(contract.Qw, parsed[0].Qw, 1e-6);
        }

        [Test]
        public void Catalog_JsonLocations_KitchenYaw()
        {
            const string json = "{\"frame_id\":\"map\",\"locations\":{\"kitchen\":{\"x\":-1.9,\"y\":0.7,\"yaw\":3.141592653589793}}}";
            var parsed = WaypointCatalog.Parse(json);
            Assert.AreEqual("kitchen", parsed[0].Name);
            Assert.AreEqual(-1.9, parsed[0].X, 1e-6);
            Assert.AreEqual(1.0, parsed[0].Qz, 1e-5);
            Assert.AreEqual(0.0, parsed[0].Qw, 1e-5);
        }

        [Test]
        public void Catalog_LocationYaml()
        {
            const string yaml = "locations:\n  desk:\n    x: 2.55\n    y: -0.45\n    yaw: 1.5707963267948966\n";
            var parsed = WaypointCatalog.Parse(yaml);
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("desk", parsed[0].Name);
            Assert.AreEqual(2.55, parsed[0].X, 1e-6);
            Assert.AreEqual(0.7071068, parsed[0].Qz, 1e-4);
        }

        [Test]
        public void Catalog_ServiceMessageWrapper()
        {
            var values = JObject.Parse("{\"success\":true,\"message\":\"{\\\"locations\\\":{\\\"door\\\":{\\\"x\\\":-0.3,\\\"y\\\":-1.45,\\\"yaw\\\":-1.5707963267948966}}}\"}");
            var parsed = WaypointCatalog.ParseServiceValues(values);
            Assert.AreEqual("door", parsed[0].Name);
            Assert.AreEqual(-0.3, parsed[0].X, 1e-6);
            Assert.AreEqual(-0.7071068, parsed[0].Qz, 1e-4);
        }

        [Test]
        public void Catalog_BadText_Empty()
        {
            Assert.AreEqual(0, WaypointCatalog.Parse("not a catalog").Count);
            Assert.AreEqual(0, WaypointCatalog.Parse("").Count);
        }

        [Test]
        public void ServiceResponse_ReplacesKitchen()
        {
            string payload = "{\"locations\":{\"kitchen\":{\"x\":1.25,\"y\":2,\"yaw\":0}}}";
            string frame = "{\"op\":\"service_response\",\"id\":\"" + NavMessages.ListServiceId
                + "\",\"result\":true,\"values\":{\"success\":true,\"message\":" + JsonString(payload) + "}}";
            _session.HandleProtocol(frame);
            var kitchen = _session.Waypoints.First(w => w.Name == "kitchen");
            Assert.AreEqual(1.25, kitchen.X, 1e-6);
            Assert.AreEqual("robot", _session.WaypointSource);
        }

        [Test]
        public void ServiceResponse_Garbage_KeepsContract()
        {
            _session.HandleProtocol("{\"op\":\"service_response\",\"id\":\"" + NavMessages.ListServiceId
                + "\",\"result\":true,\"values\":{\"success\":true,\"message\":\"nope\"}}");
            Assert.AreEqual(-1.9, _session.Waypoints.First(w => w.Name == "kitchen").X, 1e-6);
            Assert.AreEqual("apartment contract", _session.WaypointSource);
        }

        // ── Plan, feedback, map ──────────────────────────────────────────────

        [Test]
        public void Feedback_EtaAndDistance()
        {
            var kitchen = _session.Waypoints.First(w => w.Name == "kitchen");
            _session.SendGoal(kitchen);
            string id = JObject.Parse(_transport.Raw.Find(j => j.Contains("send_action_goal")))["id"].ToString();
            _session.HandleProtocol(
                "{\"op\":\"action_feedback\",\"id\":\"" + id + "\",\"action\":\"navigate_to_pose\",\"values\":{"
                + "\"estimated_time_remaining\":{\"sec\":12,\"nanosec\":500000000},"
                + "\"distance_remaining\":1.5}}");
            Assert.AreEqual(12.5f, _session.EtaSeconds.Value, 1e-4f);
            Assert.AreEqual(1.5f, _session.DistanceRemaining.Value, 1e-4f);
            Assert.AreEqual("executing", _session.StatusText);
            StringAssert.Contains("ETA 12.5 s", _session.HudLine);
            StringAssert.Contains("kitchen", _session.HudLine);
        }

        [Test]
        public void Result_Succeeded_ClearsGoal()
        {
            _session.SendGoal(_session.Waypoints.First(w => w.Name == "kitchen"));
            string id = JObject.Parse(_transport.Raw.Find(j => j.Contains("send_action_goal")))["id"].ToString();
            _session.HandleProtocol("{\"op\":\"action_result\",\"id\":\"" + id
                + "\",\"action\":\"navigate_to_pose\",\"status\":4,\"result\":true,\"values\":{}}");
            Assert.IsFalse(_session.GoalActive);
            Assert.AreEqual("succeeded", _session.StatusText);
        }

        [Test]
        public void Plan_Length()
        {
            const string json = "{\"header\":{\"frame_id\":\"map\"},\"poses\":["
                + "{\"pose\":{\"position\":{\"x\":0,\"y\":0}}},"
                + "{\"pose\":{\"position\":{\"x\":3,\"y\":0}}},"
                + "{\"pose\":{\"position\":{\"x\":3,\"y\":4}}}]}";
            var plan = NavMessages.ParsePlan(json);
            Assert.AreEqual(3, plan.Points.Count);
            Assert.AreEqual(7f, plan.LengthMeters, 1e-4f);
            _session.HandlePlan(json);
            StringAssert.Contains("plan 3 poses 7.00 m", _session.HudLine);
        }

        [Test]
        public void MapClick_OriginCell()
        {
            var map = new OccupancyMap
            {
                Width = 4,
                Height = 2,
                Resolution = 0.05f,
                OriginX = -3.455f,
                OriginY = -2.746f,
                Cells = new int[8],
            };
            Assert.IsTrue(NavMessages.TryMapClick(0f, 0f, 40f, 20f, map, out var x, out var y));
            Assert.AreEqual(-3.455f + 0.025f, x, 1e-4f);
            Assert.AreEqual(-2.746f + 0.025f, y, 1e-4f);
            Assert.IsFalse(NavMessages.TryMapClick(-1f, 0f, 40f, 20f, map, out _, out _));
        }

        [Test]
        public void MapParse_RejectsHugeGrid()
        {
            Assert.IsNull(NavMessages.ParseMap("{\"info\":{\"width\":9000,\"height\":10,\"resolution\":0.05,\"origin\":{\"position\":{\"x\":0,\"y\":0}}},\"data\":[]}"));
        }

        [Test]
        public void GoalStatusNames()
        {
            Assert.AreEqual("succeeded", NavMessages.GoalStatusName(4));
            Assert.AreEqual("canceled", NavMessages.GoalStatusName(5));
            Assert.AreEqual("aborted", NavMessages.GoalStatusName(6));
            Assert.IsTrue(NavMessages.IsTerminalStatus(4));
            Assert.IsFalse(NavMessages.IsTerminalStatus(2));
        }

        [Test]
        public void Fragment_Reassembles()
        {
            const string full = "{\"op\":\"service_response\",\"id\":\"ohho-wp-list\",\"result\":true}";
            int mid = full.Length / 2;
            var buffer = new RosbridgeFragmentBuffer();
            Assert.IsNull(buffer.Push("frag", 0, 2, full.Substring(0, mid)));
            Assert.AreEqual(full, buffer.Push("frag", 1, 2, full.Substring(mid)));
        }

        [Test]
        public void RequestWaypointList_CallsParamAndService()
        {
            _session.RequestWaypointList();
            Assert.IsTrue(_transport.Raw.Exists(j => j.Contains("/ohho/nav/list_waypoints")));
            Assert.IsTrue(_transport.Raw.Exists(j => j.Contains("/ohho/waypoints")));
            Assert.IsTrue(_transport.Raw.Exists(j => j.Contains("\"/waypoints\"")));
        }

        // ── Profile kind ─────────────────────────────────────────────────────

        [Test]
        public void Factory_SimulatedRobot_IsSim()
        {
            var profile = RobotProfileFactory.FromGarageRobot(Garage("simulated", null));
            Assert.AreEqual(RobotDeployment.Sim, profile.DeploymentKind);
        }

        [Test]
        public void Factory_KindReal_OverridesSimulatedStatus()
        {
            var profile = RobotProfileFactory.FromGarageRobot(Garage("simulated", "real"));
            Assert.AreEqual(RobotDeployment.Real, profile.DeploymentKind);
        }

        private static GarageRobot Garage(string status, string kind)
        {
            var config = new Dictionary<string, object>();
            if (kind != null) config["kind"] = kind;
            return new GarageRobot
            {
                UserRobot = new UserRobot { Status = status, Config = config, Name = "Bot" },
                HardwareModel = new VrHardwareModel
                {
                    Id = "omnibot",
                    Name = "OmniBot",
                    Locomotion = "mecanum",
                    HasArm = true,
                },
                RobotType = new VrRobotType { Id = "omnibot", Name = "OmniBot" },
                Category = new VrCategory { Id = "mobile-manipulator" },
            };
        }

        private static string JsonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private sealed class FakeLink : IRobotLink
        {
            public readonly List<(string topic, string json)> Published = new List<(string topic, string json)>();
            public bool IsConnected => true;
            public void Advertise(string topic, string type) { }
            public void Publish<T>(string topic, T msg) =>
                Published.Add((topic, Newtonsoft.Json.JsonConvert.SerializeObject(msg)));
            public void Subscribe(string topic, string type, System.Action<string> callback) { }
            public void Unsubscribe(string topic) { }
        }

        private sealed class FakeTransport : INavTransport
        {
            public readonly List<string> Raw = new List<string>();
            public readonly List<(string topic, string json)> Published = new List<(string topic, string json)>();
            public bool IsConnected => true;
            public void SendRaw(string json) => Raw.Add(json);
            public void Publish<T>(string topic, T message) =>
                Published.Add((topic, Newtonsoft.Json.JsonConvert.SerializeObject(message)));
            public void Advertise(string topic, string type) { }
            public void Subscribe(string topic, string type, System.Action<string> callback) { }
        }
    }
}
