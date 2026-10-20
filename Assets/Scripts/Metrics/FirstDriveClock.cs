using System;
using System.Globalization;
using OmniBot.VR.Core;

namespace OmniBot.VR.Metrics
{
    /// <summary>
    /// Time from the first successful ROSBridge connect until the first non-zero
    /// base command the headset actually publishes. Zero twists (the idle stop
    /// publishes) do not count. Arm, e-stop, and other topics do not count.
    ///
    /// The clock does not publish, clamp, or clear anything. Callers pass in the
    /// messages they already sent.
    /// </summary>
    public sealed class FirstDriveClock
    {
        public const double MotionEpsilon = 1e-4;
        public const string TeleopCmdVelTopic = "/cmd_vel/teleop";
        public const string LogPrefix = "[FirstDrive]";

        private readonly Func<double> _secondsSinceLaunch;
        private readonly double _launchSeconds;
        private double? _connectSeconds;
        private double? _driveSeconds;
        private string _topic;
        private double _linearX;
        private double _linearY;
        private double _angularZ;

        public FirstDriveClock(Func<double> secondsSinceLaunch)
        {
            if (secondsSinceLaunch == null) throw new ArgumentNullException(nameof(secondsSinceLaunch));
            _secondsSinceLaunch = secondsSinceLaunch;
            _launchSeconds = secondsSinceLaunch();
        }

        public bool HasConnected => _connectSeconds.HasValue;
        public bool HasFirstDrive => _driveSeconds.HasValue;
        public string Topic => _topic;

        public double? SecondsSinceConnect
        {
            get
            {
                if (!_driveSeconds.HasValue || !_connectSeconds.HasValue) return null;
                return _driveSeconds.Value - _connectSeconds.Value;
            }
        }

        public double? SecondsSinceLaunch
        {
            get
            {
                if (!_driveSeconds.HasValue) return null;
                return _driveSeconds.Value - _launchSeconds;
            }
        }

        /// <summary>Records the first time the link is up. Later calls do nothing.</summary>
        public void MarkConnected()
        {
            if (_connectSeconds.HasValue) return;
            _connectSeconds = _secondsSinceLaunch();
        }

        public static bool IsDriveTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            return topic == TeleopCmdVelTopic || topic.EndsWith("/cmd_vel", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true only on the call that records the first drive.
        /// Ignores messages until <see cref="MarkConnected"/>, ignores non-twists,
        /// and ignores twists whose x, y, and yaw are all within
        /// <see cref="MotionEpsilon"/> of zero.
        /// </summary>
        public bool ObserveMessage(string topic, object message)
        {
            if (!(message is TwistMsg twist)) return false;
            double lx = twist.linear != null ? twist.linear.x : 0d;
            double ly = twist.linear != null ? twist.linear.y : 0d;
            double az = twist.angular != null ? twist.angular.z : 0d;
            return ObserveVelocity(topic, lx, ly, az);
        }

        public bool ObserveVelocity(string topic, double linearX, double linearY, double angularZ)
        {
            if (HasFirstDrive) return false;
            if (!_connectSeconds.HasValue) return false;
            if (!IsDriveTopic(topic)) return false;
            if (Math.Abs(linearX) <= MotionEpsilon &&
                Math.Abs(linearY) <= MotionEpsilon &&
                Math.Abs(angularZ) <= MotionEpsilon)
            {
                return false;
            }

            _driveSeconds = _secondsSinceLaunch();
            _topic = topic;
            _linearX = linearX;
            _linearY = linearY;
            _angularZ = angularZ;
            return true;
        }

        public string FormatReadyLine()
        {
            if (!_connectSeconds.HasValue) return null;
            double sinceLaunch = _connectSeconds.Value - _launchSeconds;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} ready since_launch_s={1:F3}", LogPrefix, sinceLaunch);
        }

        public string FormatLogLine()
        {
            if (!HasFirstDrive) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} since_connect_s={1:F3} since_launch_s={2:F3} topic={3} linear_x={4:F3} linear_y={5:F3} angular_z={6:F3}",
                LogPrefix,
                SecondsSinceConnect ?? 0d,
                SecondsSinceLaunch ?? 0d,
                _topic,
                _linearX,
                _linearY,
                _angularZ);
        }
    }
}
