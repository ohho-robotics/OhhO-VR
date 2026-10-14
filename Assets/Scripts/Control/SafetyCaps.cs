using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Clamps teleop commands to the profile's hard caps. Speed may fall
    /// immediately. It may rise only as fast as the acceleration cap allows,
    /// and never above the linear or yaw ceiling already stored on the profile.
    /// </summary>
    public static class SafetyCaps
    {
        /// <summary>
        /// Limit one axis. <paramref name="maxAccel"/> of 0 keeps the previous
        /// behaviour (clamp to <paramref name="maxAbs"/> only). A positive
        /// acceleration cap slows increases and still allows an immediate stop.
        /// </summary>
        public static float LimitAxis(float previous, float commanded, float maxAbs, float maxAccel, float dt)
        {
            if (maxAbs < 0f) maxAbs = 0f;
            float target = Mathf.Clamp(commanded, -maxAbs, maxAbs);
            if (maxAccel <= 0f || dt <= 0f) return target;

            // Braking and holding are not delayed. Only speeding up is limited.
            if (Mathf.Abs(target) <= Mathf.Abs(previous)) return target;

            float step = maxAccel * Mathf.Clamp(dt, 0f, 0.1f);
            float delta = Mathf.Clamp(target - previous, -step, step);
            return Mathf.Clamp(previous + delta, -maxAbs, maxAbs);
        }

        /// <summary>
        /// Joint command clamp. The result stays inside [min, max] and cannot
        /// move more than <paramref name="maxStep"/> radians from <paramref name="previous"/>.
        /// </summary>
        public static float ClampJoint(float previous, float commanded, float min, float max, float maxStep)
        {
            if (min > max)
            {
                float swap = min;
                min = max;
                max = swap;
            }

            float target = Mathf.Clamp(commanded, min, max);
            if (maxStep > 0f)
                target = Mathf.Clamp(target, previous - maxStep, previous + maxStep);
            return Mathf.Clamp(target, min, max);
        }

        /// <summary>Step limit for a profile. Never above the descriptor ceiling.</summary>
        public static float JointStepLimit(RobotProfile profile)
        {
            const float ceiling = 0.15f;
            if (profile == null || profile.HardMaxJointStep <= 0f) return ceiling;
            return Mathf.Min(profile.HardMaxJointStep, ceiling);
        }

        public static void LimitTwist(
            TwistMsg twist,
            RobotProfile profile,
            ref float prevVx,
            ref float prevVy,
            ref float prevVz,
            ref float prevWz,
            float dt)
        {
            if (twist == null) return;
            if (twist.linear == null) twist.linear = new Vector3Msg();
            if (twist.angular == null) twist.angular = new Vector3Msg();
            if (profile == null)
            {
                twist.linear.x = twist.linear.y = twist.linear.z = 0f;
                twist.angular.x = twist.angular.y = twist.angular.z = 0f;
                prevVx = prevVy = prevVz = prevWz = 0f;
                return;
            }

            float lin = Positive(profile.MaxLinVel);
            float lat = Positive(profile.MaxLatVel);
            float yaw = Positive(profile.MaxAngVel);
            if (profile.HardMaxLinVel > 0f) lin = Mathf.Min(lin, profile.HardMaxLinVel);
            if (profile.HardMaxLatVel > 0f) lat = Mathf.Min(lat, profile.HardMaxLatVel);
            else if (!profile.Holonomic) lat = 0f;
            if (profile.HardMaxAngVel > 0f) yaw = Mathf.Min(yaw, profile.HardMaxAngVel);

            // Descriptor documents cannot exceed the OmniBot ceilings. A catalog
            // robot that is not descriptor-bound keeps the speed the catalog gave it.
            if (profile.CapsFromDescriptor)
            {
                lin = Mathf.Min(lin, RobotConfig.MAX_LINEAR_VEL);
                lat = Mathf.Min(lat, RobotConfig.MAX_LINEAR_VEL);
                yaw = Mathf.Min(yaw, RobotConfig.MAX_ANGULAR_VEL);
            }

            float accelLin = profile.HardMaxAccelLin;
            float accelYaw = profile.HardMaxAccelYaw;
            prevVx = LimitAxis(prevVx, twist.linear.x, lin, accelLin, dt);
            prevVy = LimitAxis(prevVy, twist.linear.y, lat, accelLin, dt);
            prevVz = LimitAxis(prevVz, twist.linear.z, lin, accelLin, dt);
            prevWz = LimitAxis(prevWz, twist.angular.z, yaw, accelYaw, dt);
            twist.linear.x = prevVx;
            twist.linear.y = prevVy;
            twist.linear.z = prevVz;
            twist.angular.x = 0f;
            twist.angular.y = 0f;
            twist.angular.z = prevWz;
        }

        private static float Positive(float value) => value > 0f ? value : 0f;
    }
}
