using System;
using System.Text;
using Ohho.Schemas;

namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// Builds the teleop HUD text from <c>/ohho/telemetry</c>, with profile-topic
    /// fallback and a local echo of what the headset published.
    ///
    /// <see cref="ConsumeFrame"/> does no work and no allocation until 100 ms
    /// have accumulated (10 Hz). Formatting appends into <see cref="Body"/>.
    /// Nothing in this type publishes a command or changes a safety default.
    /// </summary>
    public sealed class TelemetryHudComposer
    {
        const int MaxJoints = 16;

        readonly FieldAges _ages = new FieldAges();
        readonly FieldAges _scratchAges = new FieldAges();
        readonly RateTracker _telemetryRate = new RateTracker();
        readonly RateTracker _odomRate = new RateTracker();
        readonly RateTracker _jointRate = new RateTracker();
        readonly RateTracker _imuRate = new RateTracker();
        readonly RateTracker _safetyRate = new RateTracker();
        readonly RateTracker _videoRate = new RateTracker();
        readonly RateTracker _echoRate = new RateTracker();

        readonly string[] _rawNames = new string[MaxJoints];
        readonly double[] _rawPos = new double[MaxJoints];
        readonly double[] _rawVel = new double[MaxJoints];
        readonly double[] _rawEff = new double[MaxJoints];
        readonly bool[] _rawHasVel = new bool[MaxJoints];
        readonly bool[] _rawHasEff = new bool[MaxJoints];

        struct SentBeat
        {
            public long Seq;
            public double Time;
            public bool Valid;
        }

        readonly SentBeat[] _sent = new SentBeat[8];

        TelemetryV1 _sample;
        CommandEcho _echo;
        SafetyReading _telemetrySafety;
        SafetyReading _directSafety;

        double _now;
        double _telemetryTime;
        double _directSafetyTime;
        double _rawBaseTime;
        double _rawJointTime;
        double _rawImuTime;
        double _maxLin = 0.2;
        double _maxAng = 1.0;
        double _rawX, _rawY, _rawYaw, _rawVx, _rawVy, _rawWz;
        double _imuRoll, _imuPitch, _imuYaw, _imuAx, _imuAy, _imuAz;
        double _videoAgeSec;
        double _lastVideoFrame = double.NaN;
        double _echoRttMs;
        float _accum;
        int _refreshCount;
        int _rawJointCount;
        int _sentHead;
        bool _hasTelemetry;
        bool _hasDirectSafety;
        bool _hasRawBase;
        bool _rawHasPose;
        bool _rawHasYaw;
        bool _rawHasTwist;
        bool _hasRawJoints;
        bool _hasRawImu;
        bool _hasVideo;
        bool _hasEchoRtt;
        bool _armPresent = true;
        bool _hasRawBaseMode;
        bool _hasRawArmMode;
        bool _lineColored;
        string _baseModeText;
        string _armModeText;
        string _rawControl;
        string _rawArm;
        string _videoTopic;
        long _echoSeq;

        public TelemetryHudComposer()
        {
            Expanded = true;
        }

        public bool Expanded { get; set; }
        public StringBuilder Body { get; } = new StringBuilder(8192);
        public int RefreshCount { get { return _refreshCount; } }
        public bool UsingTelemetry { get; private set; }
        public double MaxLinear { get { return _maxLin; } }
        public double MaxAngular { get { return _maxAng; } }
        public string Text { get { return Body.ToString(); } }

        public CommandEcho Echo
        {
            get { return _echo ?? CommandEcho.Shared; }
            set { _echo = value; }
        }

        public bool IngestTelemetry(string json, double nowSec)
        {
            TelemetryV1 sample;
            if (!TelemetryIngest.TryParseTelemetry(json, _scratchAges, out sample))
                return false;
            _sample = sample;
            _ages.CopyFrom(_scratchAges);
            _hasTelemetry = true;
            _telemetryTime = nowSec;
            _telemetrySafety = SafetyFromSample(sample, _ages);
            _telemetryRate.Mark(nowSec);
            return true;
        }

        public bool IngestSafety(string json, double nowSec)
        {
            SafetyReading reading;
            if (!TelemetryIngest.TryParseSafety(json, out reading)) return false;
            _directSafety = reading;
            _hasDirectSafety = true;
            _directSafetyTime = nowSec;
            _safetyRate.Mark(nowSec);
            return true;
        }

        public bool IngestHeartbeatEcho(string json, double nowSec)
        {
            HeartbeatEcho echo;
            if (!TelemetryIngest.TryParseEcho(json, out echo)) return false;
            _echoRate.Mark(nowSec);
            _echoSeq = echo.Seq;
            for (int i = 0; i < _sent.Length; i++)
            {
                if (!_sent[i].Valid || _sent[i].Seq != echo.Seq) continue;
                double rtt = (nowSec - _sent[i].Time) * 1000.0;
                if (rtt >= 0 && rtt < 10000)
                {
                    _hasEchoRtt = true;
                    _echoRttMs = rtt;
                }
                break;
            }
            return true;
        }

        public void NoteHeartbeatSent(long seq, double nowSec)
        {
            _sent[_sentHead].Seq = seq;
            _sent[_sentHead].Time = nowSec;
            _sent[_sentHead].Valid = true;
            _sentHead++;
            if (_sentHead >= _sent.Length) _sentHead = 0;
        }

        public void NoteRawBase(
            double x, double y, double qx, double qy, double qz, double qw,
            double vx, double vy, double wz, bool hasPose, bool hasTwist, double nowSec)
        {
            _rawX = x;
            _rawY = y;
            _rawHasPose = hasPose;
            _rawHasYaw = hasPose && HudText.TryRpy(qx, qy, qz, qw, out _, out _, out _rawYaw);
            if (!_rawHasYaw) _rawYaw = 0;
            _rawVx = vx;
            _rawVy = vy;
            _rawWz = wz;
            _rawHasTwist = hasTwist;
            _rawBaseTime = nowSec;
            _hasRawBase = hasPose || hasTwist;
            if (hasTwist) _odomRate.Mark(nowSec);
        }

        public void BeginRawJoints(double nowSec)
        {
            _rawJointCount = 0;
            _rawJointTime = nowSec;
            _hasRawJoints = false;
            _jointRate.Mark(nowSec);
        }

        public void AddRawJoint(string name, double pos, double vel, double effort, bool hasVel, bool hasEffort)
        {
            if (_rawJointCount >= MaxJoints) return;
            int i = _rawJointCount++;
            _rawNames[i] = name;
            _rawPos[i] = pos;
            _rawVel[i] = vel;
            _rawEff[i] = effort;
            _rawHasVel[i] = hasVel;
            _rawHasEff[i] = hasEffort;
            _hasRawJoints = true;
        }

        public void NoteRawImu(double qx, double qy, double qz, double qw, double ax, double ay, double az, double nowSec)
        {
            if (!HudText.TryRpy(qx, qy, qz, qw, out _imuRoll, out _imuPitch, out _imuYaw)) return;
            _imuAx = ax;
            _imuAy = ay;
            _imuAz = az;
            _hasRawImu = true;
            _rawImuTime = nowSec;
            _imuRate.Mark(nowSec);
        }

        public void NoteRawBaseMode(string mode, double nowSec)
        {
            _rawControl = mode;
            _hasRawBaseMode = !string.IsNullOrEmpty(mode);
            _baseModeText = _hasRawBaseMode ? ModeMap.Base(mode) : null;
            if (_hasRawArmMode || _hasRawBaseMode)
                _armModeText = ModeMap.Arm(_rawControl, _rawArm, _armPresent);
            if (_hasRawBaseMode) _safetyRate.Mark(nowSec);
        }

        public void NoteRawArmMode(string mode, double nowSec)
        {
            _rawArm = mode;
            _hasRawArmMode = !string.IsNullOrEmpty(mode);
            _armModeText = ModeMap.Arm(_rawControl, _rawArm, _armPresent);
        }

        public void NoteVideo(bool hasFrame, double frameTimeSec, string topic, double nowSec)
        {
            _hasVideo = hasFrame;
            if (hasFrame)
            {
                _videoAgeSec = nowSec - frameTimeSec;
                if (_videoAgeSec < 0) _videoAgeSec = 0;
                if (frameTimeSec != _lastVideoFrame)
                {
                    _lastVideoFrame = frameTimeSec;
                    _videoRate.Mark(frameTimeSec);
                }
            }
            if (!string.IsNullOrEmpty(topic) && !string.Equals(_videoTopic, topic, StringComparison.Ordinal))
                _videoTopic = topic;
        }

        public void SetHardCaps(double maxLinearMps, double maxAngularRadPerSec)
        {
            if (maxLinearMps > 0 && !double.IsNaN(maxLinearMps) && !double.IsInfinity(maxLinearMps))
                _maxLin = maxLinearMps;
            if (maxAngularRadPerSec > 0 && !double.IsNaN(maxAngularRadPerSec) && !double.IsInfinity(maxAngularRadPerSec))
                _maxAng = maxAngularRadPerSec;
        }

        public void SetArmPresent(bool armPresent)
        {
            if (_armPresent == armPresent) return;
            _armPresent = armPresent;
            if (_hasRawBaseMode || _hasRawArmMode)
                _armModeText = ModeMap.Arm(_rawControl, _rawArm, _armPresent);
        }

        public bool ConsumeFrame(float deltaSeconds, double nowSec)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;
            _accum += deltaSeconds;
            if (_accum < TelemetryTopics.UiPeriodSeconds) return false;
            _accum -= TelemetryTopics.UiPeriodSeconds;
            if (_accum > TelemetryTopics.UiPeriodSeconds) _accum = 0f;
            Write(nowSec);
            _refreshCount++;
            return true;
        }

        public void Write(double nowSec)
        {
            Prepare(nowSec);
            Body.Length = 0;
            WriteHeader();
            WriteBase();
            WriteSent();
            WriteMode();
            WriteSafety();
            WriteImu(false);
            WriteBattery();
            WriteLink();
            WriteVideo();
            WriteGripper();
            if (Expanded)
            {
                WriteJoints();
                WriteImu(true);
                WriteNav();
                WritePolicy();
                WriteRec();
                WriteCpu();
                WriteStreams();
            }
        }

        public void WriteLegacy(double nowSec, StringBuilder pose, StringBuilder vel, StringBuilder arm, StringBuilder mode)
        {
            Prepare(nowSec);
            double cvx, cvy, cwz, mvx, mvy, mwz, x, y, yaw;
            bool hasCmd, hasMeas, hasPose, hasYaw;
            string frame;
            LoadBase(out cvx, out cvy, out cwz, out mvx, out mvy, out mwz, out x, out y, out yaw, out hasCmd, out hasMeas, out hasPose, out hasYaw, out frame);
            if (pose != null)
            {
                pose.Length = 0;
                if (!hasPose) pose.Append("x n/a  y n/a");
                else
                {
                    pose.Append("x ");
                    HudText.AppendFixed(pose, x, 2);
                    pose.Append(" m  y ");
                    HudText.AppendFixed(pose, y, 2);
                    pose.Append(" m");
                }
            }
            if (vel != null)
            {
                vel.Length = 0;
                vel.Append("vx ");
                if (hasMeas) HudText.AppendFixed(vel, mvx, 2); else vel.Append("n/a");
                vel.Append("  vy ");
                if (hasMeas) HudText.AppendFixed(vel, mvy, 2); else vel.Append("n/a");
                vel.Append("  wz ");
                if (hasMeas) HudText.AppendFixed(vel, mwz, 2); else vel.Append("n/a");
            }
            if (arm != null)
            {
                arm.Length = 0;
                arm.Append("arm ");
                if (ShowSample && _sample != null && _sample.Joints != null && _sample.Joints.Length > 0)
                {
                    HudText.AppendFixed(arm, HudText.ToDeg(_sample.Joints[0].Pos), 1);
                    arm.Append(" deg");
                }
                else if (!ShowSample && _hasRawJoints)
                {
                    HudText.AppendFixed(arm, HudText.ToDeg(_rawPos[0]), 1);
                    arm.Append(" deg");
                }
                else arm.Append("n/a");
            }
            if (mode != null)
            {
                mode.Length = 0;
                mode.Append("base ");
                AppendBaseMode(mode);
                mode.Append("  arm ");
                AppendArmMode(mode);
            }
        }

        bool ShowSample
        {
            get
            {
                return UsingTelemetry || (_hasTelemetry && !_hasRawBase && !_hasRawJoints && !_hasRawImu);
            }
        }

        void Prepare(double nowSec)
        {
            _now = nowSec;
            UsingTelemetry = _hasTelemetry && (nowSec - _telemetryTime) <= TelemetryTopics.FreshSeconds;
        }

        static SafetyReading SafetyFromSample(TelemetryV1 sample, FieldAges ages)
        {
            SafetyReading reading = new SafetyReading();
            reading.Measured = !ages.HasAgeObject || ages.Estop.Present || ages.Deadman.Present;
            reading.Deadman = sample.Deadman;
            if (sample.Estop != null)
            {
                reading.Active = sample.Estop.Active;
                reading.Latched = sample.Estop.Latched;
                reading.Source = sample.Estop.Source;
            }
            if (sample.Mode != null)
            {
                reading.HasMode = true;
                reading.BaseMode = sample.Mode.Base;
                reading.ArmMode = sample.Mode.Arm;
            }
            if (sample.Link != null)
            {
                reading.HasLink = true;
                reading.RttMs = sample.Link.RttMs;
                reading.Hz = sample.Link.Hz;
            }
            return reading;
        }

        void WriteHeader()
        {
            bool staleHold = ShowSample && !UsingTelemetry;
            BeginLine(staleHold, LimitZone.Ok);
            if (UsingTelemetry) Body.Append("src telemetry");
            else if (ShowSample) Body.Append("src telemetry-stale");
            else if (_hasRawBase || _hasRawJoints || _hasRawImu) Body.Append("src profile");
            else Body.Append("src none");
            if (ShowSample && _sample != null)
            {
                Body.Append(" v ");
                HudText.AppendText(Body, _sample.V);
                Body.Append(' ');
                HudText.AppendText(Body, _sample.RobotId);
                Body.Append(' ');
                AppendKind(_sample.Kind);
                Body.Append(" t ");
                HudText.AppendFixed(Body, _sample.T, 1);
            }
            EndLine();
        }

        void WriteBase()
        {
            double cvx, cvy, cwz, mvx, mvy, mwz, x, y, yaw;
            bool hasCmd, hasMeas, hasPose, hasYaw;
            string frame;
            LoadBase(out cvx, out cvy, out cwz, out mvx, out mvy, out mwz, out x, out y, out yaw, out hasCmd, out hasMeas, out hasPose, out hasYaw, out frame);
            bool stale = BaseIsStale();
            bool over = OverCap(cvx, cvy, cwz, mvx, mvy, mwz, hasCmd, hasMeas);
            LimitZone zone = over ? LimitZone.Red : LimitZone.Ok;
            BeginLine(stale, zone);
            Body.Append("BASE");
            if (!hasCmd && !hasMeas && !hasPose)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            AppendAxisNumbers(" vx", cvx, mvx, hasCmd, hasMeas);
            AppendAxisNumbers(" vy", cvy, mvy, hasCmd, hasMeas);
            AppendAxisNumbers(" wz", cwz, mwz, hasCmd, hasMeas);
            Body.Append(" speed ");
            if (hasMeas) HudText.AppendFixed(Body, HudText.PlanarSpeed(mvx, mvy), 2);
            else Body.Append("n/a");
            Body.Append(" cap ");
            HudText.AppendFixed(Body, _maxLin, 2);
            Body.Append(" wcap ");
            HudText.AppendFixed(Body, _maxAng, 2);
            if (over) Body.Append(" OVER");
            if (hasPose)
            {
                Body.Append(" pose x ");
                HudText.AppendFixed(Body, x, 2);
                Body.Append(" y ");
                HudText.AppendFixed(Body, y, 2);
                Body.Append(" yaw ");
                if (hasYaw) { HudText.AppendFixed(Body, HudText.ToDeg(yaw), 1); Body.Append(" deg"); }
                else Body.Append("n/a");
            }
            EndLine();
            if (!Expanded) return;
            BeginLine(stale, zone);
            Body.Append(" frame ");
            HudText.AppendText(Body, frame);
            EndLine();
            WriteAxisBar("vx", cvx, mvx, hasCmd, hasMeas, _maxLin, stale);
            WriteAxisBar("vy", cvy, mvy, hasCmd, hasMeas, _maxLin, stale);
            WriteAxisBar("wz", cwz, mwz, hasCmd, hasMeas, _maxAng, stale);
        }

        void WriteSent()
        {
            CommandEcho echo = Echo;
            bool stale = echo.HasTwist && echo.TwistAgeSeconds(_now) * 1000.0 >= FieldAges.GreyEchoMs;
            BeginLine(stale, LimitZone.Ok);
            Body.Append("SENT");
            if (!echo.HasTwist && !echo.HasJoints)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            if (echo.HasTwist)
            {
                Body.Append(" vx ");
                HudText.AppendFixed(Body, echo.Vx, 2);
                Body.Append(" vy ");
                HudText.AppendFixed(Body, echo.Vy, 2);
                Body.Append(" wz ");
                HudText.AppendFixed(Body, echo.Wz, 2);
                Body.Append(" hz ");
                HudText.AppendFixed(Body, echo.TwistHz(_now), 1);
            }
            if (echo.HasJoints)
            {
                Body.Append(" joints ");
                HudText.AppendLong(Body, echo.JointCount);
                Body.Append(" jhz ");
                HudText.AppendFixed(Body, echo.JointHz(_now), 1);
            }
            EndLine();
        }

        void WriteMode()
        {
            bool stale = ShowSample && (FieldAges.IsStale(_ages.Mode, FieldAges.GreyModeMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("MODE base ");
            AppendBaseMode(Body);
            Body.Append(" arm ");
            AppendArmMode(Body);
            EndLine();
        }

        void WriteSafety()
        {
            bool have = HasSafety();
            SafetyReading safety = CurrentSafety();
            bool stale = SafetyStale();
            bool trip = have && safety.Measured && (safety.Active || safety.Latched || safety.Source != EstopSource.None
                || (safety.HasMode && (safety.BaseMode == SubsystemMode.Fault || safety.ArmMode == SubsystemMode.Fault)));
            LimitZone zone = trip ? LimitZone.Red : LimitZone.Ok;
            BeginLine(stale, zone);
            if (!have || !safety.Measured)
            {
                Body.Append("ESTOP unmeasured");
                EndLine();
                BeginLine(stale, LimitZone.Ok);
                Body.Append("DEADMAN not-a-permit");
                EndLine();
                BeginLine(stale, LimitZone.Ok);
                Body.Append("SAFETY unmeasured");
                EndLine();
                return;
            }
            if (trip) Body.Append("ESTOP TRIP");
            else Body.Append("ESTOP clear");
            Body.Append(safety.Active ? " active" : " inactive");
            Body.Append(safety.Latched ? " latched" : " unlatched");
            Body.Append(" source ");
            AppendSource(safety.Source);
            EndLine();
            BeginLine(stale, safety.Deadman ? LimitZone.Ok : LimitZone.Red);
            Body.Append(safety.Deadman ? "DEADMAN asserted" : "DEADMAN not-a-permit");
            EndLine();
            BeginLine(stale, zone);
            Body.Append(trip ? "SAFETY TRIP" : "SAFETY no-trip");
            EndLine();
        }

        void WriteImu(bool full)
        {
            if (full && !Expanded) return;
            if (!full && Expanded) return;
            bool has = false;
            double roll = 0, pitch = 0, yaw = 0, ax = 0, ay = 0, az = 0;
            bool stale = false;
            if (ShowSample && _sample != null && _sample.Imu != null)
            {
                has = true;
                roll = _sample.Imu.Roll;
                pitch = _sample.Imu.Pitch;
                yaw = _sample.Imu.Yaw;
                ax = _sample.Imu.Ax;
                ay = _sample.Imu.Ay;
                az = _sample.Imu.Az;
                stale = FieldAges.IsStale(_ages.Imu, FieldAges.GreyImuMs) || (!UsingTelemetry && _hasTelemetry);
            }
            else if (!ShowSample && _hasRawImu)
            {
                has = true;
                roll = _imuRoll;
                pitch = _imuPitch;
                yaw = _imuYaw;
                ax = _imuAx;
                ay = _imuAy;
                az = _imuAz;
                stale = (_now - _rawImuTime) * 1000.0 >= FieldAges.GreyImuMs;
            }
            BeginLine(stale, LimitZone.Ok);
            Body.Append("IMU");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(" roll ");
            HudText.AppendFixed(Body, HudText.ToDeg(roll), 1);
            Body.Append(" deg pitch ");
            HudText.AppendFixed(Body, HudText.ToDeg(pitch), 1);
            Body.Append(" deg");
            if (full)
            {
                Body.Append(" yaw ");
                HudText.AppendFixed(Body, HudText.ToDeg(yaw), 1);
                Body.Append(" deg ax ");
                HudText.AppendFixed(Body, ax, 2);
                Body.Append(" ay ");
                HudText.AppendFixed(Body, ay, 2);
                Body.Append(" az ");
                HudText.AppendFixed(Body, az, 2);
            }
            EndLine();
        }

        void WriteBattery()
        {
            bool stale = ShowSample && (FieldAges.IsStale(_ages.Battery, FieldAges.GreyBatteryMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("BATT");
            if (!(ShowSample && _sample != null && _sample.Battery != null))
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(' ');
            HudText.AppendFixed(Body, _sample.Battery.V, 1);
            Body.Append(" V ");
            HudText.AppendFixed(Body, _sample.Battery.Pct, 0);
            Body.Append(" pct");
            Body.Append(_sample.Battery.Simulated ? " simulated" : " real");
            EndLine();
        }

        void WriteLink()
        {
            bool stale = ShowSample && (FieldAges.IsStale(_ages.Link, FieldAges.GreyLinkMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("LINK rtt ");
            bool haveRtt = false;
            if (ShowSample && _sample != null && _sample.Link != null)
            {
                HudText.AppendFixed(Body, _sample.Link.RttMs, 1);
                Body.Append(" ms hz ");
                HudText.AppendFixed(Body, _sample.Link.Hz, 1);
                haveRtt = true;
            }
            else
            {
                SafetyReading safety = CurrentSafety();
                if (HasSafety() && safety.HasLink)
                {
                    HudText.AppendFixed(Body, safety.RttMs, 1);
                    Body.Append(" ms hz ");
                    HudText.AppendFixed(Body, safety.Hz, 1);
                    haveRtt = true;
                }
            }
            if (!haveRtt) Body.Append("n/a");
            if (_hasEchoRtt)
            {
                Body.Append(" echo-rtt ");
                HudText.AppendFixed(Body, _echoRttMs, 1);
                Body.Append(" ms seq ");
                HudText.AppendLong(Body, _echoSeq);
            }
            EndLine();
        }

        void WriteVideo()
        {
            bool stale = _hasVideo && _videoAgeSec * 1000.0 >= FieldAges.GreyVideoMs;
            BeginLine(stale, LimitZone.Ok);
            Body.Append("VIDEO");
            if (!_hasVideo)
            {
                Body.Append(" no-frame");
                if (!string.IsNullOrEmpty(_videoTopic))
                {
                    Body.Append(' ');
                    Body.Append(_videoTopic);
                }
                EndLine();
                return;
            }
            Body.Append(" age ");
            HudText.AppendFixed(Body, _videoAgeSec * 1000.0, 0);
            Body.Append(" ms hz ");
            HudText.AppendFixed(Body, _videoRate.Hz(_now), 1);
            if (!string.IsNullOrEmpty(_videoTopic))
            {
                Body.Append(' ');
                Body.Append(_videoTopic);
            }
            EndLine();
        }

        void WriteGripper()
        {
            bool has = ShowSample && _sample != null && _sample.Gripper != null;
            bool stale = has && (FieldAges.IsStale(_ages.Gripper, FieldAges.GreyGripperMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            if (Expanded) Body.Append("GRIPPER");
            else Body.Append("grip");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(" pos ");
            HudText.AppendFixed(Body, _sample.Gripper.Pos, 3);
            Body.Append(" m cmd ");
            HudText.AppendFixed(Body, _sample.Gripper.Cmd, 3);
            Body.Append(" m");
            EndLine();
        }

        void WriteJoints()
        {
            Body.Append("JOINTS");
            Body.Append('\n');
            if (ShowSample && _sample != null && _sample.Joints != null && _sample.Joints.Length > 0)
            {
                int n = _sample.Joints.Length;
                if (n > MaxJoints) n = MaxJoints;
                for (int i = 0; i < n; i++)
                    WriteTelemetryJoint(_sample.Joints[i]);
                return;
            }
            if (!ShowSample && _hasRawJoints)
            {
                for (int i = 0; i < _rawJointCount; i++)
                    WriteRawJoint(i);
                return;
            }
            Body.Append("n/a");
            Body.Append('\n');
        }

        void WriteTelemetryJoint(JointSample joint)
        {
            if (joint == null) return;
            double ageMs;
            bool stale = _ages.TryJoint(joint.Name, out ageMs)
                ? ageMs >= FieldAges.GreyJointsMs
                : FieldAges.IsStale(_ages.Joints, FieldAges.GreyJointsMs);
            if (!UsingTelemetry && _hasTelemetry) stale = true;
            LimitZone zone = HudText.Zone(joint.Pos, joint.Min, joint.Max);
            BeginLine(stale, zone);
            HudText.AppendText(Body, joint.Name);
            Body.Append(' ');
            HudText.AppendFixed(Body, HudText.ToDeg(joint.Pos), 1);
            Body.Append(" deg cmd ");
            HudText.AppendFixed(Body, HudText.ToDeg(joint.Cmd), 1);
            Body.Append(' ');
            double span = joint.Max - joint.Min;
            double frac = span > 1e-9 ? (joint.Pos - joint.Min) / span : 0;
            HudText.AppendBar(Body, frac, true, HudText.GhostIndex(joint.Cmd, joint.Min, joint.Max));
            Body.Append(" vel ");
            HudText.AppendFixed(Body, joint.Vel, 2);
            Body.Append(" eff ");
            HudText.AppendFixed(Body, joint.Effort, 2);
            Body.Append(" min ");
            HudText.AppendFixed(Body, HudText.ToDeg(joint.Min), 1);
            Body.Append(" max ");
            HudText.AppendFixed(Body, HudText.ToDeg(joint.Max), 1);
            if (zone == LimitZone.Amber) Body.Append(" AMBER");
            else if (zone == LimitZone.Red) Body.Append(" RED");
            EndLine();
        }

        void WriteRawJoint(int index)
        {
            bool stale = (_now - _rawJointTime) * 1000.0 >= FieldAges.GreyJointsMs;
            BeginLine(stale, LimitZone.Ok);
            if (string.IsNullOrEmpty(_rawNames[index]))
            {
                Body.Append('j');
                HudText.AppendLong(Body, index);
            }
            else Body.Append(_rawNames[index]);
            Body.Append(' ');
            HudText.AppendFixed(Body, HudText.ToDeg(_rawPos[index]), 1);
            Body.Append(" deg");
            CommandEcho echo = Echo;
            if (echo.HasJoints && index < echo.JointCount)
            {
                Body.Append(" cmd ");
                HudText.AppendFixed(Body, HudText.ToDeg(echo.Joint(index)), 1);
            }
            if (_rawHasVel[index])
            {
                Body.Append(" vel ");
                HudText.AppendFixed(Body, _rawVel[index], 2);
            }
            if (_rawHasEff[index])
            {
                Body.Append(" eff ");
                HudText.AppendFixed(Body, _rawEff[index], 2);
            }
            EndLine();
        }

        void WriteNav()
        {
            bool has = ShowSample && _sample != null && _sample.Nav != null;
            bool stale = has && (FieldAges.IsStale(_ages.Nav, FieldAges.GreyNavMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("NAV");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(' ');
            AppendNav(_sample.Nav.State);
            if (_sample.Nav.Goal != null)
            {
                Body.Append(" goal ");
                HudText.AppendFixed(Body, _sample.Nav.Goal.X, 2);
                Body.Append(' ');
                HudText.AppendFixed(Body, _sample.Nav.Goal.Y, 2);
                Body.Append(' ');
                HudText.AppendFixed(Body, HudText.ToDeg(_sample.Nav.Goal.Yaw), 1);
                Body.Append(" deg");
            }
            Body.Append(" eta ");
            HudText.AppendFixed(Body, _sample.Nav.Eta, 1);
            Body.Append(" s");
            EndLine();
        }

        void WritePolicy()
        {
            bool has = ShowSample && _sample != null && _sample.Policy != null;
            bool stale = has && (FieldAges.IsStale(_ages.Policy, FieldAges.GreyPolicyMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("POLICY");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(' ');
            HudText.AppendText(Body, _sample.Policy.Name);
            Body.Append(' ');
            AppendPolicy(_sample.Policy.State);
            Body.Append(" latency ");
            HudText.AppendFixed(Body, _sample.Policy.LatencyMs, 0);
            Body.Append(" ms");
            EndLine();
        }

        void WriteRec()
        {
            bool has = ShowSample && _sample != null && _sample.Rec != null;
            bool stale = has && (FieldAges.IsStale(_ages.Rec, FieldAges.GreyRecMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("REC");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(_sample.Rec.Active ? " active " : " idle ");
            HudText.AppendText(Body, _sample.Rec.Episode);
            Body.Append(" frames ");
            HudText.AppendLong(Body, _sample.Rec.Frames);
            EndLine();
        }

        void WriteCpu()
        {
            bool has = ShowSample && _sample != null && _sample.Sys != null;
            bool stale = has && (FieldAges.IsStale(_ages.Sys, FieldAges.GreySysMs) || (!UsingTelemetry && _hasTelemetry));
            BeginLine(stale, LimitZone.Ok);
            Body.Append("CPU");
            if (!has)
            {
                Body.Append(" n/a");
                EndLine();
                return;
            }
            Body.Append(' ');
            HudText.AppendFixed(Body, _sample.Sys.Cpu, 1);
            Body.Append(" pct temp ");
            HudText.AppendFixed(Body, _sample.Sys.Temp, 1);
            Body.Append(" C");
            EndLine();
        }

        void WriteStreams()
        {
            Body.Append("STREAMS telem ");
            HudText.AppendFixed(Body, _telemetryRate.Hz(_now), 1);
            Body.Append(" odom ");
            HudText.AppendFixed(Body, _odomRate.Hz(_now), 1);
            Body.Append(" joints ");
            HudText.AppendFixed(Body, _jointRate.Hz(_now), 1);
            Body.Append(" imu ");
            HudText.AppendFixed(Body, _imuRate.Hz(_now), 1);
            Body.Append(" safety ");
            HudText.AppendFixed(Body, _safetyRate.Hz(_now), 1);
            Body.Append(" video ");
            HudText.AppendFixed(Body, _videoRate.Hz(_now), 1);
            Body.Append(" echo ");
            HudText.AppendFixed(Body, _echoRate.Hz(_now), 1);
            Body.Append(" sent ");
            HudText.AppendFixed(Body, Echo.TwistHz(_now), 1);
            Body.Append('\n');
        }

        void LoadBase(
            out double cvx, out double cvy, out double cwz,
            out double mvx, out double mvy, out double mwz,
            out double x, out double y, out double yaw,
            out bool hasCmd, out bool hasMeas, out bool hasPose, out bool hasYaw, out string frame)
        {
            cvx = cvy = cwz = mvx = mvy = mwz = x = y = yaw = 0;
            hasCmd = hasMeas = hasPose = hasYaw = false;
            frame = null;
            if (ShowSample && _sample != null && _sample.Base != null)
            {
                if (_sample.Base.Cmd != null)
                {
                    cvx = _sample.Base.Cmd.Vx;
                    cvy = _sample.Base.Cmd.Vy;
                    cwz = _sample.Base.Cmd.Wz;
                    hasCmd = true;
                }
                if (_sample.Base.Meas != null)
                {
                    mvx = _sample.Base.Meas.Vx;
                    mvy = _sample.Base.Meas.Vy;
                    mwz = _sample.Base.Meas.Wz;
                    hasMeas = true;
                }
                if (_sample.Base.Pose != null)
                {
                    x = _sample.Base.Pose.X;
                    y = _sample.Base.Pose.Y;
                    yaw = _sample.Base.Pose.Yaw;
                    hasPose = true;
                    hasYaw = true;
                }
                frame = _sample.Base.Frame;
                return;
            }
            if (_hasRawBase)
            {
                x = _rawX;
                y = _rawY;
                yaw = _rawYaw;
                hasPose = _rawHasPose;
                hasYaw = _rawHasYaw;
                mvx = _rawVx;
                mvy = _rawVy;
                mwz = _rawWz;
                hasMeas = _rawHasTwist;
            }
            CommandEcho echo = Echo;
            if (echo.HasTwist)
            {
                cvx = echo.Vx;
                cvy = echo.Vy;
                cwz = echo.Wz;
                hasCmd = true;
            }
        }

        bool BaseIsStale()
        {
            if (ShowSample)
            {
                if (!UsingTelemetry && _hasTelemetry) return true;
                return FieldAges.IsStale(_ages.Base, FieldAges.GreyBaseMs)
                    || FieldAges.IsStale(_ages.BaseCmd, FieldAges.GreyBaseMs)
                    || FieldAges.IsStale(_ages.BaseMeas, FieldAges.GreyBaseMs)
                    || FieldAges.IsStale(_ages.BasePose, FieldAges.GreyBaseMs);
            }
            return _hasRawBase && (_now - _rawBaseTime) * 1000.0 >= FieldAges.GreyBaseMs;
        }

        bool OverCap(double cvx, double cvy, double cwz, double mvx, double mvy, double mwz, bool hasCmd, bool hasMeas)
        {
            if (hasMeas && (HudText.PlanarSpeed(mvx, mvy) > _maxLin || Math.Abs(mwz) > _maxAng)) return true;
            if (hasCmd && (HudText.PlanarSpeed(cvx, cvy) > _maxLin || Math.Abs(cwz) > _maxAng)) return true;
            return false;
        }

        bool HasSafety()
        {
            bool directWins = _hasDirectSafety && (!UsingTelemetry || _directSafetyTime >= _telemetryTime);
            return directWins || _hasTelemetry;
        }

        SafetyReading CurrentSafety()
        {
            bool directWins = _hasDirectSafety && (!UsingTelemetry || _directSafetyTime >= _telemetryTime);
            if (directWins) return _directSafety;
            return _telemetrySafety;
        }

        bool SafetyStale()
        {
            bool direct = _hasDirectSafety && (!UsingTelemetry || _directSafetyTime >= _telemetryTime);
            if (direct) return false;
            if (!UsingTelemetry && _hasTelemetry) return true;
            return FieldAges.IsStale(_ages.Estop, FieldAges.GreyEstopMs)
                || FieldAges.IsStale(_ages.Deadman, FieldAges.GreyDeadmanMs);
        }

        void AppendAxisNumbers(string label, double cmd, double meas, bool hasCmd, bool hasMeas)
        {
            Body.Append(label);
            Body.Append(" cmd ");
            if (hasCmd) HudText.AppendFixed(Body, cmd, 2); else Body.Append("n/a");
            Body.Append(" meas ");
            if (hasMeas) HudText.AppendFixed(Body, meas, 2); else Body.Append("n/a");
        }

        void WriteAxisBar(string name, double cmd, double meas, bool hasCmd, bool hasMeas, double cap, bool stale)
        {
            bool over = false;
            if (hasCmd && cap > 0 && Math.Abs(cmd) > cap) over = true;
            if (hasMeas && cap > 0 && Math.Abs(meas) > cap) over = true;
            BeginLine(stale, over ? LimitZone.Red : LimitZone.Ok);
            Body.Append(name);
            Body.Append(" cmd ");
            if (hasCmd && cap > 0) HudText.AppendBar(Body, Math.Abs(cmd) / cap, false, 0);
            else Body.Append("n/a");
            Body.Append(" meas ");
            if (hasMeas && cap > 0) HudText.AppendBar(Body, Math.Abs(meas) / cap, false, 0);
            else Body.Append("n/a");
            EndLine();
        }

        void AppendBaseMode(StringBuilder sb)
        {
            if (ShowSample && _sample != null && _sample.Mode != null) AppendSubsystem(sb, _sample.Mode.Base);
            else if (_hasRawBaseMode) HudText.AppendText(sb, _baseModeText);
            else if (HasSafety() && CurrentSafety().HasMode) AppendSubsystem(sb, CurrentSafety().BaseMode);
            else sb.Append("n/a");
        }

        void AppendArmMode(StringBuilder sb)
        {
            if (ShowSample && _sample != null && _sample.Mode != null) AppendSubsystem(sb, _sample.Mode.Arm);
            else if (_hasRawBaseMode || _hasRawArmMode) HudText.AppendText(sb, _armModeText);
            else if (HasSafety() && CurrentSafety().HasMode) AppendSubsystem(sb, CurrentSafety().ArmMode);
            else sb.Append("n/a");
        }

        static void AppendSubsystem(StringBuilder sb, SubsystemMode mode)
        {
            switch (mode)
            {
                case SubsystemMode.Idle: sb.Append("idle"); break;
                case SubsystemMode.Teleop: sb.Append("teleop"); break;
                case SubsystemMode.Auto: sb.Append("auto"); break;
                case SubsystemMode.Hold: sb.Append("hold"); break;
                case SubsystemMode.Fault: sb.Append("fault"); break;
                case SubsystemMode.NotPresent: sb.Append("not_present"); break;
                default: sb.Append("n/a"); break;
            }
        }

        void AppendKind(RobotKind kind)
        {
            if (kind == RobotKind.Real) Body.Append("real");
            else Body.Append("sim");
        }

        void AppendSource(EstopSource source)
        {
            switch (source)
            {
                case EstopSource.None: Body.Append("none"); break;
                case EstopSource.Hardware: Body.Append("hardware"); break;
                case EstopSource.Software: Body.Append("software"); break;
                case EstopSource.Remote: Body.Append("remote"); break;
                case EstopSource.Watchdog: Body.Append("watchdog"); break;
                default: Body.Append("n/a"); break;
            }
        }

        void AppendNav(NavPhase phase)
        {
            switch (phase)
            {
                case NavPhase.Idle: Body.Append("idle"); break;
                case NavPhase.Navigating: Body.Append("navigating"); break;
                case NavPhase.Succeeded: Body.Append("succeeded"); break;
                case NavPhase.Aborted: Body.Append("aborted"); break;
                case NavPhase.Failed: Body.Append("failed"); break;
                default: Body.Append("n/a"); break;
            }
        }

        void AppendPolicy(PolicyPhase phase)
        {
            switch (phase)
            {
                case PolicyPhase.Idle: Body.Append("idle"); break;
                case PolicyPhase.Running: Body.Append("running"); break;
                case PolicyPhase.Paused: Body.Append("paused"); break;
                case PolicyPhase.Fault: Body.Append("fault"); break;
                default: Body.Append("n/a"); break;
            }
        }

        void BeginLine(bool stale, LimitZone zone)
        {
            _lineColored = HudText.ColorOpened(stale, zone);
            HudText.OpenColor(Body, stale, zone);
        }

        void EndLine()
        {
            HudText.CloseColor(Body, _lineColored);
            _lineColored = false;
            Body.Append('\n');
        }
    }
}
