using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Ohho.Schemas;
using OmniBot.VR.Telemetry;

namespace OmniBot.VR.Tests
{
    public class TelemetryHudTests
    {
        TelemetryHudComposer _hud;

        [SetUp]
        public void SetUp()
        {
            _hud = new TelemetryHudComposer();
            _hud.Echo = new CommandEcho();
        }

        [Test]
        public void FullSample_ParsesWithAgeSidecar_AndShowsEveryBlock()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 1000));
            _hud.Expanded = true;
            _hud.Write(1000);
            string text = _hud.Text;

            Assert.That(text, Does.Contain("src telemetry"));
            Assert.That(text, Does.Contain("omnibot-sim"));
            Assert.That(text, Does.Contain("sim"));
            Assert.That(text, Does.Contain("1.0.0"));
            Assert.That(text, Does.Contain("BASE"));
            Assert.That(text, Does.Contain("vx cmd 0.10 meas 0.08"));
            Assert.That(text, Does.Contain("vy cmd 0.00 meas -0.01"));
            Assert.That(text, Does.Contain("wz cmd 0.20 meas 0.18"));
            Assert.That(text, Does.Contain("cap 0.20"));
            Assert.That(text, Does.Contain("wcap 1.00"));
            Assert.That(text, Does.Contain("pose x 1.25 y -0.40 yaw 28.6 deg"));
            Assert.That(text, Does.Contain("frame odom"));
            Assert.That(text, Does.Contain("JOINTS"));
            Assert.That(text, Does.Contain("arm_shoulder_pan"));
            Assert.That(text, Does.Contain("11.5 deg"));
            Assert.That(text, Does.Contain("cmd 14.3"));
            Assert.That(text, Does.Contain("vel 0.10"));
            Assert.That(text, Does.Contain("eff 0.02"));
            Assert.That(text, Does.Contain("GRIPPER"));
            Assert.That(text, Does.Contain("pos 0.020 m cmd 0.030 m"));
            Assert.That(text, Does.Contain("IMU"));
            Assert.That(text, Does.Contain("roll 5.7 deg pitch -11.5 deg"));
            Assert.That(text, Does.Contain("ax 0.00"));
            Assert.That(text, Does.Contain("az 9.80"));
            Assert.That(text, Does.Contain("BATT 12.6 V 80 pct simulated"));
            Assert.That(text, Does.Contain("MODE base teleop arm teleop"));
            Assert.That(text, Does.Contain("ESTOP clear"));
            Assert.That(text, Does.Contain("DEADMAN asserted"));
            Assert.That(text, Does.Contain("SAFETY no-trip"));
            Assert.That(text, Does.Contain("LINK rtt 12.5 ms hz 20.0"));
            Assert.That(text, Does.Contain("NAV navigating"));
            Assert.That(text, Does.Contain("eta 4.5 s"));
            Assert.That(text, Does.Contain("POLICY smolvla running latency 18 ms"));
            Assert.That(text, Does.Contain("REC active ep1 frames 120"));
            Assert.That(text, Does.Contain("CPU 22.5 pct temp 48.2 C"));
            Assert.That(text, Does.Contain("STREAMS"));
            Assert.That(text, Does.Contain("VIDEO"));
            Assert.That(text, Does.Contain("SENT"));
            Assert.IsTrue(_hud.UsingTelemetry);
        }

        [Test]
        public void CompactMode_HidesDetailSections()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 10));
            _hud.Expanded = false;
            _hud.Write(10);
            string text = _hud.Text;
            Assert.That(text, Does.Contain("BASE"));
            Assert.That(text, Does.Contain("ESTOP clear"));
            Assert.That(text, Does.Contain("DEADMAN asserted"));
            Assert.That(text, Does.Contain("LINK rtt"));
            Assert.That(text, Does.Contain("IMU"));
            Assert.That(text, Does.Not.Contain("JOINTS"));
            Assert.That(text, Does.Not.Contain("GRIPPER"));
            Assert.That(text, Does.Not.Contain("NAV"));
            Assert.That(text, Does.Not.Contain("POLICY"));
            Assert.That(text, Does.Not.Contain("STREAMS"));
            Assert.That(text, Does.Not.Contain("CPU"));
            Assert.That(text, Does.Contain("grip"));
        }

        [Test]
        public void WrappedRosString_AndAgeStrip_RoundTripGeneratedType()
        {
            string wrapped = "{\"data\":" + JsonConvert.ToString(TelemetrySamples.Full) + "}";
            FieldAges ages = new FieldAges();
            TelemetryV1 sample;
            Assert.IsTrue(TelemetryIngest.TryParseTelemetry(wrapped, ages, out sample));
            Assert.AreEqual("omnibot-sim", sample.RobotId);
            Assert.AreEqual(RobotKind.Sim, sample.Kind);
            Assert.IsTrue(sample.Deadman);
            Assert.IsFalse(sample.Estop.Active);
            Assert.AreEqual(2, sample.Joints.Length);
            Assert.IsTrue(ages.HasAgeObject);
            Assert.IsTrue(ages.Base.Present);
            Assert.AreEqual(10, ages.Base.Ms);
            Assert.IsTrue(ages.TryJoint("arm_shoulder_pan", out double jointAge));
            Assert.AreEqual(20, jointAge);
            sample.Validate();
        }

        [Test]
        public void UnknownMember_IsRejected_AndDoesNotReplaceLastGoodSample()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 5));
            string bad = TelemetrySamples.Full.Replace("\"v\":\"1.0.0\"", "\"v\":\"1.0.0\",\"nope\":1");
            Assert.IsFalse(_hud.IngestTelemetry(bad, 6));
            _hud.Write(6);
            Assert.That(_hud.Text, Does.Contain("omnibot-sim"));
        }

        [Test]
        public void NumericBool_IsNotAPermit()
        {
            string bad = TelemetrySamples.RequiredOnly.Replace("\"deadman\":false", "\"deadman\":1");
            Assert.IsFalse(_hud.IngestTelemetry(bad, 1));
            Assert.IsTrue(_hud.IngestSafety(TelemetrySamples.GateNumericBool, 1));
            _hud.Write(1);
            Assert.That(_hud.Text, Does.Contain("DEADMAN not-a-permit"));
            Assert.That(_hud.Text, Does.Not.Contain("DEADMAN asserted"));
        }

        [Test]
        public void ExplicitSafetyWithoutAge_IsClear_AndFalseDeadmanIsNotAPermit()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.RequiredOnly, 3));
            _hud.Write(3);
            string text = _hud.Text;
            Assert.That(text, Does.Contain("ESTOP clear"));
            Assert.That(text, Does.Contain("DEADMAN not-a-permit"));
            Assert.That(text, Does.Contain("SAFETY no-trip"));
            Assert.That(text, Does.Contain("not_present"));
        }

        [Test]
        public void AgeObjectWithoutEstopKey_StaysUnmeasured()
        {
            string json = TelemetrySamples.RequiredOnly.Insert(
                TelemetrySamples.RequiredOnly.Length - 1,
                ",\"age_ms\":{\"link\":0}");
            Assert.IsTrue(_hud.IngestTelemetry(json, 4));
            _hud.Write(4);
            Assert.That(_hud.Text, Does.Contain("ESTOP unmeasured"));
            Assert.That(_hud.Text, Does.Contain("SAFETY unmeasured"));
            Assert.That(_hud.Text, Does.Not.Contain("ESTOP clear"));
        }

        [Test]
        public void StaleBase_IsGrey()
        {
            string stale = TelemetrySamples.Full.Replace("\"base\":10", "\"base\":400");
            Assert.IsTrue(_hud.IngestTelemetry(stale, 8));
            _hud.Write(8);
            Assert.That(_hud.Text, Does.Contain("<color=#8A93A0>BASE"));
        }

        [Test]
        public void FreshBase_IsNotGrey()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 8));
            _hud.Write(8);
            Assert.That(_hud.Text, Does.Not.Contain("<color=#8A93A0>BASE"));
        }

        [Test]
        public void JointNearLimit_TurnsRed_AndAmberInsideTheOuterBand()
        {
            Assert.AreEqual(LimitZone.Red, HudText.Zone(3.0, -3.14, 3.14));
            Assert.AreEqual(LimitZone.Amber, HudText.Zone(1.15, -1.57, 1.57));
            Assert.AreEqual(LimitZone.Ok, HudText.Zone(0.2, -3.14, 3.14));

            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.NearLimit, 9));
            _hud.Expanded = true;
            _hud.Write(9);
            Assert.That(_hud.Text, Does.Contain("RED"));
            Assert.That(_hud.Text, Does.Contain("<color=#E23B3B>"));
            Assert.That(_hud.Text, Does.Contain("|"));

            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.AmberJoint, 9));
            _hud.Write(9);
            Assert.That(_hud.Text, Does.Contain("AMBER"));
            Assert.That(_hud.Text, Does.Contain("<color=#E0A020>"));
        }

        [Test]
        public void SchemaSafetyTrip_AndGateWatchdog_DoNotReadAsClear()
        {
            Assert.IsTrue(_hud.IngestSafety(TelemetrySamples.SchemaSafety, 2));
            _hud.Write(2);
            string schema = _hud.Text;
            Assert.That(schema, Does.Contain("ESTOP TRIP"));
            Assert.That(schema, Does.Contain("SAFETY TRIP"));
            Assert.That(schema, Does.Contain("hardware"));
            Assert.That(schema, Does.Contain("latched"));
            Assert.That(schema, Does.Contain("DEADMAN not-a-permit"));
            Assert.That(schema, Does.Contain("fault"));

            TelemetryHudComposer gate = new TelemetryHudComposer();
            gate.Echo = new CommandEcho();
            Assert.IsTrue(gate.IngestSafety(TelemetrySamples.GateTrip, 2));
            gate.Write(2);
            Assert.That(gate.Text, Does.Contain("ESTOP TRIP"));
            Assert.That(gate.Text, Does.Contain("watchdog"));
            Assert.That(gate.Text, Does.Contain("DEADMAN not-a-permit"));

            TelemetryHudComposer open = new TelemetryHudComposer();
            open.Echo = new CommandEcho();
            Assert.IsTrue(open.IngestSafety(TelemetrySamples.GatePermit, 2));
            open.Write(2);
            Assert.That(open.Text, Does.Contain("ESTOP clear"));
            Assert.That(open.Text, Does.Contain("DEADMAN asserted"));
            Assert.That(open.Text, Does.Contain("SAFETY no-trip"));
        }

        [Test]
        public void AbsentTelemetry_FallsBackToProfileTopics_AndLocalEchoIsTheCommand()
        {
            _hud.NoteRawBase(1.5, -0.25, 0, 0, 0, 1, 0.4, 0, 0.2, true, true, 50);
            _hud.Echo.NoteTwist(0.05, 0.01, -0.2, 50);
            _hud.SetHardCaps(0.2, 1.0);
            _hud.Write(50);
            string text = _hud.Text;
            Assert.IsFalse(_hud.UsingTelemetry);
            Assert.That(text, Does.Contain("src profile"));
            Assert.That(text, Does.Contain("meas 0.40"));
            Assert.That(text, Does.Contain("cap 0.20"));
            Assert.That(text, Does.Contain("OVER"));
            Assert.That(text, Does.Not.Contain("cap 0.40"));
            Assert.That(text, Does.Contain("SENT vx 0.05 vy 0.01 wz -0.20"));
            Assert.AreEqual(0.2, _hud.MaxLinear);
            Assert.AreEqual(1.0, _hud.MaxAngular);
        }

        [Test]
        public void FreshTelemetry_WinsOverRaw_UntilItGoesQuiet()
        {
            _hud.NoteRawBase(9, 9, 0, 0, 0, 1, 0.01, 0, 0, true, true, 0);
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 10));
            _hud.Write(10);
            Assert.IsTrue(_hud.UsingTelemetry);
            Assert.That(_hud.Text, Does.Contain("src telemetry"));
            Assert.That(_hud.Text, Does.Contain("x 1.25"));
            Assert.That(_hud.Text, Does.Not.Contain("x 9.00"));

            _hud.Write(12);
            Assert.IsFalse(_hud.UsingTelemetry);
            Assert.That(_hud.Text, Does.Contain("src profile"));
            Assert.That(_hud.Text, Does.Contain("x 9.00"));
        }

        [Test]
        public void QuietTelemetryWithoutRaw_StaysOnScreenAndGoesGrey()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 1));
            _hud.Write(3);
            Assert.That(_hud.Text, Does.Contain("src telemetry-stale"));
            Assert.That(_hud.Text, Does.Contain("<color=#8A93A0>"));
            Assert.That(_hud.Text, Does.Contain("omnibot-sim"));
        }

        [Test]
        public void LocalEcho_ReportsAboutTwentyHertz()
        {
            CommandEcho echo = _hud.Echo;
            for (int i = 0; i < 21; i++)
                echo.NoteTwist(0.1, 0, 0.2, i * 0.05);
            float[] joints = { 0.1f, 0.2f, 0.3f };
            for (int i = 0; i < 21; i++)
                echo.NoteJoints(joints, joints.Length, i * 0.05);
            Assert.AreEqual(20, echo.TwistHz(1.0), 0.6);
            Assert.AreEqual(20, echo.JointHz(1.0), 0.6);
            _hud.IngestTelemetry(TelemetrySamples.Full, 1);
            _hud.Write(1);
            Assert.That(_hud.Text, Does.Contain("SENT vx 0.10"));
            Assert.That(_hud.Text, Does.Contain("joints 3"));
        }

        [Test]
        public void HeartbeatEcho_RttUsesTheMatchingSend()
        {
            _hud.NoteHeartbeatSent(7, 40);
            string echo = "{\"v\":\"1.0.0\",\"seq\":7,\"t_client\":40,\"t\":40.03}";
            Assert.IsTrue(_hud.IngestHeartbeatEcho(echo, 40.04));
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 40.04));
            _hud.Write(40.04);
            Assert.That(_hud.Text, Does.Contain("echo-rtt 40.0 ms"));
            Assert.That(_hud.Text, Does.Contain("seq 7"));
        }

        [Test]
        public void HardCap_IsTheValueGiven_NotRaisedForAFasterReading()
        {
            _hud.SetHardCaps(0.2, 1);
            _hud.SetHardCaps(0.5, 2);
            Assert.AreEqual(0.5, _hud.MaxLinear);
            Assert.AreEqual(2, _hud.MaxAngular);
            _hud.SetHardCaps(0, 0);
            _hud.SetHardCaps(double.NaN, double.PositiveInfinity);
            Assert.AreEqual(0.5, _hud.MaxLinear, "A non-positive or non-finite number does not replace the cap.");
        }

        [Test]
        public void ModeMap_MatchesAggregatorNames()
        {
            Assert.AreEqual("auto", ModeMap.Base("nav2"));
            Assert.AreEqual("teleop", ModeMap.Base("Teleop"));
            Assert.AreEqual("idle", ModeMap.Base("nope"));
            Assert.AreEqual("not_present", ModeMap.Arm("teleop", "policy", false));
            Assert.AreEqual("teleop", ModeMap.Arm("teleop", "policy", true));
            Assert.AreEqual("auto", ModeMap.Arm("vla", "teleop", true));
            Assert.AreEqual("hold", ModeMap.Arm("idle", "hold", true));
        }

        [Test]
        public void VideoAge_GreysAfterTheHint()
        {
            _hud.NoteVideo(true, 10.0, "/camera/front/image_raw", 10.1);
            _hud.Write(10.1);
            Assert.That(_hud.Text, Does.Contain("VIDEO age 100 ms"));
            Assert.That(_hud.Text, Does.Contain("/camera/front/image_raw"));
            Assert.That(_hud.Text, Does.Not.Contain("<color=#8A93A0>VIDEO"));

            _hud.NoteVideo(true, 10.0, "/camera/front/image_raw", 10.4);
            _hud.Write(10.4);
            Assert.That(_hud.Text, Does.Contain("<color=#8A93A0>VIDEO"));
            Assert.That(_hud.Text, Does.Contain("age 400 ms"));
        }

        [Test]
        public void SeventyTwoHertz_RefreshesAtTenHertz()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 0));
            int refreshes = 0;
            for (int i = 0; i < 72; i++)
            {
                if (_hud.ConsumeFrame(1f / 72f, i / 72.0)) refreshes++;
            }
            Assert.GreaterOrEqual(refreshes, 9);
            Assert.LessOrEqual(refreshes, 11);
            Assert.Less(refreshes, 72);
        }

        [Test]
        public void IdleFrames_AndRepeatFormat_AllocateNothing()
        {
            Assert.IsTrue(_hud.IngestTelemetry(TelemetrySamples.Full, 10));
            _hud.Expanded = true;
            Assert.IsTrue(_hud.ConsumeFrame(0.1f, 10));
            Assert.IsTrue(_hud.ConsumeFrame(0.1f, 10));
            _hud.Expanded = false;
            Assert.IsTrue(_hud.ConsumeFrame(0.1f, 10));
            _hud.Expanded = true;
            Assert.IsTrue(_hud.ConsumeFrame(0.1f, 10));

            bool idleA, idleB, idleC;
            long before = GC.GetAllocatedBytesForCurrentThread();
            idleA = _hud.ConsumeFrame(0.01f, 10);
            idleB = _hud.ConsumeFrame(0.01f, 10);
            idleC = _hud.ConsumeFrame(0.01f, 10);
            long idle = GC.GetAllocatedBytesForCurrentThread();
            Assert.IsFalse(idleA);
            Assert.IsFalse(idleB);
            Assert.IsFalse(idleC);
            Assert.AreEqual(0, idle - before, "Idle frames must not allocate.");

            bool wrote;
            long formatBefore = GC.GetAllocatedBytesForCurrentThread();
            wrote = _hud.ConsumeFrame(0.1f, 10);
            long formatted = GC.GetAllocatedBytesForCurrentThread();
            Assert.IsTrue(wrote);
            Assert.AreEqual(0, formatted - formatBefore, "A warmed 10 Hz refresh must not allocate.");

            CommandEcho echo = _hud.Echo;
            echo.NoteTwist(0, 0, 0, 0);
            long echoBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
                echo.NoteTwist(0.1, -0.02, 0.3, i * 0.05);
            long echoAfter = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, echoAfter - echoBefore);
        }

        [Test]
        public void MalformedJson_ReturnsFalse()
        {
            FieldAges ages = new FieldAges();
            TelemetryV1 sample;
            Assert.IsFalse(TelemetryIngest.TryParseTelemetry("{", ages, out sample));
            Assert.IsFalse(TelemetryIngest.TryParseTelemetry("", ages, out sample));
            Assert.IsNull(sample);
            SafetyReading reading;
            Assert.IsFalse(TelemetryIngest.TryParseSafety("[]", out reading));
            Assert.IsFalse(reading.Measured);
        }

        [Test]
        public void Orientation_IdentityAndYaw()
        {
            double roll, pitch, yaw;
            Assert.IsTrue(HudText.TryRpy(0, 0, 0, 1, out roll, out pitch, out yaw));
            Assert.AreEqual(0, roll, 1e-9);
            Assert.AreEqual(0, pitch, 1e-9);
            Assert.AreEqual(0, yaw, 1e-9);
            double half = Math.Sin(Math.PI / 4);
            Assert.IsTrue(HudText.TryRpy(0, 0, half, half, out roll, out pitch, out yaw));
            Assert.AreEqual(Math.PI / 2, yaw, 1e-9);
        }
    }
}
