using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// One age from the HUD <c>age_ms</c> sidecar. <see cref="Present"/> is false
    /// when the aggregator omitted the key. Missing is not the same as zero.
    /// </summary>
    public struct FieldAge
    {
        public bool Present;
        public double Ms;
    }

    /// <summary>
    /// Grey-out hints copied from the aggregator <c>SUGGESTED_GREY_MS</c>.
    /// They dim the panel. They are not watchdogs and they do not stop the robot.
    /// </summary>
    public sealed class FieldAges
    {
        public const int MaxJoints = 16;

        public const double GreyBaseMs = 250;
        public const double GreyJointsMs = 250;
        public const double GreyImuMs = 250;
        public const double GreyGripperMs = 250;
        public const double GreyBatteryMs = 5000;
        public const double GreyNavMs = 1000;
        public const double GreyPolicyMs = 1000;
        public const double GreyRecMs = 2000;
        public const double GreyModeMs = 2500;
        public const double GreyEstopMs = 2500;
        public const double GreyDeadmanMs = 2500;
        public const double GreySysMs = 5000;
        public const double GreyLinkMs = 1000;
        public const double GreyVideoMs = 250;
        public const double GreyEchoMs = 150;

        public const double AmberEdge = 0.15;
        public const double RedEdge = 0.05;

        public bool HasAgeObject;
        public FieldAge Base, BaseCmd, BaseMeas, BasePose;
        public FieldAge Joints, Gripper, Imu, Battery, Nav, Policy, Rec, Sys;
        public FieldAge Mode, ModeBase, ModeArm, Estop, Deadman, Link;

        public readonly string[] JointNames = new string[MaxJoints];
        public readonly double[] JointMs = new double[MaxJoints];
        public readonly bool[] JointPresent = new bool[MaxJoints];
        public int JointAgeCount;

        public void Clear()
        {
            HasAgeObject = false;
            Base = BaseCmd = BaseMeas = BasePose = default;
            Joints = Gripper = Imu = Battery = Nav = Policy = Rec = Sys = default;
            Mode = ModeBase = ModeArm = Estop = Deadman = Link = default;
            JointAgeCount = 0;
            for (int i = 0; i < MaxJoints; i++)
                JointPresent[i] = false;
        }

        public void CopyFrom(FieldAges other)
        {
            if (other == null)
            {
                Clear();
                return;
            }
            HasAgeObject = other.HasAgeObject;
            Base = other.Base;
            BaseCmd = other.BaseCmd;
            BaseMeas = other.BaseMeas;
            BasePose = other.BasePose;
            Joints = other.Joints;
            Gripper = other.Gripper;
            Imu = other.Imu;
            Battery = other.Battery;
            Nav = other.Nav;
            Policy = other.Policy;
            Rec = other.Rec;
            Sys = other.Sys;
            Mode = other.Mode;
            ModeBase = other.ModeBase;
            ModeArm = other.ModeArm;
            Estop = other.Estop;
            Deadman = other.Deadman;
            Link = other.Link;
            JointAgeCount = other.JointAgeCount;
            if (JointAgeCount > MaxJoints) JointAgeCount = MaxJoints;
            for (int i = 0; i < JointAgeCount; i++)
            {
                JointNames[i] = other.JointNames[i];
                JointMs[i] = other.JointMs[i];
                JointPresent[i] = other.JointPresent[i];
            }
            for (int i = JointAgeCount; i < MaxJoints; i++)
                JointPresent[i] = false;
        }

        public bool TryJoint(string name, out double ms)
        {
            ms = 0;
            if (string.IsNullOrEmpty(name)) return false;
            int n = JointAgeCount;
            if (n > MaxJoints) n = MaxJoints;
            for (int i = 0; i < n; i++)
            {
                if (!JointPresent[i]) continue;
                if (string.Equals(JointNames[i], name, System.StringComparison.Ordinal))
                {
                    ms = JointMs[i];
                    return true;
                }
            }
            return false;
        }

        public static bool IsStale(FieldAge age, double thresholdMs)
        {
            return age.Present && age.Ms >= thresholdMs;
        }

        public static void Read(JToken token, FieldAges ages)
        {
            ages.Clear();
            if (token == null || token.Type == JTokenType.Null) return;
            JObject obj = token as JObject;
            if (obj == null) return;
            ages.HasAgeObject = true;
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value == null || (prop.Value.Type != JTokenType.Float && prop.Value.Type != JTokenType.Integer))
                    continue;
                double ms = prop.Value.Value<double>();
                string key = prop.Name;
                if (key.Length > 7 && key.StartsWith("joints.", System.StringComparison.Ordinal))
                {
                    if (ages.JointAgeCount >= MaxJoints) continue;
                    int slot = ages.JointAgeCount++;
                    ages.JointNames[slot] = key.Substring(7);
                    ages.JointMs[slot] = ms;
                    ages.JointPresent[slot] = true;
                    continue;
                }
                FieldAge age = new FieldAge { Present = true, Ms = ms };
                switch (key)
                {
                    case "base": ages.Base = age; break;
                    case "base.cmd": ages.BaseCmd = age; break;
                    case "base.meas": ages.BaseMeas = age; break;
                    case "base.pose": ages.BasePose = age; break;
                    case "joints": ages.Joints = age; break;
                    case "gripper": ages.Gripper = age; break;
                    case "imu": ages.Imu = age; break;
                    case "battery": ages.Battery = age; break;
                    case "nav": ages.Nav = age; break;
                    case "policy": ages.Policy = age; break;
                    case "rec": ages.Rec = age; break;
                    case "sys": ages.Sys = age; break;
                    case "mode": ages.Mode = age; break;
                    case "mode.base": ages.ModeBase = age; break;
                    case "mode.arm": ages.ModeArm = age; break;
                    case "estop": ages.Estop = age; break;
                    case "deadman": ages.Deadman = age; break;
                    case "link": ages.Link = age; break;
                }
            }
        }
    }
}
