using System;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Unity pose (metres, left-handed Y-up). Quaternion is x, y, z, w.
    /// Translation is unscaled; the view multiplies by <see cref="TwinScale.Factor"/>.
    /// </summary>
    public readonly struct TwinPose
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float Qx;
        public readonly float Qy;
        public readonly float Qz;
        public readonly float Qw;

        public TwinPose(float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            X = x;
            Y = y;
            Z = z;
            Qx = qx;
            Qy = qy;
            Qz = qz;
            Qw = qw;
        }

        public static TwinPose Identity => new TwinPose(0f, 0f, 0f, 0f, 0f, 0f, 1f);

        public TwinPose Scaled(float factor)
        {
            return new TwinPose(X * factor, Y * factor, Z * factor, Qx, Qy, Qz, Qw);
        }
    }

    /// <summary>Right-handed ROS pose (metres, X forward, Y left, Z up).</summary>
    public readonly struct RosTransform
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float Qx;
        public readonly float Qy;
        public readonly float Qz;
        public readonly float Qw;

        public RosTransform(float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            X = x;
            Y = y;
            Z = z;
            Qx = qx;
            Qy = qy;
            Qz = qz;
            Qw = qw;
        }

        public static RosTransform Identity => new RosTransform(0f, 0f, 0f, 0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// ROS FLU to Unity. Position maps (x, y, z) to (-y, z, x). The quaternion
    /// matches the ros-sharp / Unity Robotics conversion so a positive ROS yaw
    /// (left turn) stays a left turn in Unity.
    /// </summary>
    public static class TwinMath
    {
        public static RosTransform FromRpy(float x, float y, float z, float roll, float pitch, float yaw)
        {
            double cr = Math.Cos(roll * 0.5);
            double sr = Math.Sin(roll * 0.5);
            double cp = Math.Cos(pitch * 0.5);
            double sp = Math.Sin(pitch * 0.5);
            double cy = Math.Cos(yaw * 0.5);
            double sy = Math.Sin(yaw * 0.5);
            return new RosTransform(
                x, y, z,
                (float)(sr * cp * cy - cr * sp * sy),
                (float)(cr * sp * cy + sr * cp * sy),
                (float)(cr * cp * sy - sr * sp * cy),
                (float)(cr * cp * cy + sr * sp * sy));
        }

        public static RosTransform FromAxisAngle(float axisX, float axisY, float axisZ, float angleRadians)
        {
            double length = Math.Sqrt(axisX * axisX + axisY * axisY + axisZ * axisZ);
            if (length < 1e-12 || Math.Abs(angleRadians) < 1e-12)
                return RosTransform.Identity;
            double scale = Math.Sin(angleRadians * 0.5) / length;
            return new RosTransform(
                0f, 0f, 0f,
                (float)(axisX * scale),
                (float)(axisY * scale),
                (float)(axisZ * scale),
                (float)Math.Cos(angleRadians * 0.5));
        }

        public static RosTransform Translation(float x, float y, float z)
        {
            return new RosTransform(x, y, z, 0f, 0f, 0f, 1f);
        }

        public static RosTransform Compose(RosTransform parent, RosTransform child)
        {
            Rotate(parent, child.X, child.Y, child.Z, out float rx, out float ry, out float rz);
            Multiply(parent, child, out float qx, out float qy, out float qz, out float qw);
            return new RosTransform(parent.X + rx, parent.Y + ry, parent.Z + rz, qx, qy, qz, qw);
        }

        public static TwinPose ToUnity(RosTransform ros)
        {
            Normalize(ros.Qy, -ros.Qz, -ros.Qx, ros.Qw, out float qx, out float qy, out float qz, out float qw);
            return new TwinPose(-ros.Y, ros.Z, ros.X, qx, qy, qz, qw);
        }

        public static void Rotate(RosTransform frame, float vx, float vy, float vz, out float ox, out float oy, out float oz)
        {
            float qx = frame.Qx;
            float qy = frame.Qy;
            float qz = frame.Qz;
            float qw = frame.Qw;
            float cx = qy * vz - qz * vy;
            float cy = qz * vx - qx * vz;
            float cz = qx * vy - qy * vx;
            float tx = cx + qw * vx;
            float ty = cy + qw * vy;
            float tz = cz + qw * vz;
            ox = vx + 2f * (qy * tz - qz * ty);
            oy = vy + 2f * (qz * tx - qx * tz);
            oz = vz + 2f * (qx * ty - qy * tx);
        }

        public static void Multiply(RosTransform a, RosTransform b, out float qx, out float qy, out float qz, out float qw)
        {
            qw = a.Qw * b.Qw - a.Qx * b.Qx - a.Qy * b.Qy - a.Qz * b.Qz;
            qx = a.Qw * b.Qx + a.Qx * b.Qw + a.Qy * b.Qz - a.Qz * b.Qy;
            qy = a.Qw * b.Qy - a.Qx * b.Qz + a.Qy * b.Qw + a.Qz * b.Qx;
            qz = a.Qw * b.Qz + a.Qx * b.Qy - a.Qy * b.Qx + a.Qz * b.Qw;
        }

        public static void Normalize(float qx, float qy, float qz, float qw, out float ox, out float oy, out float oz, out float ow)
        {
            double length = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (length < 1e-12)
            {
                ox = 0f;
                oy = 0f;
                oz = 0f;
                ow = 1f;
                return;
            }
            float inv = (float)(1.0 / length);
            ox = qx * inv;
            oy = qy * inv;
            oz = qz * inv;
            ow = qw * inv;
        }
    }
}
