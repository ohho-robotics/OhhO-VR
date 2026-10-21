namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// Local echo of twist and joint targets the headset published.
    /// Recording a publish does not change the message, the rate, or any cap.
    /// </summary>
    public sealed class CommandEcho
    {
        public const int MaxJoints = 16;

        public static readonly CommandEcho Shared = new CommandEcho();

        readonly RateTracker _twistRate = new RateTracker();
        readonly RateTracker _jointRate = new RateTracker();
        readonly double[] _joints = new double[MaxJoints];

        double _vx, _vy, _wz, _twistTime, _jointTime;
        int _jointCount;
        bool _hasTwist, _hasJoints;

        public bool HasTwist { get { return _hasTwist; } }
        public bool HasJoints { get { return _hasJoints; } }
        public double Vx { get { return _vx; } }
        public double Vy { get { return _vy; } }
        public double Wz { get { return _wz; } }
        public int JointCount { get { return _jointCount; } }

        public double Joint(int index)
        {
            if (index < 0 || index >= _jointCount) return 0;
            return _joints[index];
        }

        public double TwistAgeSeconds(double now)
        {
            if (!_hasTwist) return -1;
            return now - _twistTime;
        }

        public double JointAgeSeconds(double now)
        {
            if (!_hasJoints) return -1;
            return now - _jointTime;
        }

        public double TwistHz(double now) { return _twistRate.Hz(now); }
        public double JointHz(double now) { return _jointRate.Hz(now); }

        public void NoteTwist(double vx, double vy, double wz, double nowSec)
        {
            if (double.IsNaN(vx) || double.IsNaN(vy) || double.IsNaN(wz)) return;
            if (double.IsInfinity(vx) || double.IsInfinity(vy) || double.IsInfinity(wz)) return;
            _vx = vx;
            _vy = vy;
            _wz = wz;
            _hasTwist = true;
            _twistTime = nowSec;
            _twistRate.Mark(nowSec);
        }

        public void NoteJoints(float[] positions, int count, double nowSec)
        {
            if (positions == null || count <= 0 || double.IsNaN(nowSec)) return;
            if (count > positions.Length) count = positions.Length;
            if (count > MaxJoints) count = MaxJoints;
            for (int i = 0; i < count; i++)
            {
                float value = positions[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                _joints[i] = value;
            }
            _jointCount = count;
            _hasJoints = true;
            _jointTime = nowSec;
            _jointRate.Mark(nowSec);
        }
    }
}
