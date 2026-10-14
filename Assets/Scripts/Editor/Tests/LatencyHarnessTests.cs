using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmniBot.VR.Core;
using OmniBot.VR.Latency;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Fake-link coverage for the latency harness. No Quest, no robot, no socket.
    /// </summary>
    public class LatencyHarnessTests
    {
        private const string MovingTwist =
            "{\"linear\":{\"x\":0.1,\"y\":0,\"z\":0},\"angular\":{\"x\":0,\"y\":0,\"z\":0}}";

        private const string ZeroTwist =
            "{\"linear\":{\"x\":0,\"y\":0,\"z\":0},\"angular\":{\"x\":0,\"y\":0,\"z\":0}}";

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("OHHO_LATENCY_LOG", null);
            Environment.SetEnvironmentVariable("OHHO_LATENCY_PROFILE", null);
            Environment.SetEnvironmentVariable("OHHO_LATENCY_HARDWARE", null);
            LatencyCapture.ResetForTests();
        }

        [Test]
        public void FakeLink_LocalProfile_RecordsEverySegmentInsideBudget()
        {
            ManualLatencyClock clock = new ManualLatencyClock(1_000_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            FakeRobotLink link = new FakeRobotLink(session, clock);
            link.Connect();
            string delivered = null;
            link.Subscribe("/odom", "nav_msgs/Odometry", json => delivered = json);

            session.MarkInput();
            clock.Advance(12);
            link.Publish(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            clock.Advance(40);
            session.NoteVideo(clock.NowMs - 30, clock.NowMs, "WebRTC");
            Assert.AreEqual(2, link.Pump());

            Assert.AreEqual(MovingTwist, link.Published[0].Json);
            Assert.IsNotNull(delivered);
            StringAssert.Contains("\"x\":0.1", delivered);
            StringAssert.DoesNotContain("rewritten", delivered);

            LatencyReport report = session.BuildReport();
            Assert.IsFalse(report.HardwareRun);
            AssertSample(report, "input_to_command", 12);
            AssertSample(report, "command_to_motion", 40);
            AssertSample(report, "glass_to_glass", 30);
            AssertSample(report, "telemetry_age", 15);
            AssertSample(report, "heartbeat_age", 10);
            AssertVerdict(report, "input_to_command", LatencyVerdict.Pass);
            AssertVerdict(report, "command_to_motion", LatencyVerdict.Pass);
            AssertVerdict(report, "glass_to_glass", LatencyVerdict.Pass);
            AssertVerdict(report, "telemetry_age", LatencyVerdict.Pass);
            AssertVerdict(report, "heartbeat_age", LatencyVerdict.Pass);
        }

        [Test]
        public void WrapperPublish_PairsInputToCommand()
        {
            ManualLatencyClock clock = new ManualLatencyClock(5_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            clock.Advance(8);
            string wrapper = "{\"op\":\"publish\",\"topic\":\"" + RobotConfig.TOPIC_CMD_VEL
                + "\",\"msg\":" + MovingTwist + "}";
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, wrapper);
            LatencyReport report = session.BuildReport();
            AssertSample(report, "input_to_command", 8);
            AssertVerdict(report, "input_to_command", LatencyVerdict.Pass);
        }

        [Test]
        public void CloudAndReal_InputToCommand_NeedsStampedRemoteAck()
        {
            LatencyProfile[] profiles = { LatencyProfile.Cloud, LatencyProfile.Real };
            for (int i = 0; i < profiles.Length; i++)
            {
                ManualLatencyClock clock = new ManualLatencyClock(1_000_000);
                LatencySession session = new LatencySession(profiles[i], clock);
                session.MarkInput();
                clock.Advance(12);
                session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
                AssertVerdict(session.BuildReport(), "input_to_command", LatencyVerdict.NotMeasured);

                long ack = 1_000_000 + 30;
                session.ObserveIncoming(
                    LatencyBudget.CommandAckTopic,
                    "{\"header\":{\"stamp\":" + FakeRobotLink.StampJson(ack) + "}}");
                LatencySegmentSummary row = session.BuildReport().Find("input_to_command");
                Assert.AreEqual(LatencyVerdict.Pass, row.Verdict, profiles[i].ToString());
                Assert.AreEqual(30L, row.P95Ms.Value);
            }
        }

        [Test]
        public void LateMotion_FailsBudget_AndLaterPublishStillAccepted()
        {
            ManualLatencyClock clock = new ManualLatencyClock(1_000_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            FakeRobotLink link = new FakeRobotLink(session, clock)
            {
                AutoHeartbeat = false,
                MotionDelayMs = 400,
                TelemetryLagMs = 10
            };
            link.Connect();
            session.MarkInput();
            clock.Advance(10);
            link.Publish(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            clock.Advance(400);
            link.Pump();
            session.MarkInput();
            link.Publish(RobotConfig.TOPIC_CMD_VEL, MovingTwist);

            Assert.AreEqual(2, link.Published.Count);
            Assert.AreEqual(MovingTwist, link.Published[0].Json);
            Assert.AreEqual(MovingTwist, link.Published[1].Json);
            AssertVerdict(session.BuildReport(), "command_to_motion", LatencyVerdict.Fail);
        }

        [Test]
        public void ZeroTwist_DoesNotArmCommandToMotion()
        {
            ManualLatencyClock clock = new ManualLatencyClock(10_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            FakeRobotLink link = new FakeRobotLink(session, clock) { AutoHeartbeat = false };
            link.Connect();
            session.MarkInput();
            link.Publish(RobotConfig.TOPIC_CMD_VEL, ZeroTwist);
            clock.Advance(50);
            Assert.AreEqual(0, link.Pump());
            AssertVerdict(session.BuildReport(), "command_to_motion", LatencyVerdict.NotMeasured);
        }

        [Test]
        public void EmergencyStop_PayloadUnchanged_DoesNotArmMotion()
        {
            const string payload = "{\"data\":true}";
            ManualLatencyClock clock = new ManualLatencyClock(10_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            FakeRobotLink link = new FakeRobotLink(session, clock) { AutoHeartbeat = false };
            link.Connect();
            link.Publish(RobotConfig.TOPIC_EMERGENCY_STOP, payload);
            clock.Advance(80);
            link.Pump();

            Assert.AreEqual(1, link.Published.Count);
            Assert.AreEqual(RobotConfig.TOPIC_EMERGENCY_STOP, link.Published[0].Topic);
            Assert.AreEqual(payload, link.Published[0].Json);
            AssertVerdict(session.BuildReport(), "command_to_motion", LatencyVerdict.NotMeasured);
            Assert.IsFalse(HasSegment(session.BuildReport(), "command_to_motion"));
        }

        [Test]
        public void StationaryOdom_DoesNotCountAsMotion()
        {
            ManualLatencyClock clock = new ManualLatencyClock(20_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            clock.Advance(20);
            session.ObserveIncoming(RobotConfig.TOPIC_ODOM, Odom(clock.NowMs - 10, 0));
            LatencyReport report = session.BuildReport();
            AssertVerdict(report, "command_to_motion", LatencyVerdict.Incomplete);
            AssertSample(report, "telemetry_age", 10);
            AssertVerdict(report, "telemetry_age", LatencyVerdict.Pass);
        }

        [Test]
        public void GarbageAndNull_DoNotThrowOrInventLatency()
        {
            LatencySession session = new LatencySession(LatencyProfile.Local, new ManualLatencyClock(100));
            Assert.DoesNotThrow(() => session.ObserveOutgoing(null, null));
            Assert.DoesNotThrow(() => session.ObserveIncoming(null, "{"));
            Assert.DoesNotThrow(() => session.ObserveIncoming(RobotConfig.TOPIC_ODOM, "{{{"));
            Assert.DoesNotThrow(() => session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, "not-json"));
            LatencyReport report = session.BuildReport();
            Assert.IsFalse(HasSegment(report, "input_to_command"));
            for (int i = 0; i < report.Samples.Count; i++)
                Assert.IsFalse(report.Samples[i].LatencyMs.HasValue);
        }

        [Test]
        public void CsvAndJson_RoundTrip()
        {
            ManualLatencyClock clock = new ManualLatencyClock(1_000_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            clock.Advance(12);
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            session.NoteVideo(clock.NowMs - 30, clock.NowMs, "WebRTC");
            LatencyReport report = session.BuildReport();

            string csv = LatencyLog.ToCsv(report);
            StringAssert.Contains("t_unix_ms,kind,segment,latency_ms,topic,ack,transport,profile", csv);
            StringAssert.Contains("input_to_command", csv);
            StringAssert.Contains("glass_to_glass", csv);

            string json = LatencyLog.ToJson(report);
            StringAssert.Contains("\"hardware_run\": false", json);
            StringAssert.Contains("\"latency_ms\": 12", json);
            StringAssert.Contains("\"verdict\": \"pass\"", json);

            string dir = Path.Combine(Path.GetTempPath(), "ohho-latency-" + Guid.NewGuid().ToString("N"));
            try
            {
                LatencyLog.Write(dir, report);
                Assert.IsTrue(File.Exists(Path.Combine(dir, "latency-local.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "latency-local.json")));
                string written = File.ReadAllText(Path.Combine(dir, "latency-local.json"));
                StringAssert.Contains("\"profile\": \"local\"", written);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void BudgetsJson_MatchesCode()
        {
            string path = FindUp(Path.Combine("docs", "latency-budgets.json"));
            string actual = File.ReadAllText(path).Replace("\r\n", "\n");
            if (!actual.EndsWith("\n")) actual += "\n";
            Assert.AreEqual(LatencyBudget.CanonicalJson(), actual);
        }

        [Test]
        public void Doc_ListsEveryBudget()
        {
            string path = FindUp(Path.Combine("docs", "LATENCY_BUDGETS.md"));
            string doc = File.ReadAllText(path);
            for (int p = 0; p < LatencyBudget.ProfileCount; p++)
            {
                var profile = (LatencyProfile)p;
                for (int s = 0; s < LatencyBudget.SegmentCount; s++)
                {
                    var segment = (LatencySegment)s;
                    string row = "| " + LatencyBudget.ProfileLabel(profile)
                        + " | `" + LatencyBudget.SegmentId(segment) + "` | "
                        + LatencyBudget.P95Milliseconds(profile, segment) + " |";
                    StringAssert.Contains(row, doc);
                }
            }
        }

        [Test]
        public void JpegCaptureStamp_ReadsMarker()
        {
            Assert.IsFalse(JpegCaptureStamp.TryReadUnixMs(new byte[] { 1, 2, 3, 4 }).HasValue);
            byte[] framed = Encoding.ASCII.GetBytes("xxOHHO_CAP:1700000001234yy");
            Assert.AreEqual(1700000001234L, JpegCaptureStamp.TryReadUnixMs(framed).Value);
            Assert.IsFalse(JpegCaptureStamp.TryReadUnixMs(Encoding.ASCII.GetBytes("OHHO_CAP:")).HasValue);
        }

        [Test]
        public void Percentile_NearestRank()
        {
            long[] values = new long[100];
            for (int i = 0; i < values.Length; i++) values[i] = i + 1;
            Assert.AreEqual(95L, LatencyStats.PercentileNearestRank(values, 0.95));
            Assert.AreEqual(50L, LatencyStats.PercentileNearestRank(values, 0.50));
            Assert.AreEqual(7L, LatencyStats.PercentileNearestRank(new long[] { 7 }, 0.95));
        }

        [Test]
        public void CaptureDisabled_IsNoOp()
        {
            Assert.IsFalse(LatencyCapture.IsEnabled);
            LatencyCapture.MarkInputSample();
            LatencyCapture.ClearInputSample();
            LatencyCapture.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            LatencyCapture.ObserveIncoming(RobotConfig.TOPIC_ODOM, "{}");
            LatencyCapture.NoteVideoFrame(1, 2, "MJPEG");
            LatencyCapture.StopAndWrite();
            Assert.IsNull(LatencyCapture.Active);
        }

        [Test]
        public void SupersededCommand_IsIncompleteNotAPass()
        {
            ManualLatencyClock clock = new ManualLatencyClock(8_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            clock.Advance(5);
            session.MarkInput();
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            LatencySegmentSummary row = session.BuildReport().Find("command_to_motion");
            Assert.AreEqual(LatencyVerdict.Incomplete, row.Verdict);
            Assert.Greater(row.Incomplete, 0);
            Assert.IsFalse(row.P95Ms.HasValue);
        }

        [Test]
        public void FutureStamp_FailsTelemetryAge()
        {
            ManualLatencyClock clock = new ManualLatencyClock(5_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            session.ObserveOutgoing(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            session.ObserveIncoming(RobotConfig.TOPIC_ODOM, Odom(clock.NowMs + 5, 0.1));
            AssertVerdict(session.BuildReport(), "telemetry_age", LatencyVerdict.Fail);
        }

        [Test]
        public void JointPositionDelta_ClosesCommandToMotion()
        {
            ManualLatencyClock clock = new ManualLatencyClock(2_000_000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            session.MarkInput();
            session.ObserveOutgoing(RobotConfig.TOPIC_ARM_COMMANDS, "{\"position\":[0,0]}");
            session.ObserveOutgoing(RobotConfig.TOPIC_ARM_COMMANDS, "{\"position\":[0.4,0]}");
            session.ObserveIncoming(RobotConfig.TOPIC_ARM_STATES, JointState(clock.NowMs, "0,0"));
            AssertVerdict(session.BuildReport(), "command_to_motion", LatencyVerdict.Incomplete);
            clock.Advance(30);
            session.ObserveIncoming(RobotConfig.TOPIC_ARM_STATES, JointState(clock.NowMs, "0.4,0"));
            LatencyReport report = session.BuildReport();
            AssertSample(report, "command_to_motion", 30);
            AssertVerdict(report, "command_to_motion", LatencyVerdict.Pass);
        }

        [Test]
        public void DisconnectedPublish_IsDropped()
        {
            ManualLatencyClock clock = new ManualLatencyClock(1000);
            LatencySession session = new LatencySession(LatencyProfile.Local, clock);
            FakeRobotLink link = new FakeRobotLink(session, clock) { AutoHeartbeat = false };
            link.Publish(RobotConfig.TOPIC_CMD_VEL, MovingTwist);
            Assert.AreEqual(0, link.Published.Count);
            Assert.AreEqual(0, session.BuildReport().Samples.Count);
        }

        [Test]
        public void TryStartFromEnvironment_ReadsProfileAndHardwareFlag()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ohho-latency-env-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.IsFalse(LatencyCapture.TryStartFromEnvironment());
                Environment.SetEnvironmentVariable("OHHO_LATENCY_LOG", dir);
                Environment.SetEnvironmentVariable("OHHO_LATENCY_PROFILE", "not-a-profile");
                Assert.IsFalse(LatencyCapture.TryStartFromEnvironment());

                Environment.SetEnvironmentVariable("OHHO_LATENCY_PROFILE", "cloud");
                Environment.SetEnvironmentVariable("OHHO_LATENCY_HARDWARE", "1");
                Assert.IsTrue(LatencyCapture.TryStartFromEnvironment());
                Assert.AreEqual(LatencyProfile.Cloud, LatencyCapture.Active.Profile);
                Assert.IsTrue(LatencyCapture.Active.HardwareRun);
                LatencyCapture.Active.MarkInput();
                LatencyCapture.StopAndWrite();
                Assert.IsNull(LatencyCapture.Active);
                string json = File.ReadAllText(Path.Combine(dir, "latency-cloud.json"));
                StringAssert.Contains("\"hardware_run\": true", json);
                StringAssert.Contains("\"profile\": \"cloud\"", json);
            }
            finally
            {
                LatencyCapture.ResetForTests();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static string Odom(long stampMs, double linearX)
        {
            return FakeRobotLink.OdomJson(stampMs, linearX);
        }

        private static string JointState(long stampMs, string positions)
        {
            return "{\"header\":{\"stamp\":" + FakeRobotLink.StampJson(stampMs)
                + "},\"name\":[\"j0\",\"j1\"],\"position\":[" + positions + "],\"velocity\":[0,0]}";
        }

        private static void AssertSample(LatencyReport report, string segment, long latencyMs)
        {
            LatencySample found = null;
            for (int i = 0; i < report.Samples.Count; i++)
            {
                LatencySample sample = report.Samples[i];
                if (sample.Segment == segment && sample.LatencyMs.HasValue && sample.LatencyMs.Value == latencyMs)
                {
                    found = sample;
                    break;
                }
            }
            Assert.IsNotNull(found, segment + " latency " + latencyMs);
        }

        private static void AssertVerdict(LatencyReport report, string segment, string verdict)
        {
            LatencySegmentSummary row = report.Find(segment);
            Assert.IsNotNull(row, segment);
            Assert.AreEqual(verdict, row.Verdict, segment);
        }

        private static bool HasSegment(LatencyReport report, string segment)
        {
            for (int i = 0; i < report.Samples.Count; i++)
            {
                if (report.Samples[i].Segment == segment) return true;
            }
            return false;
        }

        private static string FindUp(string relative)
        {
            string[] starts = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
            for (int s = 0; s < starts.Length; s++)
            {
                DirectoryInfo dir = new DirectoryInfo(starts[s]);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            Assert.Fail("Could not find " + relative);
            return null;
        }
    }
}
