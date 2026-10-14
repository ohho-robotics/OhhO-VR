using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Control.Manip;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control.Safety
{
    /// <summary>
    /// Headset session for <c>teleop_safety</c>: 10 Hz <c>/ohho/heartbeat</c>
    /// while the operator is present and the link is up, safety-state latch,
    /// confirmed <c>/ohho/estop_reset</c>, and a deadman re-press after the link
    /// or the operator drops out. Motion does not resume on its own.
    /// </summary>
    public sealed class TeleopSafetySession
    {
        private readonly Func<double> _clock;

        private bool _active;
        private bool _present = true;
        private bool _connected;
        private bool _seenLoss;
        private bool _linkRestored;
        private bool _advertised;
        private bool _subscribed;
        private bool _announcedHeartbeat;

        private uint _seq;
        private double _hbElapsed;
        private double _lastTClient;
        private double _prevTClient;
        private double _maxGap;

        private bool _stateKnown;
        private bool _latched;
        private bool _armed;
        private string _reason = "";
        private bool _resetConfirmPending;

        private bool _awaitingDeadman;
        private bool _needFreshPress;
        private bool _deadmanHeld;
        private bool _manipHeld;

        public TeleopSafetySession(Func<double> clock = null)
        {
            _clock = clock ?? DefaultClock;
        }

        public bool SessionActive => _active;
        public bool OperatorPresent => _present;
        public bool LinkConnected => _connected;
        public bool HeartbeatRunning => _active && _present && _connected;
        public uint HeartbeatSeq => _seq;
        public int HeartbeatCount => (int)_seq;
        public double LastTClient => _lastTClient;
        public double MaxInterHeartbeatGap => _maxGap;
        public bool StateKnown => _stateKnown;
        public bool Latched => _latched;
        public bool Armed => _armed;
        public string TripReason => _reason ?? "";
        public bool AwaitingDeadman => _awaitingDeadman;
        public bool ResetConfirmPending => _resetConfirmPending;
        public string LastLog { get; private set; } = "";

        /// <summary>
        /// Armed / latched / trip reason for the HUD. Empty until the first
        /// <c>/ohho/safety/state</c> message so a missing publisher is not
        /// shown as a false trip.
        /// </summary>
        public string StatusLine
        {
            get
            {
                if (!_active) return "SAFETY — no session";
                if (!_stateKnown) return "SAFETY — waiting for /ohho/safety/state";
                if (_latched)
                    return string.IsNullOrEmpty(_reason) ? "LATCHED" : "LATCHED — " + _reason;
                if (_armed)
                    return string.IsNullOrEmpty(_reason) ? "ARMED" : "ARMED — " + _reason;
                return string.IsNullOrEmpty(_reason) ? "DISARMED" : "DISARMED — " + _reason;
            }
        }

        /// <summary>
        /// True when the robot watchdog would already have zeroed the base.
        /// <paramref name="lastHeartbeatClientTime"/> is <see cref="LastTClient"/>
        /// from the last pulse; a non-positive stamp means no pulse yet.
        /// </summary>
        public static bool WatchdogWouldZero(double lastHeartbeatClientTime, double now)
        {
            if (lastHeartbeatClientTime <= 0) return true;
            return (now - lastHeartbeatClientTime) >= OhhoSafetyContract.WatchdogSec;
        }

        public static bool IsDeadmanHeld(in TeleopInput input)
        {
            return (input.LeftGrip || input.RightGrip) && !input.BothGrips;
        }

        /// <summary>
        /// Update operator presence. Returns true when an active session just
        /// lost the operator (caller should zero the base immediately).
        /// Headset-off, app pause, and focus loss all come through here.
        /// </summary>
        public bool SetOperatorPresent(bool present, string reason, IRobotLink link)
        {
            if (present == _present) return false;
            _present = present;
            if (!_active) return false;

            if (!present)
            {
                HoldForDeadman();
                Log(StopLog(string.IsNullOrEmpty(reason) ? "presence lost" : reason));
                return true;
            }

            Log("[TeleopSafety] operator present; heartbeat resumes; motion held until deadman re-press");
            return false;
        }

        public void Start(IRobotLink link)
        {
            if (_active) Stop(link);

            _active = true;
            _seq = 0;
            _hbElapsed = 0;
            _lastTClient = 0;
            _prevTClient = 0;
            _maxGap = 0;
            _stateKnown = false;
            _latched = false;
            _armed = false;
            _reason = "";
            _resetConfirmPending = false;
            _deadmanHeld = false;
            _manipHeld = false;
            _seenLoss = false;
            _linkRestored = false;
            _advertised = false;
            _announcedHeartbeat = false;
            _connected = link != null && link.IsConnected;
            _awaitingDeadman = !_present;
            _needFreshPress = _awaitingDeadman;

            EnsureSubscribed(link);
            TickHeartbeat(0, link);
        }

        public void Stop(IRobotLink link)
        {
            _active = false;
            _awaitingDeadman = false;
            _needFreshPress = false;
            _advertised = false;
            _manipHeld = false;
            if (_subscribed && link != null)
            {
                link.Unsubscribe(OhhoSafetyContract.SafetyStateTopic);
                _subscribed = false;
            }
        }

        /// <summary>True once after the link returns from a drop this session.</summary>
        public bool ConsumeLinkRestored()
        {
            bool restored = _linkRestored;
            _linkRestored = false;
            return restored;
        }

        public void RequestEstopReset()
        {
            if (!_latched) return;
            _resetConfirmPending = true;
            Log("[TeleopSafety] e-stop reset armed — confirm to call /ohho/estop_reset");
        }

        public void CancelEstopReset()
        {
            _resetConfirmPending = false;
        }

        /// <summary>
        /// Second step of the HUD confirm. Does not clear <see cref="Latched"/>;
        /// the next <c>/ohho/safety/state</c> does, after the robot accepts it.
        /// </summary>
        public bool ConfirmEstopReset(IRobotLink link)
        {
            if (!_resetConfirmPending || !_latched) return false;
            if (link == null || !link.IsConnected)
            {
                Log("[TeleopSafety] e-stop reset held — link is down");
                return false;
            }

            _resetConfirmPending = false;
            Log("[TeleopSafety] e-stop reset sent /ohho/estop_reset");
            link.CallService(OhhoSafetyContract.EstopResetService, OhhoSafetyContract.EstopResetType,
                (ok, values) => Log("[TeleopSafety] estop_reset response ok=" + ok + " " + values));
            return true;
        }

        public void ApplySafetyState(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonException)
            {
                Debug.LogWarning("[TeleopSafety] Ignored malformed /ohho/safety/state payload.");
                return;
            }

            if (token == null || token.Type != JTokenType.Object) return;
            if (token["msg"] is JObject msg)
                token = msg;
            if (token["armed"] == null && token["latched"] == null && token["data"] != null)
            {
                var data = token["data"];
                if (data.Type == JTokenType.Object)
                    token = data;
                else if (data.Type == JTokenType.String)
                {
                    string text = data.Value<string>();
                    if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith("{")) return;
                    try { token = JToken.Parse(text); }
                    catch (JsonException)
                    {
                        Debug.LogWarning("[TeleopSafety] Ignored malformed /ohho/safety/state data.");
                        return;
                    }
                }
            }

            if (token == null || token.Type != JTokenType.Object) return;
            if (token["latched"] == null && token["armed"] == null) return;

            if (token["latched"] != null && token["latched"].Type == JTokenType.Boolean)
            {
                _latched = token.Value<bool>("latched");
                if (!_latched) _resetConfirmPending = false;
            }
            if (token["armed"] != null && token["armed"].Type == JTokenType.Boolean)
                _armed = token.Value<bool>("armed");
            if (token["reason"] != null && token["reason"].Type != JTokenType.Null)
                _reason = token.Value<string>("reason") ?? "";
            _stateKnown = true;
        }

        /// <summary>
        /// One control frame. Heartbeat is paced here. Drive is suppressed while
        /// latched, while the operator or the link is gone, and until a deadman
        /// re-press after either returns. E-stop edges inside the drive scheme
        /// still run.
        /// </summary>
        public void Tick(IRobotLink link, ref TeleopInput input, IDriveScheme drive, IManipulationScheme manip, Action tickManip)
        {
            if (link != null)
                NoteConnected(link.IsConnected);

            TickHeartbeat(input.DeltaTime, link);

            bool deadman = IsDeadmanHeld(input);
            bool allow = AllowsDrive(deadman);

            if (drive is DriveSchemeBase gated)
                gated.SuppressMotion = !allow;

            if (link != null && link.IsConnected && drive != null)
                drive.Tick(ref input, link);

            if (allow)
            {
                _manipHeld = false;
                tickManip?.Invoke();
            }
            else if (!_manipHeld)
            {
                manip?.Stop(link);
                _manipHeld = true;
            }
        }

        public bool AllowsDrive(bool deadmanHeld)
        {
            if (!_active) return false;

            if (_awaitingDeadman && _needFreshPress && !deadmanHeld)
                _needFreshPress = false;

            if (_awaitingDeadman && !_needFreshPress && !_latched && _present && _connected
                && deadmanHeld && !_deadmanHeld)
            {
                _awaitingDeadman = false;
                Log("[TeleopSafety] deadman re-pressed; motion allowed");
            }

            _deadmanHeld = deadmanHeld;
            return _present && _connected && !_latched && !_awaitingDeadman;
        }

        private void NoteConnected(bool connected)
        {
            if (connected == _connected) return;

            if (!connected)
            {
                _seenLoss = true;
                _connected = false;
                _advertised = false;
                if (_active)
                {
                    HoldForDeadman();
                    Log(StopLog("link lost"));
                }
                return;
            }

            _connected = true;
            if (_active && _seenLoss)
            {
                _linkRestored = true;
                _advertised = false;
                Log("[TeleopSafety] link restored; motion held until deadman re-press");
            }
        }

        private void HoldForDeadman()
        {
            _awaitingDeadman = true;
            _needFreshPress = true;
        }

        private void TickHeartbeat(double dt, IRobotLink link)
        {
            if (!CanHeartbeat(link))
            {
                _hbElapsed = OhhoSafetyContract.HeartbeatPeriodSec;
                return;
            }

            _hbElapsed += dt;
            if (_seq != 0 && _hbElapsed < OhhoSafetyContract.HeartbeatPeriodSec - 1e-9)
                return;

            _hbElapsed = 0;
            PublishHeartbeat(link);
        }

        private bool CanHeartbeat(IRobotLink link)
        {
            return _active && _present && link != null && link.IsConnected;
        }

        private void PublishHeartbeat(IRobotLink link)
        {
            EnsureAdvertised(link);
            double stamp = _clock();
            if (_seq > 0)
            {
                double gap = stamp - _prevTClient;
                if (gap > _maxGap) _maxGap = gap;
            }

            _seq++;
            _prevTClient = stamp;
            _lastTClient = stamp;
            var msg = new HeartbeatMsg { seq = _seq, t_client = stamp };
            link.Publish(OhhoSafetyContract.HeartbeatTopic, msg);

            if (!_announcedHeartbeat)
            {
                _announcedHeartbeat = true;
                Log("[TeleopSafety] heartbeat started " + OhhoSafetyContract.HeartbeatTopic
                    + " " + OhhoSafetyContract.HeartbeatType + " "
                    + OhhoSafetyContract.HeartbeatHz.ToString("0") + " Hz seq=" + msg.seq
                    + " t_client=" + msg.t_client.ToString("F3"));
            }
        }

        private void EnsureSubscribed(IRobotLink link)
        {
            if (link == null || _subscribed) return;
            link.Subscribe(OhhoSafetyContract.SafetyStateTopic, OhhoSafetyContract.SafetyStateType, ApplySafetyState);
            _subscribed = true;
        }

        private void EnsureAdvertised(IRobotLink link)
        {
            if (link == null || _advertised) return;
            link.Advertise(OhhoSafetyContract.HeartbeatTopic, OhhoSafetyContract.HeartbeatType);
            _advertised = true;
        }

        private string StopLog(string reason)
        {
            int budgetMs = (int)System.Math.Round(OhhoSafetyContract.StopBudgetSec * 1000.0);
            return "[TeleopSafety] heartbeat stopped (" + reason + ") seq=" + _seq
                + " t_client=" + _lastTClient.ToString("F3")
                + "; robot watchdog zeros teleop within " + budgetMs + " ms";
        }

        private void Log(string message)
        {
            LastLog = message;
            Debug.Log(message);
        }

        private static double DefaultClock()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }
    }
}
