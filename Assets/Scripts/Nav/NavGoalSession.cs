using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Nav
{
    /// <summary>
    /// Headset-side Nav2 goal: named waypoints or a map click go out as
    /// <c>navigate_to_pose</c>, <c>/control_mode</c> becomes <c>nav2</c>, and the
    /// HUD reads <c>/plan</c> plus action feedback (status, ETA, distance).
    /// Stick override and the cancel button both publish <c>/mission/cancel</c>
    /// and cancel the action goal.
    /// </summary>
    public sealed class NavGoalSession
    {
        public static readonly NavGoalSession Active = new NavGoalSession();

        public INavTransport Transport { get; set; }
        public event Action Changed;

        public string DeploymentKind { get; private set; } = RobotDeployment.Real;
        public bool GoalActive { get; private set; }
        public string ObservedMode { get; private set; } = "teleop";
        public string StatusText { get; private set; } = "idle";
        public string GoalName { get; private set; }
        public float? EtaSeconds { get; private set; }
        public float? DistanceRemaining { get; private set; }
        public GlobalPlan Plan { get; private set; }
        public OccupancyMap Map { get; private set; }
        public string WaypointSource { get; private set; } = "apartment contract";
        public bool ModeIsNav2 =>
            string.Equals(ObservedMode, "nav2", StringComparison.OrdinalIgnoreCase);

        private readonly List<NamedWaypoint> _waypoints = WaypointCatalog.ApartmentContract();
        private string _goalId;

        public IReadOnlyList<NamedWaypoint> Waypoints => _waypoints;

        public void ResetForTests()
        {
            Changed = null;
            Transport = null;
            DeploymentKind = RobotDeployment.Real;
            GoalActive = false;
            ObservedMode = "teleop";
            StatusText = "idle";
            GoalName = null;
            EtaSeconds = null;
            DistanceRemaining = null;
            Plan = null;
            Map = null;
            WaypointSource = "apartment contract";
            _goalId = null;
            _waypoints.Clear();
            _waypoints.AddRange(WaypointCatalog.ApartmentContract());
        }

        public void SetDeployment(string kind)
        {
            DeploymentKind = RobotDeployment.Normalize(kind);
            Changed?.Invoke();
        }

        public void NoteModePublished(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            ObservedMode = mode;
            Changed?.Invoke();
        }

        public string HudLine
        {
            get
            {
                string who = string.IsNullOrEmpty(GoalName) ? "no goal" : GoalName;
                string eta = EtaSeconds.HasValue
                    ? " · ETA " + EtaSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture) + " s"
                    : "";
                string dist = DistanceRemaining.HasValue
                    ? " · " + DistanceRemaining.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m"
                    : "";
                string plan = Plan != null && Plan.Points.Count > 0
                    ? " · plan " + Plan.Points.Count + " poses "
                      + Plan.LengthMeters.ToString("0.00", CultureInfo.InvariantCulture) + " m"
                    : "";
                return who + " · " + StatusText + eta + dist + plan;
            }
        }

        public bool SendGoal(NamedWaypoint wp)
        {
            if (wp == null) return false;
            var transport = Effective;
            if (transport == null || !transport.IsConnected)
            {
                StatusText = "not connected";
                Changed?.Invoke();
                return false;
            }

            if (!string.IsNullOrEmpty(_goalId))
                transport.SendRaw(NavMessages.CancelGoalJson(_goalId));

            _goalId = Guid.NewGuid().ToString("N");
            GoalName = wp.Name;
            GoalActive = true;
            ObservedMode = "nav2";
            StatusText = "sending";
            EtaSeconds = null;
            DistanceRemaining = null;
            transport.Advertise(RobotConfig.TOPIC_CONTROL_MODE, "std_msgs/String");
            transport.Publish(RobotConfig.TOPIC_CONTROL_MODE, new StringMsg("nav2"));
            transport.SendRaw(NavMessages.SendGoalJson(_goalId, wp));
            Changed?.Invoke();
            return true;
        }

        public bool SendMapGoal(float x, float y)
        {
            return SendGoal(new NamedWaypoint
            {
                Name = "map",
                FrameId = "map",
                X = x,
                Y = y,
                Qw = 1,
            });
        }

        /// <summary>Explicit Nav2 / Teleop button. Teleop cancels a live goal first.</summary>
        public void RequestMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            var transport = Effective;
            if (transport == null || !transport.IsConnected)
            {
                StatusText = "not connected";
                Changed?.Invoke();
                return;
            }
            if (string.Equals(mode, "teleop", StringComparison.OrdinalIgnoreCase)
                && (GoalActive || ModeIsNav2))
                CancelForOverride(null);
            transport.Advertise(RobotConfig.TOPIC_CONTROL_MODE, "std_msgs/String");
            transport.Publish(RobotConfig.TOPIC_CONTROL_MODE, new StringMsg(mode));
            NoteModePublished(mode);
        }

        /// <summary>Stick took over while Nav2 was active. Cancel the goal and remember teleop.</summary>
        public void CancelForOverride(IRobotLink link)
        {
            SendMissionCancel(link);
            SendActionCancel();
            GoalActive = false;
            _goalId = null;
            ObservedMode = "teleop";
            StatusText = "canceled · teleop";
            EtaSeconds = null;
            DistanceRemaining = null;
            Plan = null;
            Changed?.Invoke();
        }

        /// <summary>Cancel button. Publishes <c>/mission/cancel</c> and cancels the action.</summary>
        public void CancelFromButton(IRobotLink link)
        {
            SendMissionCancel(link);
            SendActionCancel();
            GoalActive = false;
            _goalId = null;
            StatusText = "canceled";
            EtaSeconds = null;
            DistanceRemaining = null;
            Plan = null;
            Changed?.Invoke();
        }

        public void RequestWaypointList()
        {
            var transport = Effective;
            if (transport == null || !transport.IsConnected) return;
            transport.SendRaw(NavMessages.GetParamCallJson(NavMessages.ParamOhhoId, NavMessages.ParamOhhoName));
            transport.SendRaw(NavMessages.GetParamCallJson(NavMessages.ParamRootId, NavMessages.ParamRootName));
            transport.SendRaw(NavMessages.ListWaypointsCallJson());
        }

        public void HandleProtocol(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JObject root;
            try { root = JObject.Parse(json); }
            catch { return; }

            string op = root["op"]?.ToString();
            if (op == "action_feedback") HandleFeedback(root);
            else if (op == "action_result") HandleResult(root);
            else if (op == "service_response") HandleService(root);
        }

        public void HandlePlan(string msgJson)
        {
            var plan = NavMessages.ParsePlan(msgJson);
            if (plan == null) return;
            Plan = plan;
            Changed?.Invoke();
        }

        public void HandleMap(string msgJson)
        {
            var map = NavMessages.ParseMap(msgJson);
            if (map == null) return;
            Map = map;
            Changed?.Invoke();
        }

        public void HandleWaypointMessage(string msgJson)
        {
            if (string.IsNullOrWhiteSpace(msgJson)) return;
            string text = msgJson;
            try
            {
                var obj = JObject.Parse(msgJson);
                if (obj["data"] != null) text = obj["data"].ToString();
            }
            catch
            {
                // plain catalog text
            }
            ApplyCatalog(WaypointCatalog.Parse(text));
        }

        public void HandleModeEcho(string msgJson)
        {
            if (string.IsNullOrWhiteSpace(msgJson)) return;
            try
            {
                var data = JObject.Parse(msgJson)["data"]?.ToString();
                if (!string.IsNullOrEmpty(data))
                    ObservedMode = data;
            }
            catch
            {
                // ignore malformed echo
            }
            Changed?.Invoke();
        }

        private void HandleFeedback(JObject root)
        {
            if (!MatchesGoal(root)) return;
            var values = root["values"] ?? root["feedback"];
            if (values != null)
            {
                if (NavMessages.TryDurationSeconds(values["estimated_time_remaining"], out var eta))
                    EtaSeconds = eta;
                if (values["distance_remaining"] != null && values["distance_remaining"].Type != JTokenType.Null)
                    DistanceRemaining = values["distance_remaining"].Value<float>();
            }
            if (GoalActive) StatusText = "executing";
            Changed?.Invoke();
        }

        private void HandleResult(JObject root)
        {
            if (!MatchesGoal(root)) return;
            int status = root["status"]?.Value<int>() ?? 0;
            StatusText = NavMessages.GoalStatusName(status);
            if (NavMessages.IsTerminalStatus(status) || root["result"]?.Value<bool>() == false)
            {
                GoalActive = false;
                _goalId = null;
                EtaSeconds = null;
            }
            Changed?.Invoke();
        }

        private void HandleService(JObject root)
        {
            string id = root["id"]?.ToString();
            if (id != NavMessages.ListServiceId && id != NavMessages.ParamOhhoId && id != NavMessages.ParamRootId)
                return;
            if (root["result"]?.Type == JTokenType.Boolean && root["result"].Value<bool>() == false)
                return;
            var parsed = WaypointCatalog.ParseServiceValues(root["values"]);
            ApplyCatalog(parsed);
        }

        private void ApplyCatalog(List<NamedWaypoint> parsed)
        {
            if (parsed == null || parsed.Count == 0) return;
            _waypoints.Clear();
            _waypoints.AddRange(parsed);
            WaypointSource = "robot";
            Changed?.Invoke();
        }

        private bool MatchesGoal(JObject root)
        {
            if (string.IsNullOrEmpty(_goalId)) return false;
            string id = root["id"]?.ToString();
            return string.IsNullOrEmpty(id) || id == _goalId;
        }

        private void SendMissionCancel(IRobotLink link)
        {
            var msg = new StringMsg("cancel");
            if (link != null && link.IsConnected)
            {
                link.Advertise(RobotConfig.TOPIC_MISSION_CANCEL, "std_msgs/String");
                link.Publish(RobotConfig.TOPIC_MISSION_CANCEL, msg);
                return;
            }
            var transport = Effective;
            if (transport != null && transport.IsConnected)
            {
                transport.Advertise(RobotConfig.TOPIC_MISSION_CANCEL, "std_msgs/String");
                transport.Publish(RobotConfig.TOPIC_MISSION_CANCEL, msg);
            }
        }

        private void SendActionCancel()
        {
            if (string.IsNullOrEmpty(_goalId)) return;
            var transport = Effective;
            if (transport != null && transport.IsConnected)
                transport.SendRaw(NavMessages.CancelGoalJson(_goalId));
        }

        private INavTransport Effective => Transport != null ? Transport : RosbridgeNavTransport.Instance;
    }
}
