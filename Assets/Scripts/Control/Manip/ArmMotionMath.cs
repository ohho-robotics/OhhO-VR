using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control.Manip
{
    /// <summary>
    /// Steps <paramref name="current"/> toward <paramref name="target"/> by at
    /// most the safety cap. The cap is clamped down to
    /// <see cref="RobotDescriptor.MaxJointDeltaRad"/> and is never raised.
    /// </summary>
    public static class JointRateLimiter
    {
        public static void StepTowards(float[] current, float[] target, float maxDelta, float[] jointMin, float[] jointMax)
        {
            if (current == null || target == null) return;
            float cap = RobotDescriptor.ClampJointDelta(maxDelta);
            int n = current.Length;
            if (target.Length < n) n = target.Length;
            for (int i = 0; i < n; i++)
            {
                float goal = target[i];
                if (float.IsNaN(goal) || float.IsInfinity(goal)) goal = current[i];
                if (jointMin != null && jointMax != null && i < jointMin.Length && i < jointMax.Length)
                    goal = Mathf.Clamp(goal, jointMin[i], jointMax[i]);
                float delta = Mathf.Clamp(goal - current[i], -cap, cap);
                current[i] = current[i] + delta;
            }
        }
    }

    /// <summary>Slew a Cartesian hand target. Does not replace the joint cap.</summary>
    public sealed class CartesianSmoother
    {
        public Vector3 Value;
        public bool Primed;
        public float MaxSpeedMps = RobotDescriptor.MaxCartesianSpeedMps;

        public void Snap(Vector3 value)
        {
            Value = value;
            Primed = true;
        }

        public Vector3 MoveTowards(Vector3 target, float dt)
        {
            if (!Primed || dt <= 0f)
            {
                Snap(target);
                return Value;
            }
            float step = Mathf.Max(0f, MaxSpeedMps) * dt;
            Value = Vector3.MoveTowards(Value, target, step);
            return Value;
        }
    }

    public static class JointJog
    {
        public const float StickDeadzone = 0.15f;

        public static float Axis(float stick)
        {
            if (Mathf.Abs(stick) < StickDeadzone) return 0f;
            return Mathf.Clamp(stick, -1f, 1f);
        }

        /// <summary>
        /// Integrate one joint. The returned delta is already inside
        /// <see cref="RobotDescriptor.MaxJointDeltaRad"/>.
        /// </summary>
        public static float DeltaRad(float stick, float dt)
        {
            float raw = Axis(stick) * RobotDescriptor.JogRadPerSec * Mathf.Max(0f, dt);
            return Mathf.Clamp(raw, -RobotDescriptor.MaxJointDeltaRad, RobotDescriptor.MaxJointDeltaRad);
        }
    }

    /// <summary>Unit circle samples for the workspace sphere rings.</summary>
    public static class WorkspaceSphereRings
    {
        public static Vector3[] Ring(float radius, Vector3 axisU, Vector3 axisV, int segments)
        {
            if (segments < 3) segments = 3;
            var pts = new Vector3[segments + 1];
            Fill(radius, axisU, axisV, pts);
            return pts;
        }

        public static void Fill(float radius, Vector3 axisU, Vector3 axisV, Vector3[] dest)
        {
            if (dest == null || dest.Length < 2) return;
            int segments = dest.Length - 1;
            for (int i = 0; i <= segments; i++)
            {
                float a = (Mathf.PI * 2f) * i / segments;
                dest[i] = (axisU * Mathf.Cos(a) + axisV * Mathf.Sin(a)) * radius;
            }
        }
    }
}
