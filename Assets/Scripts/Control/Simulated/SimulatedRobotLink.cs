using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// In-headset <see cref="IRobotLink"/>. No socket. Mecanum kinematics and
    /// rate-limited arm joints run on the headset so teleop works in airplane
    /// mode and in EditMode tests.
    ///
    /// Deadman matches <c>lib/connect/teleop.ts</c>: the base moves only while
    /// the deadman is held and e-stop is clear. Dropping the deadman commands
    /// zero. E-stop latches zero velocity and freezes the arm until cleared.
    /// Speed cannot exceed <see cref="RobotConfig.MAX_LINEAR_VEL"/> per axis
    /// or <see cref="RobotConfig.MAX_ANGULAR_VEL"/> in yaw. Joints slew at that
    /// yaw cap and stay inside their position limits.
    ///
    /// Outbound odom, joint states, IMU, and <c>/sim/telemetry</c> carry
    /// <c>kind=sim</c>. Select this link with
    /// <see cref="RobotLinkFactory.Select"/>
    /// (<see cref="RobotLinkTarget.InHeadset"/>).
    /// </summary>
    public sealed class SimulatedRobotLink : IRobotLink
    {
        public const string Kind = "sim";
        public const string DeadmanTopic = "/deadman";
        public const string TelemetryTopic = "/sim/telemetry";

        readonly Dictionary<string, List<Action<string>>> _subscribers =
            new Dictionary<string, List<Action<string>>>();
        readonly HashSet<string> _advertised = new HashSet<string>();

        readonly string _cmdTopic;
        readonly string _odomTopic;
        readonly string _jointStateTopic;
        readonly string _armCmdTopic;
        readonly string _armEnableTopic;
        readonly string _estopTopic;
        readonly string _imuTopic;
        readonly string _controlModeTopic;
        readonly double _maxLin;
        readonly double _maxAng;
        readonly double _jointRate;

        string[] _names = Array.Empty<string>();
        double[] _min = Array.Empty<double>();
        double[] _max = Array.Empty<double>();
        double[] _home = Array.Empty<double>();
        double[] _jointPos = Array.Empty<double>();
        double[] _jointTarget = Array.Empty<double>();
        double[] _jointVel = Array.Empty<double>();

        SimPose _pose;
        SimVelocity _applied;
        double _simTime;
        bool _zeroAlreadySent = true;
        uint _seq;

        public bool IsConnected { get; private set; }
        public bool DeadmanHeld { get; private set; }
        public bool EstopLatched { get; private set; }
        public bool ArmEnabled { get; private set; }
        public string LastControlMode { get; private set; } = "";
        public SimPose Pose => _pose;
        public SimVelocity Applied => _applied;
        public double SimTime => _simTime;
        public int JointCount => _jointPos.Length;
        public double MaxLinearMps => _maxLin;
        public double MaxAngularRadPerSec => _maxAng;

        public SimulatedRobotLink(RobotProfile profile = null)
        {
            bool hasProfile = profile != null;
            _maxLin = SimulatedSafetyCaps.Resolve(
                profile != null ? profile.MaxLinVel : 0f,
                RobotConfig.MAX_LINEAR_VEL,
                hasProfile);
            _maxAng = SimulatedSafetyCaps.Resolve(
                profile != null ? profile.MaxAngVel : 0f,
                RobotConfig.MAX_ANGULAR_VEL,
                hasProfile);
            _jointRate = SimulatedSafetyCaps.HardMaxJointRateRadPerSec;

            var topics = profile?.Topics;
            _cmdTopic = Or(topics?.CmdVel, RobotConfig.TOPIC_CMD_VEL);
            _odomTopic = Or(topics?.Odom, RobotConfig.TOPIC_ODOM);
            _jointStateTopic = Or(topics?.JointStates, RobotConfig.TOPIC_ARM_STATES);
            _armCmdTopic = Or(topics?.ArmCommands, RobotConfig.TOPIC_ARM_COMMANDS);
            _armEnableTopic = Or(topics?.ArmEnable, RobotConfig.TOPIC_ARM_ENABLE);
            _estopTopic = Or(topics?.EmergencyStop, RobotConfig.TOPIC_EMERGENCY_STOP);
            _imuTopic = Or(topics?.Imu, RobotConfig.TOPIC_IMU);
            _controlModeTopic = Or(topics?.ControlMode, RobotConfig.TOPIC_CONTROL_MODE);
            ConfigureJoints(profile);
        }

        static string Or(string value, string fallback)
            => string.IsNullOrEmpty(value) ? fallback : value;

        public void Connect()
        {
            IsConnected = true;
            DeadmanHeld = false;
            EstopLatched = false;
            ArmEnabled = false;
            LastControlMode = "";
            _applied = SimVelocity.Zero;
            _zeroAlreadySent = true;
            _pose = default;
            _simTime = 0;
            _seq = 0;
            for (int i = 0; i < _jointPos.Length; i++)
            {
                _jointPos[i] = ClampJoint(_home[i], _min[i], _max[i]);
                _jointTarget[i] = _jointPos[i];
                _jointVel[i] = 0;
            }
        }

        public void Disconnect()
        {
            if (!IsConnected) return;
            _applied = SimVelocity.Zero;
            DeadmanHeld = false;
            PublishGatedCommand();
            IsConnected = false;
        }

        /// <summary>
        /// Deadman (left index trigger / Space / <see cref="DeadmanTopic"/>).
        /// Releasing it commands zero and does not resume the previous twist.
        /// </summary>
        public void SetDeadman(bool held)
        {
            if (DeadmanHeld == held) return;
            DeadmanHeld = held;
            if (!held)
            {
                _applied = SimVelocity.Zero;
                PublishGatedCommand();
            }
        }

        public void EmergencyStop()
        {
            _applied = SimVelocity.Zero;
            FreezeArm();
            if (EstopLatched) return;
            EstopLatched = true;
            PublishGatedCommand();
        }

        public void ReleaseStop()
        {
            EstopLatched = false;
        }

        public void SetArmEnabled(bool enabled) => ArmEnabled = enabled;

        /// <summary>Direct velocity command. Deadman, e-stop, and caps apply.</summary>
        public void CommandVelocity(double linearX, double linearY, double angularZ)
        {
            if (!IsConnected) return;
            var requested = new SimVelocity(linearX, linearY, angularZ);
            var gated = SimulatedTeleopGate.Apply(requested, DeadmanHeld, EstopLatched);
            _applied = SimulatedSafetyCaps.ClampBase(gated, _maxLin, _maxAng);
            PublishGatedCommand();
        }

        /// <summary>Joint position targets. Ignored while e-stop is latched. Slew runs on <see cref="Tick"/>.</summary>
        public void CommandJoints(string[] names, double[] positions)
        {
            if (!IsConnected || EstopLatched || positions == null || _jointTarget.Length == 0) return;

            if (names != null && names.Length > 0)
            {
                int n = Math.Min(names.Length, positions.Length);
                for (int i = 0; i < n; i++)
                {
                    int idx = IndexOfJoint(names[i]);
                    if (idx >= 0) _jointTarget[idx] = positions[i];
                }
                return;
            }

            int m = Math.Min(positions.Length, _jointTarget.Length);
            for (int i = 0; i < m; i++) _jointTarget[i] = positions[i];
        }

        public double GetJointPosition(int index)
        {
            if (index < 0 || index >= _jointPos.Length) return 0;
            return _jointPos[index];
        }

        public string GetJointName(int index)
        {
            if (index < 0 || index >= _names.Length) return "";
            return _names[index] ?? "";
        }

        /// <summary>Advance the kinematic sim by <paramref name="dt"/> seconds and publish telemetry.</summary>
        public void Tick(double dt)
        {
            if (!IsConnected || dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;

            if (!EstopLatched) SlewJoints(dt);
            _pose = MecanumSimKinematics.Integrate(_pose, _applied, dt);
            _simTime += dt;
            PublishTelemetry();
        }

        public bool IsAdvertised(string topic) => topic != null && _advertised.Contains(topic);

        public void Advertise(string topic, string type)
        {
            if (!string.IsNullOrEmpty(topic)) _advertised.Add(topic);
        }

        public void Publish<T>(string topic, T msg)
        {
            if (!IsConnected || string.IsNullOrEmpty(topic) || msg == null) return;

            if (msg is TwistMsg twist && IsCmdVel(topic))
            {
                double lx = twist.linear != null ? twist.linear.x : 0;
                double ly = twist.linear != null ? twist.linear.y : 0;
                double wz = twist.angular != null ? twist.angular.z : 0;
                CommandVelocity(lx, ly, wz);
                return;
            }

            if (msg is JointStateMsg joints && IsArmCommand(topic))
            {
                double[] positions = ToDoubles(joints.position);
                CommandJoints(joints.name, positions);
                return;
            }

            if (msg is BoolMsg flag)
            {
                if (IsEstop(topic))
                {
                    if (flag.data) EmergencyStop();
                    else ReleaseStop();
                    Notify(topic, WithKind(flag));
                    return;
                }
                if (IsDeadman(topic))
                {
                    SetDeadman(flag.data);
                    Notify(topic, WithKind(flag));
                    return;
                }
                if (IsArmEnable(topic))
                {
                    ArmEnabled = flag.data;
                    Notify(topic, WithKind(flag));
                    return;
                }
            }

            if (msg is StringMsg text && IsControlMode(topic))
            {
                LastControlMode = text.data ?? "";
                string json = WithKind(text);
                Notify(topic, json);
                if (!string.Equals(topic, RobotConfig.TOPIC_CONTROL_MODE_ACTIVE, StringComparison.Ordinal))
                    Notify(RobotConfig.TOPIC_CONTROL_MODE_ACTIVE, json);
                return;
            }

            Notify(topic, WithKind(msg));
        }

        public void Subscribe(string topic, string type, Action<string> callback)
        {
            if (string.IsNullOrEmpty(topic) || callback == null) return;
            if (!_subscribers.TryGetValue(topic, out var list))
            {
                list = new List<Action<string>>();
                _subscribers[topic] = list;
            }
            list.Add(callback);
        }

        public void Unsubscribe(string topic)
        {
            if (!string.IsNullOrEmpty(topic)) _subscribers.Remove(topic);
        }

        void ConfigureJoints(RobotProfile profile)
        {
            if (profile?.Joints != null && profile.Joints.Length > 0)
            {
                int n = profile.Joints.Length;
                AllocJoints(n);
                for (int i = 0; i < n; i++)
                {
                    var joint = profile.Joints[i];
                    _names[i] = string.IsNullOrEmpty(joint.Name) ? "joint_" + i : joint.Name;
                    _min[i] = joint.Min;
                    _max[i] = joint.Max;
                    if (_min[i] > _max[i])
                    {
                        double swap = _min[i];
                        _min[i] = _max[i];
                        _max[i] = swap;
                    }
                    _home[i] = joint.Home;
                    _jointPos[i] = ClampJoint(_home[i], _min[i], _max[i]);
                    _jointTarget[i] = _jointPos[i];
                }
                return;
            }

            int count = RobotConfig.ARM_JOINT_NAMES.Length;
            AllocJoints(count);
            for (int i = 0; i < count; i++)
            {
                _names[i] = RobotConfig.ARM_JOINT_NAMES[i];
                _min[i] = RobotConfig.ARM_JOINT_MIN[i];
                _max[i] = RobotConfig.ARM_JOINT_MAX[i];
                _home[i] = 0;
                _jointPos[i] = 0;
                _jointTarget[i] = 0;
            }
        }

        void AllocJoints(int n)
        {
            _names = new string[n];
            _min = new double[n];
            _max = new double[n];
            _home = new double[n];
            _jointPos = new double[n];
            _jointTarget = new double[n];
            _jointVel = new double[n];
        }

        void SlewJoints(double dt)
        {
            double maxStep = _jointRate * dt;
            if (maxStep < 0) maxStep = 0;
            for (int i = 0; i < _jointPos.Length; i++)
            {
                double delta = _jointTarget[i] - _jointPos[i];
                if (delta > maxStep) delta = maxStep;
                else if (delta < -maxStep) delta = -maxStep;
                double next = ClampJoint(_jointPos[i] + delta, _min[i], _max[i]);
                _jointVel[i] = (next - _jointPos[i]) / dt;
                _jointPos[i] = next;
            }
        }

        void FreezeArm()
        {
            for (int i = 0; i < _jointPos.Length; i++)
            {
                _jointTarget[i] = _jointPos[i];
                _jointVel[i] = 0;
            }
        }

        void PublishGatedCommand()
        {
            var decision = SimulatedTeleopGate.ShouldPublish(DeadmanHeld, EstopLatched, _zeroAlreadySent);
            if (!decision.Publish) return;
            _zeroAlreadySent = decision.ZeroAlreadySent;
            Notify(_cmdTopic, WithKind(MakeTwist(_applied)));
        }

        void PublishTelemetry()
        {
            _seq++;
            var stamp = MakeStamp(_simTime);
            var orientation = YawQuaternion(_pose.Theta);

            if (!string.IsNullOrEmpty(_odomTopic))
            {
                var odom = new OdometryMsg();
                odom.header.seq = _seq;
                odom.header.stamp = stamp;
                odom.header.frame_id = "odom";
                odom.child_frame_id = "base_link";
                odom.pose.pose.position = new Vector3Msg((float)_pose.X, (float)_pose.Y, 0f);
                odom.pose.pose.orientation = orientation;
                odom.twist.twist = MakeTwist(_applied);
                Notify(_odomTopic, WithKind(odom));
            }

            if (!string.IsNullOrEmpty(_jointStateTopic) && _jointPos.Length > 0)
            {
                var positions = new float[_jointPos.Length];
                var velocities = new float[_jointVel.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = (float)_jointPos[i];
                    velocities[i] = (float)_jointVel[i];
                }
                var joints = new JointStateMsg(_names, positions);
                joints.header.seq = _seq;
                joints.header.stamp = stamp;
                joints.header.frame_id = "sim";
                joints.velocity = velocities;
                Notify(_jointStateTopic, WithKind(joints));
            }

            if (!string.IsNullOrEmpty(_imuTopic))
            {
                var imu = new ImuMsg();
                imu.header.seq = _seq;
                imu.header.stamp = stamp;
                imu.header.frame_id = "sim";
                imu.orientation = orientation;
                imu.angular_velocity = new Vector3Msg(0f, 0f, (float)_applied.AngularZ);
                imu.linear_acceleration = new Vector3Msg(0f, 0f, 9.81f);
                Notify(_imuTopic, WithKind(imu));
            }

            Notify(TelemetryTopic, JsonConvert.SerializeObject(new SimTelemetry
            {
                kind = Kind,
                protocol = "simulated",
                label = "Simulated robot",
                battery = SimulatedVectors.Battery,
                latencyMs = SimulatedVectors.ConnectedLatencyMs,
                msgRate = SimulatedVectors.ConnectedMsgRateHz,
                deadmanHeld = DeadmanHeld,
                estopLatched = EstopLatched,
                armEnabled = ArmEnabled,
                x = _pose.X,
                y = _pose.Y,
                theta = _pose.Theta,
            }));
        }

        void Notify(string topic, string json)
        {
            if (string.IsNullOrEmpty(topic) || json == null) return;
            if (!_subscribers.TryGetValue(topic, out var list) || list.Count == 0) return;
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i]?.Invoke(json); }
                catch (Exception ex)
                {
                    Debug.LogError($"[SimulatedRobotLink] Subscriber failed on {topic}: {ex.Message}");
                }
            }
        }

        bool IsCmdVel(string topic)
            => string.Equals(topic, _cmdTopic, StringComparison.Ordinal)
               || IndexOf(topic, "cmd_vel");

        bool IsArmCommand(string topic)
            => string.Equals(topic, _armCmdTopic, StringComparison.Ordinal)
               || IndexOf(topic, "joint_command");

        bool IsEstop(string topic)
            => string.Equals(topic, _estopTopic, StringComparison.Ordinal)
               || IndexOf(topic, "emergency_stop");

        bool IsDeadman(string topic)
            => string.Equals(topic, DeadmanTopic, StringComparison.Ordinal)
               || IndexOf(topic, "deadman");

        bool IsArmEnable(string topic)
            => string.Equals(topic, _armEnableTopic, StringComparison.Ordinal);

        bool IsControlMode(string topic)
            => string.Equals(topic, _controlModeTopic, StringComparison.Ordinal);

        static bool IndexOf(string topic, string needle)
            => topic != null && topic.IndexOf(needle, StringComparison.Ordinal) >= 0;

        int IndexOfJoint(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < _names.Length; i++)
            {
                if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        static double ClampJoint(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        static double[] ToDoubles(float[] values)
        {
            if (values == null) return null;
            var copy = new double[values.Length];
            for (int i = 0; i < values.Length; i++) copy[i] = values[i];
            return copy;
        }

        static TwistMsg MakeTwist(SimVelocity velocity)
        {
            return new TwistMsg(
                new Vector3Msg((float)velocity.LinearX, (float)velocity.LinearY, 0f),
                new Vector3Msg(0f, 0f, (float)velocity.AngularZ));
        }

        static QuaternionMsg YawQuaternion(double theta)
        {
            double half = theta * 0.5;
            return new QuaternionMsg(0f, 0f, (float)Math.Sin(half), (float)Math.Cos(half));
        }

        static TimeMsg MakeStamp(double time)
        {
            if (time < 0 || double.IsNaN(time) || double.IsInfinity(time)) time = 0;
            int secs = (int)Math.Floor(time);
            int nsecs = (int)Math.Round((time - secs) * 1e9);
            if (nsecs >= 1000000000)
            {
                secs++;
                nsecs = 0;
            }
            return new TimeMsg(secs, nsecs);
        }

        static string WithKind(object msg)
        {
            var jo = JObject.FromObject(msg);
            jo["kind"] = Kind;
            return jo.ToString(Formatting.None);
        }
    }

    /// <summary>
    /// <c>kind=sim</c> snapshot published on <see cref="SimulatedRobotLink.TelemetryTopic"/>.
    /// Battery, latency, and rate match <c>lib/connect/simulated.ts</c>.
    /// </summary>
    public sealed class SimTelemetry
    {
        [JsonProperty("kind")] public string kind = SimulatedRobotLink.Kind;
        [JsonProperty("protocol")] public string protocol = "simulated";
        [JsonProperty("label")] public string label = "Simulated robot";
        [JsonProperty("battery")] public double battery = SimulatedVectors.Battery;
        [JsonProperty("latencyMs")] public int latencyMs = SimulatedVectors.ConnectedLatencyMs;
        [JsonProperty("msgRate")] public int msgRate = SimulatedVectors.ConnectedMsgRateHz;
        [JsonProperty("deadmanHeld")] public bool deadmanHeld;
        [JsonProperty("estopLatched")] public bool estopLatched;
        [JsonProperty("armEnabled")] public bool armEnabled;
        [JsonProperty("x")] public double x;
        [JsonProperty("y")] public double y;
        [JsonProperty("theta")] public double theta;
    }
}
