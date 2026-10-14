using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Control.Safety;
using OmniBot.VR.Core;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// OHH-202: 10 Hz heartbeat, link-loss hold, latched e-stop reset, against a
    /// fake <see cref="IRobotLink"/>. The 350 ms figure is teleop_safety's
    /// watchdog (300 ms) plus one gate tick — these tests do not touch a robot.
    /// </summary>
    public class TeleopSafetyTests
    {
        private const string CmdVel = "/cmd_vel/teleop";
        private const string Estop = "/emergency_stop";

        private double _clock;
        private FakeRobotLink _link;
        private TeleopSafetySession _session;
        private MecanumDriveScheme _drive;

        [SetUp]
        public void SetUp()
        {
            _clock = 5000;
            _link = new FakeRobotLink();
            _session = new TeleopSafetySession(() => _clock);
            _drive = new MecanumDriveScheme();
            _drive.Configure(MakeProfile());
        }

        [Test]
        public void Heartbeat_IsEmptyOnTheWire_AndCarriesSeqAndClientTime()
        {
            var input = Forward(0f);
            _session.Start(_link);

            Assert.AreEqual(1, _session.HeartbeatCount);
            Assert.AreEqual(OhhoSafetyContract.HeartbeatTopic, _link.Advertised[0].Topic);
            Assert.AreEqual(OhhoSafetyContract.HeartbeatType, _link.Advertised[0].Type);

            var pulse = (HeartbeatMsg)_link.Published[0].Msg;
            Assert.AreEqual(1u, pulse.seq);
            Assert.AreEqual(5000d, pulse.t_client, 1e-9);
            Assert.AreEqual("{}", JsonConvert.SerializeObject(pulse));
            _session.Tick(_link, ref input, null, null, null);
        }

        [Test]
        public void Heartbeat_PublishesAt10Hz_InsideTheWatchdog()
        {
            var input = Forward(0f);
            _session.Start(_link);

            for (int i = 0; i < 20; i++)
            {
                _clock += 0.05;
                input.DeltaTime = 0.05f;
                _session.Tick(_link, ref input, null, null, null);
            }

            // Start publishes immediately, then one pulse each 0.1 s for 1.0 s.
            Assert.AreEqual(11, _session.HeartbeatCount);
            Assert.AreEqual(11u, _session.HeartbeatSeq);
            Assert.AreEqual(0.1, _session.MaxInterHeartbeatGap, 0.02);
            Assert.Less(_session.MaxInterHeartbeatGap, OhhoSafetyContract.WatchdogSec);
            Assert.AreEqual(_clock, _session.LastTClient, 0.02);

            var last = LastOf<HeartbeatMsg>(_link, OhhoSafetyContract.HeartbeatTopic);
            Assert.AreEqual(11u, last.seq);
            Assert.AreEqual("{}", JsonConvert.SerializeObject(last));
        }

        [Test]
        public void HmdUnmounted_StopsHeartbeat_AndWatchdogZerosWithin350ms()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.Greater(LastLinearX(_link), 0.05f);

            int heartbeats = _session.HeartbeatCount;
            double lastPulse = _session.LastTClient;
            _session.SetOperatorPresent(false, "hmd unmounted", _link);

            Assert.IsTrue(_session.AwaitingDeadman);
            Assert.That(_session.LastLog, Does.Contain("hmd unmounted"));
            Assert.That(_session.LastLog, Does.Contain("350"));

            int mark = _link.Published.Count;
            input.DeltaTime = 0f;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.AreEqual(heartbeats, _session.HeartbeatCount);
            AssertNoForwardAfter(_link, mark);
            Assert.AreEqual(0f, LastLinearX(_link), 0.001f);

            _clock += OhhoSafetyContract.StopBudgetSec;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.AreEqual(heartbeats, _session.HeartbeatCount);
            Assert.IsTrue(TeleopSafetySession.WatchdogWouldZero(lastPulse, _clock));
            Assert.LessOrEqual(_clock - lastPulse, OhhoSafetyContract.StopBudgetSec + 1e-9);
        }

        [TestCase("app pause")]
        [TestCase("focus lost")]
        public void PresenceLoss_StopsHeartbeat(string reason)
        {
            var input = Forward(0f);
            _session.Start(_link);
            int heartbeats = _session.HeartbeatCount;

            _session.SetOperatorPresent(false, reason, _link);
            _session.Tick(_link, ref input, null, null, null);

            Assert.AreEqual(heartbeats, _session.HeartbeatCount);
            Assert.IsFalse(_session.HeartbeatRunning);
            Assert.That(_session.LastLog, Does.Contain(reason));
            Assert.That(_session.LastLog, Does.Contain("350"));
            Assert.IsTrue(_session.AwaitingDeadman);
        }

        [Test]
        public void WifiLoss_StopsHeartbeat_AndWatchdogZerosWithin350ms()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);
            int heartbeats = _session.HeartbeatCount;
            double lastPulse = _session.LastTClient;
            int mark = _link.Published.Count;

            _link.IsConnected = false;
            _session.Tick(_link, ref input, _drive, null, null);

            Assert.AreEqual(mark, _link.Published.Count, "A dead link must not publish another command or heartbeat.");
            Assert.AreEqual(heartbeats, _session.HeartbeatCount);
            Assert.That(_session.LastLog, Does.Contain("link lost"));
            Assert.That(_session.LastLog, Does.Contain("350"));
            Assert.IsTrue(_session.AwaitingDeadman);

            _clock += OhhoSafetyContract.StopBudgetSec;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.AreEqual(heartbeats, _session.HeartbeatCount);
            Assert.IsTrue(TeleopSafetySession.WatchdogWouldZero(lastPulse, _clock));
        }

        [Test]
        public void Reconnect_StaysStillUntilDeadmanRepress()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.Greater(LastLinearX(_link), 0.05f);
            Assert.IsFalse(_session.AwaitingDeadman);

            _link.IsConnected = false;
            _session.Tick(_link, ref input, _drive, null, null);

            _link.IsConnected = true;
            int mark = _link.Published.Count;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);

            Assert.IsTrue(_session.AwaitingDeadman);
            Assert.That(_session.LastLog, Does.Contain("motion held until deadman re-press"));
            AssertNoForwardAfter(_link, mark);

            input.LeftGrip = true;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);

            Assert.IsFalse(_session.AwaitingDeadman);
            Assert.That(_session.LastLog, Does.Contain("deadman re-pressed"));
            Assert.Greater(LastLinearX(_link), 0.05f);
        }

        [Test]
        public void HeldGripAcrossReconnect_DoesNotCountUntilReleaseAndPress()
        {
            var input = Forward(0.1f);
            input.RightGrip = true;
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);

            _link.IsConnected = false;
            _session.Tick(_link, ref input, _drive, null, null);
            _link.IsConnected = true;

            int mark = _link.Published.Count;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.IsTrue(_session.AwaitingDeadman);
            AssertNoForwardAfter(_link, mark);

            input.RightGrip = false;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.IsTrue(_session.AwaitingDeadman);
            AssertNoForwardAfter(_link, mark);

            input.RightGrip = true;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.IsFalse(_session.AwaitingDeadman);
            Assert.Greater(LastLinearX(_link), 0.05f);
        }

        [Test]
        public void LatchedSafetyState_BlocksDrive_AndShowsTripReason()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.Greater(LastLinearX(_link), 0.05f);

            _link.Push(OhhoSafetyContract.SafetyStateTopic, RobotState(false, true, "estop"));
            Assert.IsTrue(_session.Latched);
            Assert.IsFalse(_session.Armed);
            Assert.AreEqual("estop", _session.TripReason);
            Assert.AreEqual("LATCHED — estop", _session.StatusLine);

            int mark = _link.Published.Count;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.IsFalse(_session.AllowsDrive(true));
            AssertNoForwardAfter(_link, mark);
        }

        [Test]
        public void DisarmedWithoutLatch_DoesNotBlockDrive()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _link.Push(OhhoSafetyContract.SafetyStateTopic,
                "{\"armed\":false,\"latched\":false,\"reason\":\"heartbeat_watchdog\"}");

            Assert.IsFalse(_session.Latched);
            Assert.AreEqual("DISARMED — heartbeat_watchdog", _session.StatusLine);
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.Greater(LastLinearX(_link), 0.05f);
        }

        [Test]
        public void EstopReset_RequiresConfirm_ThenCallsService_LatchClearsOnState()
        {
            _session.Start(_link);
            _link.Push(OhhoSafetyContract.SafetyStateTopic, RobotState(false, true, "estop"));

            _session.ConfirmEstopReset(_link);
            Assert.AreEqual(0, _link.ServiceCalls.Count, "Confirm without the HUD arming step must not call the service.");

            _session.RequestEstopReset();
            Assert.IsTrue(_session.ResetConfirmPending);
            Assert.AreEqual(0, _link.ServiceCalls.Count);

            Assert.IsTrue(_session.ConfirmEstopReset(_link));
            Assert.AreEqual(1, _link.ServiceCalls.Count);
            Assert.AreEqual(OhhoSafetyContract.EstopResetService, _link.ServiceCalls[0].Service);
            Assert.AreEqual(OhhoSafetyContract.EstopResetType, _link.ServiceCalls[0].Type);
            Assert.IsFalse(_session.ResetConfirmPending);
            Assert.IsTrue(_session.Latched, "The latch clears from /ohho/safety/state, not from the call itself.");

            _link.IsConnected = false;
            _session.RequestEstopReset();
            Assert.IsFalse(_session.ConfirmEstopReset(_link));
            Assert.AreEqual(1, _link.ServiceCalls.Count);
            Assert.IsTrue(_session.ResetConfirmPending);

            _link.IsConnected = true;
            _link.Push(OhhoSafetyContract.SafetyStateTopic, RobotState(true, false, ""));
            Assert.IsFalse(_session.Latched);
            Assert.IsTrue(_session.Armed);
            Assert.AreEqual("ARMED", _session.StatusLine);
            Assert.IsFalse(_session.ResetConfirmPending);
        }

        [Test]
        public void BothGripsWhileHeld_PublishesEstop_AndDoesNotRearm()
        {
            var input = Forward(0.1f);
            _session.Start(_link);
            _session.Tick(_link, ref input, _drive, null, null);

            _link.IsConnected = false;
            _session.Tick(_link, ref input, _drive, null, null);
            _link.IsConnected = true;
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.IsTrue(_session.AwaitingDeadman);

            int mark = _link.Published.Count;
            input.LeftGrip = true;
            input.RightGrip = true;
            input.BothGrips = true;
            input.BothGripsPressed = true;
            input.DeltaTime = 0.1f;
            _session.Tick(_link, ref input, _drive, null, null);

            Assert.IsTrue(_session.AwaitingDeadman);
            Assert.IsTrue(SawEstopTrue(_link));
            AssertNoForwardAfter(_link, mark);
        }

        [Test]
        public void MalformedSafetyState_DoesNotClearALatch()
        {
            _session.Start(_link);
            _link.Push(OhhoSafetyContract.SafetyStateTopic, RobotState(false, true, "estop"));

            _session.ApplySafetyState("{");
            _session.ApplySafetyState("{\"data\":\"not-json\"}");
            _session.ApplySafetyState("");

            Assert.IsTrue(_session.Latched);
            Assert.AreEqual("estop", _session.TripReason);
        }

        [Test]
        public void HeadsetOffAtSessionStart_DoesNotHeartbeatUntilPresent()
        {
            _session.SetOperatorPresent(false, "hmd unmounted", _link);
            _session.Start(_link);
            Assert.AreEqual(0, _session.HeartbeatCount);
            Assert.IsTrue(_session.AwaitingDeadman);

            _session.SetOperatorPresent(true, "hmd mounted", _link);
            var input = Forward(0.1f);
            _session.Tick(_link, ref input, _drive, null, null);
            Assert.Greater(_session.HeartbeatCount, 0);
            Assert.IsTrue(_session.AwaitingDeadman);
            AssertNoForwardAfter(_link, 0);
        }

        [Test]
        public void ReconnectBackoff_IsTwoThenFourThenEight_AndStops()
        {
            var backoff = new ReconnectBackoff();
            Assert.IsTrue(backoff.TrySchedule(out float first));
            Assert.AreEqual(2f, first);
            Assert.IsTrue(backoff.TrySchedule(out float second));
            Assert.AreEqual(4f, second);
            Assert.IsTrue(backoff.TrySchedule(out float third));
            Assert.AreEqual(8f, third);
            Assert.IsTrue(backoff.Exhausted);
            Assert.IsFalse(backoff.TrySchedule(out float none));
            Assert.AreEqual(0f, none);

            backoff.Reset();
            Assert.IsFalse(backoff.Exhausted);
            Assert.IsTrue(backoff.TrySchedule(out float again));
            Assert.AreEqual(2f, again);
        }

        [Test]
        public void EstopReset_RosbridgePayload_MatchesTrigger()
        {
            string json = RosBridgeServiceCall.FormatRequest(
                "ohho_svc_1",
                OhhoSafetyContract.EstopResetService,
                OhhoSafetyContract.EstopResetType);

            Assert.That(json, Does.Contain("\"op\":\"call_service\""));
            Assert.That(json, Does.Contain("\"service\":\"/ohho/estop_reset\""));
            Assert.That(json, Does.Contain("\"type\":\"std_srvs/Trigger\""));
            Assert.That(json, Does.Contain("\"args\":{}"));

            const string response =
                "{\"op\":\"service_response\",\"id\":\"ohho_svc_1\",\"service\":\"/ohho/estop_reset\",\"result\":true,\"values\":{\"success\":false,\"message\":\"stop still asserted\"}}";
            Assert.IsTrue(RosBridgeServiceCall.TryParseResponse(response, out string id, out bool ok, out string values));
            Assert.AreEqual("ohho_svc_1", id);
            Assert.IsFalse(ok);
            Assert.That(values, Does.Contain("stop still asserted"));
        }

        private static RobotProfile MakeProfile()
        {
            return new RobotProfile
            {
                RobotId = "omnibot",
                Name = "OmniBot",
                Drive = DriveKind.Mecanum,
                MaxLinVel = 0.2f,
                MaxAngVel = 1f,
                Topics = new RobotTopics
                {
                    CmdVel = CmdVel,
                    ControlMode = "/control_mode",
                    EmergencyStop = Estop,
                },
            };
        }

        private static TeleopInput Forward(float dt)
        {
            return new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                DeltaTime = dt,
            };
        }

        /// <summary>std_msgs/String envelope, the shape rosbridge delivers for /ohho/safety/state.</summary>
        private static string RobotState(bool armed, bool latched, string reason)
        {
            var inner = new JObject
            {
                ["armed"] = armed,
                ["latched"] = latched,
                ["reason"] = reason ?? "",
            };
            return new JObject { ["data"] = inner.ToString(Formatting.None) }.ToString(Formatting.None);
        }

        private static float LastLinearX(FakeRobotLink link)
        {
            float x = 0f;
            for (int i = 0; i < link.Published.Count; i++)
            {
                if (link.Published[i].Msg is TwistMsg twist && twist.linear != null)
                    x = twist.linear.x;
            }
            return x;
        }

        private static void AssertNoForwardAfter(FakeRobotLink link, int start)
        {
            for (int i = start; i < link.Published.Count; i++)
            {
                if (link.Published[i].Msg is TwistMsg twist && twist.linear != null)
                    Assert.LessOrEqual(Mathf.Abs(twist.linear.x), 0.001f, "cmd_vel index " + i);
            }
        }

        private static bool SawEstopTrue(FakeRobotLink link)
        {
            for (int i = 0; i < link.Published.Count; i++)
            {
                if (link.Published[i].Topic == Estop && link.Published[i].Msg is BoolMsg msg && msg.data)
                    return true;
            }
            return false;
        }

        private static T LastOf<T>(FakeRobotLink link, string topic) where T : class
        {
            T found = null;
            for (int i = 0; i < link.Published.Count; i++)
            {
                if (link.Published[i].Topic == topic && link.Published[i].Msg is T typed)
                    found = typed;
            }
            Assert.IsNotNull(found);
            return found;
        }
    }
}
