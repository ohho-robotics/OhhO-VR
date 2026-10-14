using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Policy
{
    /// <summary>
    /// Headset policy control that does not need a Unity scene.
    ///
    /// Start publishes <c>/control_mode</c> <c>vla</c> and <c>/arm/cmd_mode</c>
    /// <c>policy</c> only after an explicit confirmation. Real (and unspecified)
    /// sessions also require the supervise hold. Releasing it, or an e-stop or
    /// deadman, publishes teleop mode and zero base and arm commands on the
    /// same call. Existing drive e-stop behaviour is not modified here.
    ///
    /// <c>/policy/load</c> is published on load. policy_node still takes its
    /// checkpoint from a startup parameter, so a running node may ignore that
    /// topic until it grows a load subscription. Serve <c>POST /load_model</c>
    /// is performed by <see cref="PolicyPanelController"/> when a URL is known.
    /// </summary>
    public sealed class PolicyController
    {
        private readonly List<PolicyDescriptor> _policies = new List<PolicyDescriptor>();
        private RobotProfile _profile;
        private IRobotLink _link;
        private string _paramRaw = "";
        private bool _serveListActive;
        private bool _listening;
        private bool _telemetrySeen;
        private bool _localEstop;
        private bool _telemetryEstop;
        private bool _deadman;
        private bool _handingOver;

        public RobotProfile Profile => _profile;
        public IRobotLink Link => _link;
        public PolicySessionKind SessionKind { get; private set; } = PolicySessionKind.Unspecified;
        public PolicyRunState RunState { get; private set; } = PolicyRunState.Idle;
        public bool SuperviseHeld { get; private set; }
        public bool Confirmed { get; private set; }
        public string SelectedId { get; private set; }
        public string LoadedId { get; private set; }
        public string LoadedCheckpoint { get; private set; }
        public string LastStopReason { get; private set; }
        public string LastRefusal { get; private set; }
        public PolicyStatusView Status { get; private set; } = new PolicyStatusView();
        public bool TelemetrySeen => _telemetrySeen;
        public bool EmergencyStopActive => _localEstop || _telemetryEstop;
        public bool DeadmanActive => _deadman;

        /// <summary>Sim is the only kind that may run without the supervise hold.</summary>
        public bool RequiresSupervise => SessionKind != PolicySessionKind.Sim;

        public IReadOnlyList<PolicyDescriptor> Policies => _policies;

        public void Bind(RobotProfile profile, IRobotLink link, PolicySessionKind kind)
        {
            if (profile == null) return;
            if (RunState == PolicyRunState.Running)
                Handover("rebind", true);

            _profile = profile;
            _link = link;
            SessionKind = kind == PolicySessionKind.Unspecified
                ? PolicySessionKind.Real
                : kind;

            if (string.IsNullOrEmpty(_paramRaw))
                _paramRaw = profile.PolicyModelsFallback ?? "";

            EnsureListening();
            if (!_serveListActive && _policies.Count == 0)
                ReplaceFromParam(_paramRaw);
        }

        /// <summary>
        /// Refuses a Real→Sim downgrade while a policy is running. Moving a
        /// running sim session to real without supervise hands back immediately.
        /// </summary>
        public void SetSessionKind(PolicySessionKind kind)
        {
            if (kind == PolicySessionKind.Unspecified)
                kind = PolicySessionKind.Real;
            if (RunState == PolicyRunState.Running && RequiresSupervise && kind == PolicySessionKind.Sim)
                return;
            if (kind == SessionKind) return;
            SessionKind = kind;
            if (RunState == PolicyRunState.Running && RequiresSupervise && !SuperviseHeld)
                Handover("session_real", true);
        }

        public void Confirm()
        {
            Confirmed = true;
        }

        public void RevokeConfirmation()
        {
            Confirmed = false;
        }

        /// <summary>
        /// Real-robot deadman. Releasing while a policy is running publishes
        /// teleop mode and zero commands before this call returns.
        /// </summary>
        public void SetSuperviseHeld(bool held)
        {
            if (SuperviseHeld == held) return;
            SuperviseHeld = held;
            if (!held && RunState == PolicyRunState.Running && RequiresSupervise)
                Handover("supervise_release", true);
        }

        /// <summary>Local both-grips e-stop. Telemetry cannot clear this latch.</summary>
        public void NotifyEmergencyStop(bool active)
        {
            _localEstop = active;
            if (EmergencyStopActive && RunState == PolicyRunState.Running)
                Handover("estop", true);
        }

        /// <summary>Robot deadman from telemetry or another safety source.</summary>
        public void NotifyDeadman(bool active)
        {
            _deadman = active;
            if (_deadman && RunState == PolicyRunState.Running)
                Handover("deadman", true);
        }

        public void ApplyServeModelsJson(string json)
        {
            if (!PolicyCatalog.TryParseServe(json, out List<PolicyDescriptor> parsed))
            {
                UseParamFallback();
                return;
            }
            _policies.Clear();
            _policies.AddRange(parsed);
            _serveListActive = true;
            if (Find(SelectedId) == null) SelectedId = null;
        }

        public void ApplyParamModels(string raw)
        {
            _paramRaw = raw ?? "";
            if (_serveListActive) return;
            ReplaceFromParam(_paramRaw);
        }

        public void UseParamFallback()
        {
            _serveListActive = false;
            string raw = string.IsNullOrWhiteSpace(_paramRaw)
                ? (_profile != null ? _profile.PolicyModelsFallback : "")
                : _paramRaw;
            _paramRaw = raw ?? "";
            ReplaceFromParam(_paramRaw);
        }

        public bool Select(string id)
        {
            if (Find(id) == null) return false;
            SelectedId = id;
            return true;
        }

        public void SelectNext()
        {
            if (_policies.Count == 0) return;
            int index = -1;
            for (int i = 0; i < _policies.Count; i++)
            {
                if (string.Equals(_policies[i].Id, SelectedId, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            index = (index + 1) % _policies.Count;
            SelectedId = _policies[index].Id;
        }

        public PolicyCommandResult Load()
        {
            if (_profile == null) return Refuse(PolicyRefuse.NoProfile);
            if (!Connected) return Refuse(PolicyRefuse.NotConnected);
            if (RunState == PolicyRunState.Running) return Refuse(PolicyRefuse.Running);
            PolicyDescriptor policy = Find(SelectedId);
            if (policy == null) return Refuse(PolicyRefuse.NotSelected);

            string checkpoint = string.IsNullOrEmpty(policy.Checkpoint) ? policy.Id : policy.Checkpoint;
            PublishString(_profile.Topics != null ? _profile.Topics.PolicyLoad : null, checkpoint);
            LoadedId = policy.Id;
            LoadedCheckpoint = checkpoint;
            RunState = PolicyRunState.Loaded;
            LastRefusal = null;
            return PolicyCommandResult.Ok();
        }

        public PolicyCommandResult Start()
        {
            if (_profile == null) return Refuse(PolicyRefuse.NoProfile);
            if (!Connected) return Refuse(PolicyRefuse.NotConnected);
            if (EmergencyStopActive) return Refuse(PolicyRefuse.Estop);
            if (DeadmanActive) return Refuse(PolicyRefuse.Deadman);
            if (!Confirmed) return Refuse(PolicyRefuse.Confirmation);
            if (RequiresSupervise && !SuperviseHeld) return Refuse(PolicyRefuse.Supervise);
            if (string.IsNullOrEmpty(LoadedId)) return Refuse(PolicyRefuse.NotLoaded);
            if (!_profile.HasArm) return Refuse(PolicyRefuse.NoArm);

            RobotTopics topics = _profile.Topics;
            if (topics == null || string.IsNullOrEmpty(topics.ControlMode) || string.IsNullOrEmpty(topics.ArmCmdMode))
                return Refuse(PolicyRefuse.TopicsMissing);

            Confirmed = false;
            LastRefusal = null;
            LastStopReason = null;
            RunState = PolicyRunState.Running;
            // Enable and select the arm source before releasing the teleop hold.
            PublishEnable(true);
            PublishString(topics.ArmCmdMode, "policy");
            PublishString(topics.ControlMode, "vla");
            return PolicyCommandResult.Ok();
        }

        /// <summary>Always returns to teleop and publishes zero commands.</summary>
        public void Stop()
        {
            Handover("stop", true);
        }

        public void IngestTelemetry(string raw)
        {
            if (!PolicyStatusParser.TryParseTelemetry(raw, out PolicyStatusView view, out PolicySafetyNote safety))
                return;

            _telemetrySeen = true;
            Status = view;
            if (SessionKind == PolicySessionKind.Unspecified)
            {
                if (view.Kind == "sim") SessionKind = PolicySessionKind.Sim;
                else if (view.Kind == "real") SessionKind = PolicySessionKind.Real;
            }

            if (safety.HasEstop)
                NoteTelemetryEstop(safety.EstopActive);
            if (safety.HasDeadman)
                NotifyDeadman(safety.DeadmanActive);
        }

        public void IngestAiStatus(string raw)
        {
            if (_telemetrySeen) return;
            if (PolicyStatusParser.TryParseAiStatus(raw, out PolicyStatusView view))
                Status = view;
        }

        public void IngestPolicyModels(string raw)
        {
            _paramRaw = raw ?? "";
            if (_serveListActive) return;
            ReplaceFromParam(_paramRaw);
        }

        public void IngestControlMode(string raw)
        {
            NoteControlModeActive(PolicyText.PeelStringData(raw));
        }

        /// <summary>
        /// The drive scheme (or another owner) took <c>/control_mode</c>.
        /// Disable inference and do not zero the arm the operator may be driving.
        /// </summary>
        public void NoteControlModeActive(string mode)
        {
            if (RunState != PolicyRunState.Running || _handingOver) return;
            if (string.IsNullOrWhiteSpace(mode)) return;
            string normalized = mode.Trim().ToLowerInvariant();
            if (normalized == "vla") return;

            _handingOver = true;
            try
            {
                RunState = PolicyRunState.Stopped;
                Confirmed = false;
                LastStopReason = "external_mode";
                PublishEnable(false);
            }
            finally
            {
                _handingOver = false;
            }
        }

        public string FormatPolicyList()
        {
            if (_policies.Count == 0)
                return "No policies — serve /models empty";

            var builder = new StringBuilder();
            for (int i = 0; i < _policies.Count; i++)
            {
                PolicyDescriptor policy = _policies[i];
                builder.Append(string.Equals(policy.Id, SelectedId, StringComparison.OrdinalIgnoreCase) ? "> " : "  ");
                builder.Append(policy.Label);
                if (!string.Equals(policy.Label, policy.Checkpoint, StringComparison.Ordinal))
                {
                    builder.Append("  ");
                    builder.Append(policy.Checkpoint);
                }
                if (i < _policies.Count - 1) builder.Append('\n');
            }
            return builder.ToString();
        }

        public string FormatStatus()
        {
            if (Status == null || string.IsNullOrEmpty(Status.Source) || Status.Source == "none")
                return RunState + " — no policy telemetry yet";

            string name = string.IsNullOrEmpty(Status.PolicyName) ? (LoadedId ?? "policy") : Status.PolicyName;
            string state = string.IsNullOrEmpty(Status.PolicyState)
                ? RunState.ToString().ToLowerInvariant()
                : Status.PolicyState;
            string latency = Status.LatencyMs.HasValue
                ? Status.LatencyMs.Value.ToString("0.#", CultureInfo.InvariantCulture) + " ms"
                : "latency —";
            PolicyGuardTrips trips = Status.Trips;
            return name + "  " + state + "  " + latency
                + "  black " + Count(trips.HasBlackFrame, trips.BlackFrame)
                + "  stale " + Count(trips.HasStaleInference, trips.StaleInference)
                + "  clamp " + Count(trips.HasClampedDeltas, trips.ClampedDeltas)
                + "  [" + Status.Source + "]";
        }

        public string FormatGate()
        {
            if (EmergencyStopActive) return "E-stop latched — policy blocked";
            if (DeadmanActive) return "Deadman open — policy blocked";
            if (RequiresSupervise)
                return SuperviseHeld ? "REAL — supervise held" : "REAL — hold supervise to arm";
            return "SIM — supervise not required";
        }

        private void NoteTelemetryEstop(bool active)
        {
            _telemetryEstop = active;
            if (EmergencyStopActive && RunState == PolicyRunState.Running)
                Handover("estop", true);
        }

        private void EnsureListening()
        {
            if (_listening || _link == null || _profile == null || _profile.Topics == null) return;
            _listening = true;
            // Do not unsubscribe later. IRobotLink.Unsubscribe drops every
            // callback on the topic, including the HUD.
            Listen(_profile.Topics.Telemetry, IngestTelemetry);
            Listen(_profile.Topics.AiStatus, IngestAiStatus);
            Listen(_profile.Topics.PolicyModels, IngestPolicyModels);
            Listen(_profile.Topics.ControlModeActive, IngestControlMode);
        }

        private void Listen(string topic, Action<string> callback)
        {
            if (string.IsNullOrEmpty(topic) || _link == null) return;
            _link.Subscribe(topic, "std_msgs/String", callback);
        }

        private void ReplaceFromParam(string raw)
        {
            var parsed = PolicyCatalog.Parse(raw);
            _policies.Clear();
            _policies.AddRange(parsed);
            if (Find(SelectedId) == null) SelectedId = null;
        }

        private PolicyDescriptor Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _policies.Count; i++)
            {
                if (string.Equals(_policies[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return _policies[i];
            }
            return null;
        }

        private void Handover(string reason, bool zeroArm)
        {
            if (_handingOver) return;
            _handingOver = true;
            try
            {
                RunState = PolicyRunState.Stopped;
                Confirmed = false;
                LastStopReason = reason;
                if (!Connected || _profile == null || _profile.Topics == null) return;

                RobotTopics topics = _profile.Topics;
                // Teleop first so arm_cmd_mux holds policy before the zeros land.
                PublishString(topics.ControlMode, "teleop");
                PublishEnable(false);
                PublishZeroTwist(topics.CmdVel);
                PublishZeroTwist(topics.PolicyCmdVel);
                if (zeroArm)
                    PublishZeroArm(topics.ArmCommands);
            }
            finally
            {
                _handingOver = false;
            }
        }

        private PolicyCommandResult Refuse(string reason)
        {
            LastRefusal = reason;
            return PolicyCommandResult.Refuse(reason);
        }

        private bool Connected => _link != null && _link.IsConnected;

        private void PublishString(string topic, string data)
        {
            if (!Connected || string.IsNullOrEmpty(topic)) return;
            _link.Advertise(topic, "std_msgs/String");
            _link.Publish(topic, new StringMsg(data));
        }

        private void PublishEnable(bool enabled)
        {
            string topic = _profile != null && _profile.Topics != null ? _profile.Topics.PolicyEnable : null;
            if (!Connected || string.IsNullOrEmpty(topic)) return;
            _link.Advertise(topic, "std_msgs/Bool");
            _link.Publish(topic, new BoolMsg(enabled));
        }

        private void PublishZeroTwist(string topic)
        {
            if (!Connected || string.IsNullOrEmpty(topic)) return;
            _link.Advertise(topic, "geometry_msgs/Twist");
            _link.Publish(topic, new TwistMsg(new Vector3Msg(0f, 0f, 0f), new Vector3Msg(0f, 0f, 0f)));
        }

        private void PublishZeroArm(string topic)
        {
            if (!Connected || string.IsNullOrEmpty(topic) || _profile == null) return;
            string[] names;
            if (_profile.Joints != null && _profile.Joints.Length > 0)
            {
                names = new string[_profile.Joints.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = string.IsNullOrEmpty(_profile.Joints[i].Name)
                        ? "joint_" + (i + 1)
                        : _profile.Joints[i].Name;
            }
            else
            {
                int count = _profile.ArmDof > 0 ? _profile.ArmDof : 0;
                names = new string[count];
                for (int i = 0; i < count; i++) names[i] = "joint_" + (i + 1);
            }
            if (names.Length == 0) return;
            _link.Advertise(topic, "sensor_msgs/JointState");
            _link.Publish(topic, new JointStateMsg(names, new float[names.Length]));
        }

        private static string Count(bool has, int value) => has ? value.ToString(CultureInfo.InvariantCulture) : "—";
    }
}
