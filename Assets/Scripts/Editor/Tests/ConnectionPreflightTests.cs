using System.Collections.Generic;
using NUnit.Framework;
using OmniBot.VR.Control.Preflight;
using OmniBot.VR.Control.Session;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Connection preflight against a fake rosbridge probe: a healthy robot,
    /// a sim missing the arm mux, a bad protocolVersion, a latched e-stop,
    /// video down, and real-versus-sim gating.
    /// </summary>
    public class ConnectionPreflightTests
    {
        private const string VideoUrl = "http://10.0.0.8:8080/stream?topic=/camera/front/image_raw";

        [Test]
        public void Pass_AllChecksClear_AllowsRealTeleop()
        {
            var report = ConnectionPreflight.Run(Healthy(), Request(SessionKind.Real));

            Assert.IsFalse(report.BlocksTeleop);
            Assert.AreEqual("Preflight passed.", report.Summary);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Handshake).Status);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Descriptor).Status);
            StringAssert.Contains("protocolVersion 1.0.0 is compatible", report.Find(PreflightCheckId.Descriptor).Detail);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Topics).Status);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Safety).Status);
            StringAssert.Contains("E-stop is clear", report.Find(PreflightCheckId.Safety).Detail);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Video).Status);
            Assert.AreEqual(PreflightStatus.Pass, report.Find(PreflightCheckId.Rtt).Status);
            StringAssert.Contains("RTT 12 ms", report.Find(PreflightCheckId.Rtt).Detail);
            StringAssert.Contains("teleop: allowed", report.Diagnostics);
        }

        [Test]
        public void MissingMuxTopics_FailsWithClearMessage()
        {
            var link = Healthy();
            link.Topics = new[]
            {
                "/cmd_vel", "/cmd_vel/teleop", "/odom", "/emergency_stop",
                "/arm/joint_commands", "/ohho/descriptor", "/camera/front/image_raw",
            };

            var sim = ConnectionPreflight.Run(link, Request(SessionKind.Sim));
            var topics = sim.Find(PreflightCheckId.Topics);

            Assert.AreEqual(PreflightStatus.Fail, topics.Status);
            StringAssert.Contains("Arm mux is not running", topics.Detail);
            StringAssert.Contains("/arm/joint_commands/out", topics.Detail);
            StringAssert.Contains("/cmd_vel/out", topics.Detail);
            Assert.IsFalse(sim.BlocksTeleop);
            StringAssert.Contains("Simulation is not blocked", sim.Summary);

            var real = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            Assert.IsTrue(real.BlocksTeleop);
            StringAssert.Contains("Teleop blocked", real.Summary);
            StringAssert.Contains("Arm mux is not running", real.Diagnostics);
        }

        [Test]
        public void IncompatibleProtocolVersion_Fails()
        {
            var link = Healthy();
            link.DescriptorMessage = Wrap(Descriptor(protocolVersion: "2.0.0", includeProtocolField: true));

            var report = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            var descriptor = report.Find(PreflightCheckId.Descriptor);

            Assert.AreEqual(PreflightStatus.Fail, descriptor.Status);
            Assert.IsTrue(descriptor.Safety);
            StringAssert.Contains("protocolVersion 2.0.0 is not compatible", descriptor.Detail);
            Assert.IsTrue(report.BlocksTeleop);

            var sim = ConnectionPreflight.Run(link, Request(SessionKind.Sim));
            Assert.IsFalse(sim.BlocksTeleop);
            StringAssert.Contains("protocolVersion 2.0.0 is not compatible", sim.Find(PreflightCheckId.Descriptor).Detail);
        }

        [Test]
        public void SafetyLatched_FailsClosed()
        {
            var link = Healthy();
            link.SafetyMessage = Wrap(
                "{\"v\":\"1.0.0\",\"t\":1735689600.5,\"robot_id\":\"omnibot-sim\",\"kind\":\"sim\"," +
                "\"mode\":{\"base\":\"idle\",\"arm\":\"not_present\"}," +
                "\"estop\":{\"active\":false,\"latched\":true,\"source\":\"watchdog\"}," +
                "\"deadman\":true,\"link\":{\"rtt_ms\":0,\"hz\":0}}");

            var report = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            var safety = report.Find(PreflightCheckId.Safety);

            Assert.AreEqual(PreflightStatus.Fail, safety.Status);
            StringAssert.Contains("E-stop is latched", safety.Detail);
            StringAssert.Contains("watchdog", safety.Detail);
            Assert.IsTrue(report.BlocksTeleop);
        }

        [Test]
        public void MissingSafetyState_IsNotTreatedAsClear()
        {
            var link = Healthy();
            link.SafetyMessage = null;

            var report = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            StringAssert.Contains("not a permit", report.Find(PreflightCheckId.Safety).Detail);
            Assert.IsTrue(report.BlocksTeleop);
        }

        [Test]
        public void NoVideo_FailsWithoutBlockingTeleop()
        {
            var link = Healthy();
            link.Video = VideoProbeState.Unreachable;
            link.VideoDetail = VideoUrl;

            var real = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            var video = real.Find(PreflightCheckId.Video);

            Assert.AreEqual(PreflightStatus.Fail, video.Status);
            Assert.IsFalse(video.Safety);
            StringAssert.Contains("Video is not reachable", video.Detail);
            StringAssert.Contains(VideoUrl, video.Detail);
            Assert.IsFalse(real.BlocksTeleop);
            Assert.AreEqual(PreflightStatus.Pass, real.Find(PreflightCheckId.Safety).Status);
        }

        [Test]
        public void RealBlocksOnSafetyFailure_SimWarnsOnly()
        {
            var link = Healthy();
            link.SafetyMessage = Wrap(
                "{\"v\":\"1.0.0\",\"t\":1,\"robot_id\":\"omnibot\",\"kind\":\"real\"," +
                "\"mode\":{\"base\":\"idle\",\"arm\":\"idle\"}," +
                "\"estop\":{\"active\":false,\"latched\":true,\"source\":\"hardware\"}," +
                "\"deadman\":false,\"link\":{\"rtt_ms\":1,\"hz\":20}}");

            var real = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            var sim = ConnectionPreflight.Run(link, Request(SessionKind.Sim));

            Assert.AreEqual(PreflightStatus.Fail, real.Find(PreflightCheckId.Safety).Status);
            Assert.AreEqual(PreflightStatus.Fail, sim.Find(PreflightCheckId.Safety).Status);
            Assert.IsTrue(real.BlocksTeleop);
            Assert.IsFalse(sim.BlocksTeleop);
            StringAssert.Contains("teleop: blocked", real.Diagnostics);
            StringAssert.Contains("teleop: allowed", sim.Diagnostics);
            StringAssert.Contains("Simulation is not blocked", sim.Summary);
            StringAssert.Contains("E-stop is latched", sim.Summary);
        }

        [Test]
        public void RaisedHardCap_FailsDescriptor()
        {
            var link = Healthy();
            link.DescriptorMessage = Wrap(Descriptor(maxVx: "0.21"));

            var report = ConnectionPreflight.Run(link, Request(SessionKind.Real));
            StringAssert.Contains("exceeds the ceiling 0.2", report.Find(PreflightCheckId.Descriptor).Detail);
            Assert.IsTrue(report.BlocksTeleop);
        }

        private static PreflightRequest Request(SessionKind kind)
        {
            return new PreflightRequest
            {
                Kind = kind,
                RosbridgeUrl = "ws://10.0.0.8:9090",
            };
        }

        private static FakePreflightLink Healthy()
        {
            return new FakePreflightLink
            {
                IsConnected = true,
                DescriptorMessage = Wrap(Descriptor()),
                SafetyMessage = Wrap(
                    "{\"v\":\"1.0.0\",\"t\":1735689600.5,\"robot_id\":\"omnibot\",\"kind\":\"sim\"," +
                    "\"mode\":{\"base\":\"idle\",\"arm\":\"idle\"}," +
                    "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"}," +
                    "\"deadman\":false,\"link\":{\"rtt_ms\":5,\"hz\":20}}"),
                Topics = new[]
                {
                    "/cmd_vel", "/cmd_vel/out", "/cmd_vel/teleop", "/odom", "/emergency_stop",
                    "/arm/joint_commands", "/arm/joint_commands/out",
                    "/ohho/descriptor", "/ohho/safety/state", "/camera/front/image_raw",
                },
                TopicsOk = true,
                Video = VideoProbeState.Reachable,
                VideoDetail = VideoUrl,
                RttOk = true,
                RttMs = 12,
            };
        }

        private static string Descriptor(string protocolVersion = null, bool includeProtocolField = false, string maxVx = "0.2")
        {
            string protocol = includeProtocolField
                ? "\"protocolVersion\":\"" + protocolVersion + "\","
                : "";
            return "{" + protocol +
                   "\"v\":\"1.0.0\",\"id\":\"omnibot\",\"model\":\"OmniBot\"," +
                   "\"drive\":{\"kind\":\"mecanum\",\"holonomic\":true,\"caps\":{\"vx_mps\":0.2,\"vy_mps\":0.2,\"wz_rad_s\":1.0}}," +
                   "\"arms\":[{\"name\":\"so101\",\"joints\":[]}]," +
                   "\"topics\":{\"cmd\":\"/cmd_vel\",\"state\":\"/odom\",\"estop\":\"/emergency_stop\"," +
                   "\"heartbeat\":\"/ohho/heartbeat\",\"telemetry\":\"/ohho/telemetry\"," +
                   "\"images\":[\"/camera/front/image_raw\"]}," +
                   "\"cameras\":[{\"name\":\"front\",\"kind\":\"rgb\",\"topic\":\"/camera/front/image_raw\"}]," +
                   "\"urdf_url\":\"package://omnibot_description/urdf/omnibot.urdf.xacro\"," +
                   "\"apriltags\":[]," +
                   "\"telemetry\":{\"schema_id\":\"ohho.telemetry.v1\",\"robot_id\":\"omnibot\",\"kind\":\"sim\",\"fields\":[\"base\"]}," +
                   "\"safety\":{\"hard\":{" +
                   "\"max_vx_mps\":" + maxVx + ",\"max_vy_mps\":0.2,\"max_wz_rad_s\":1.0," +
                   "\"max_accel_x_mps2\":0.5,\"max_accel_y_mps2\":0.5,\"max_accel_wz_rad_s2\":2.0," +
                   "\"max_joint_step_rad\":0.15}}}";
        }

        private static string Wrap(string json)
        {
            return "{\"data\":\"" + json.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
        }

        /// <summary>In-memory rosbridge. Answers inside the call so the checker stays synchronous.</summary>
        private sealed class FakePreflightLink : IPreflightLink
        {
            public bool IsConnected { get; set; }
            public string DescriptorMessage;
            public string SafetyMessage;
            public string[] Topics = System.Array.Empty<string>();
            public bool TopicsOk = true;
            public string TopicsError = "";
            public VideoProbeState Video = VideoProbeState.NotProbed;
            public string VideoDetail;
            public bool RttOk;
            public double RttMs;
            public string RttDetail = "";

            public string Message(string topic)
            {
                if (topic == PreflightTopics.Descriptor) return DescriptorMessage;
                if (topic == PreflightTopics.SafetyState) return SafetyMessage;
                return null;
            }

            public bool TryService(string service, out string valuesJson, out string error)
            {
                valuesJson = null;
                error = null;
                if (service != PreflightTopics.TopicsService)
                {
                    error = "Unknown service " + service;
                    return false;
                }
                if (!TopicsOk)
                {
                    error = string.IsNullOrEmpty(TopicsError) ? "rosapi/topics failed." : TopicsError;
                    return false;
                }
                var names = new List<string>();
                if (Topics != null)
                {
                    for (int i = 0; i < Topics.Length; i++)
                        names.Add("\"" + Topics[i] + "\"");
                }
                valuesJson = "{\"topics\":[" + string.Join(",", names) + "],\"types\":[]}";
                return true;
            }

            public void ReadVideo(out VideoProbeState state, out string detail)
            {
                state = Video;
                detail = VideoDetail;
            }

            public bool TryRtt(out double rttMs, out string detail)
            {
                rttMs = RttMs;
                detail = RttDetail;
                return RttOk;
            }
        }
    }
}
