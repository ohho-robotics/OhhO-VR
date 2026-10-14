using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using OmniBot.VR.Core;
using OmniBot.VR.Metrics;

namespace OmniBot.VR.Tests
{
    public class FirstDriveClockTests
    {
        private double _now;

        private FirstDriveClock ClockAtZero()
        {
            _now = 0d;
            return new FirstDriveClock(() => _now);
        }

        [Test]
        public void Observe_BeforeConnect_DoesNotRecord()
        {
            var clock = ClockAtZero();
            _now = 5d;
            Assert.IsFalse(clock.ObserveVelocity(FirstDriveClock.TeleopCmdVelTopic, 0.2d, 0d, 0d));
            Assert.IsFalse(clock.HasFirstDrive);
            Assert.IsNull(clock.FormatLogLine());
        }

        [Test]
        public void ZeroTwist_DoesNotCountAsDrive()
        {
            var clock = ClockAtZero();
            _now = 2d;
            clock.MarkConnected();
            _now = 3d;
            var zero = new TwistMsg(new Vector3Msg(0f, 0f, 0f), new Vector3Msg(0f, 0f, 0f));
            Assert.IsFalse(clock.ObserveMessage(FirstDriveClock.TeleopCmdVelTopic, zero));
            Assert.IsFalse(clock.HasFirstDrive);
        }

        [Test]
        public void EpsilonOrBelow_DoesNotCount()
        {
            var clock = ClockAtZero();
            clock.MarkConnected();
            _now = 1d;
            Assert.IsFalse(clock.ObserveVelocity(
                FirstDriveClock.TeleopCmdVelTopic, FirstDriveClock.MotionEpsilon, 0d, 0d));
            Assert.IsFalse(clock.HasFirstDrive);
        }

        [Test]
        public void NonZeroLinearX_RecordsSinceConnectAndLaunch()
        {
            var clock = ClockAtZero();
            _now = 10d;
            clock.MarkConnected();
            _now = 12d;
            clock.MarkConnected();
            _now = 15.5d;
            Assert.IsTrue(clock.ObserveVelocity(FirstDriveClock.TeleopCmdVelTopic, 0.2d, 0d, 0d));
            Assert.AreEqual(5.5d, clock.SecondsSinceConnect.Value, 1e-9);
            Assert.AreEqual(15.5d, clock.SecondsSinceLaunch.Value, 1e-9);
            Assert.AreEqual(FirstDriveClock.TeleopCmdVelTopic, clock.Topic);
        }

        [Test]
        public void StrafeAndYaw_EachCountOnTheirOwn()
        {
            var strafe = ClockAtZero();
            strafe.MarkConnected();
            _now = 4d;
            Assert.IsTrue(strafe.ObserveVelocity("/cmd_vel/teleop", 0d, -0.05d, 0d));

            var yaw = ClockAtZero();
            yaw.MarkConnected();
            _now = 4d;
            Assert.IsTrue(yaw.ObserveVelocity("/cmd_vel", 0d, 0d, 0.3d));
            Assert.AreEqual("/cmd_vel", yaw.Topic);
        }

        [Test]
        public void SecondCommand_DoesNotMoveTheTimestamp()
        {
            var clock = ClockAtZero();
            clock.MarkConnected();
            _now = 3d;
            Assert.IsTrue(clock.ObserveVelocity("/cmd_vel/teleop", 0.1d, 0d, 0d));
            _now = 9d;
            Assert.IsFalse(clock.ObserveVelocity("/cmd_vel/teleop", 0.2d, 0d, 0d));
            Assert.AreEqual(3d, clock.SecondsSinceConnect.Value, 1e-9);
        }

        [Test]
        public void ArmTopicAndNonTwist_AreIgnored()
        {
            var clock = ClockAtZero();
            clock.MarkConnected();
            _now = 2d;
            Assert.IsFalse(clock.ObserveMessage("/arm/joint_commands",
                new TwistMsg(new Vector3Msg(1f, 0f, 0f), new Vector3Msg())));
            Assert.IsFalse(clock.ObserveMessage("/cmd_vel/teleop", "not-a-twist"));
            Assert.IsFalse(clock.ObserveMessage("/emergency_stop",
                new TwistMsg(new Vector3Msg(1f, 0f, 0f), new Vector3Msg())));
            Assert.IsFalse(clock.HasFirstDrive);
        }

        [Test]
        public void FormatLogLine_UsesInvariantNumbers()
        {
            var clock = ClockAtZero();
            _now = 1.25d;
            clock.MarkConnected();
            _now = 4d;
            clock.ObserveMessage("/cmd_vel/teleop",
                new TwistMsg(new Vector3Msg(0.2f, -0.1f, 0f), new Vector3Msg(0f, 0f, 0.5f)));
            string line = clock.FormatLogLine();
            StringAssert.StartsWith("[FirstDrive] since_connect_s=2.750 since_launch_s=4.000 topic=/cmd_vel/teleop", line);
            StringAssert.Contains("linear_x=0.200", line);
            StringAssert.Contains("linear_y=-0.100", line);
            StringAssert.Contains("angular_z=0.500", line);
            Assert.IsFalse(line.Contains(","));
        }

        [Test]
        public void FormatReadyLine_UsesFirstConnectTime()
        {
            var clock = ClockAtZero();
            _now = 1.2d;
            clock.MarkConnected();
            _now = 8d;
            clock.MarkConnected();
            Assert.AreEqual("[FirstDrive] ready since_launch_s=1.200", clock.FormatReadyLine());
        }

        [Test]
        public void WriteLog_RoundTripsTheLine()
        {
            string path = Path.Combine(Path.GetTempPath(), "ohho-first-drive-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                const string line = "[FirstDrive] since_connect_s=1.000 since_launch_s=2.000 topic=/cmd_vel/teleop linear_x=0.200 linear_y=0.000 angular_z=0.000";
                FirstDriveMetric.WriteLog(path, line);
                string text = File.ReadAllText(path);
                Assert.AreEqual(line + "\n", text);
                Assert.AreEqual("1.000", Slice(text, "since_connect_s="));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static string Slice(string text, string key)
        {
            int start = text.IndexOf(key, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            start += key.Length;
            int end = text.IndexOf(' ', start);
            return text.Substring(start, end - start);
        }

        [Test]
        public void NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new FirstDriveClock(null));
        }

        [Test]
        public void Culture_DoesNotChangeTheLogDecimal()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var clock = ClockAtZero();
                clock.MarkConnected();
                _now = 1.5d;
                clock.ObserveVelocity("/cmd_vel/teleop", 0.2d, 0d, 0d);
                StringAssert.Contains("since_connect_s=1.500", clock.FormatLogLine());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
