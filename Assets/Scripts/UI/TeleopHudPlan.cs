using System;
using System.Collections.Generic;
using OmniBot.VR.Control;

namespace OmniBot.VR.UI
{
    /// <summary>
    /// Which HUD rows a profile shows. Rows come from descriptor telemetry
    /// blocks when the profile has them, and from base/arm DOF otherwise.
    /// An arm-only robot (no base DOF, no "base" block) gets arm and gripper only.
    /// </summary>
    public static class TeleopHudPlan
    {
        public const string Pose = "pose";
        public const string Velocity = "velocity";
        public const string Arm = "arm";
        public const string Gripper = "gripper";
        public const string Mode = "mode";

        public static string[] Rows(RobotProfile profile)
        {
            if (profile == null) return Array.Empty<string>();

            bool described = profile.TelemetryFields != null && profile.TelemetryFields.Length > 0;
            var rows = new List<string>(5);
            bool baseNamed = !described || Contains(profile.TelemetryFields, "base");
            if (profile.BaseDof > 0 && baseNamed)
            {
                rows.Add(Pose);
                rows.Add(Velocity);
                rows.Add(Mode);
            }

            bool jointsNamed = !described || Contains(profile.TelemetryFields, "joints");
            if (profile.HasArm && profile.ArmDof > 0 && jointsNamed)
                rows.Add(Arm);

            bool gripNamed = !described || Contains(profile.TelemetryFields, "gripper");
            if (gripNamed && HasGripper(profile))
                rows.Add(Gripper);

            return rows.ToArray();
        }

        public static bool Has(string[] rows, string id)
        {
            return rows != null && Array.IndexOf(rows, id) >= 0;
        }

        private static bool Contains(string[] fields, string block)
        {
            if (fields == null) return false;
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Equals(fields[i], block, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasGripper(RobotProfile profile)
        {
            if (profile.Joints == null) return false;
            for (int i = 0; i < profile.Joints.Length; i++)
            {
                var joint = profile.Joints[i];
                if (ContainsGrip(joint.Name) || ContainsGrip(joint.Label))
                    return true;
            }

            return false;
        }

        private static bool ContainsGrip(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
