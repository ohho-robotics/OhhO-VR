using System;
using UnityEngine;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Rigid transform with column vectors: p' = R * p + t.
    /// Tag and robot poses are proper rotations. The ROS/Unity and OpenCV/Unity
    /// axis changes are improper and stay inside <see cref="DetectionFrames"/>.
    /// </summary>
    public readonly struct RigidTransform
    {
        public RigidTransform(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22,
            double x, double y, double z)
        {
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
            X = x; Y = y; Z = z;
        }

        public double M00 { get; }
        public double M01 { get; }
        public double M02 { get; }
        public double M10 { get; }
        public double M11 { get; }
        public double M12 { get; }
        public double M20 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static RigidTransform Identity { get; } = new RigidTransform(
            1, 0, 0,
            0, 1, 0,
            0, 0, 1,
            0, 0, 0);

        public bool IsFinite
        {
            get
            {
                return IsFiniteNumber(M00) && IsFiniteNumber(M01) && IsFiniteNumber(M02)
                    && IsFiniteNumber(M10) && IsFiniteNumber(M11) && IsFiniteNumber(M12)
                    && IsFiniteNumber(M20) && IsFiniteNumber(M21) && IsFiniteNumber(M22)
                    && IsFiniteNumber(X) && IsFiniteNumber(Y) && IsFiniteNumber(Z);
            }
        }

        public static RigidTransform Translation(double x, double y, double z)
        {
            return new RigidTransform(
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                x, y, z);
        }

        /// <summary>
        /// Rotation about Y by <paramref name="radians"/>, then the translation.
        /// Positive angle takes +Z toward +X.
        /// </summary>
        public static RigidTransform RotationY(double radians, double x, double y, double z)
        {
            var c = Math.Cos(radians);
            var s = Math.Sin(radians);
            return new RigidTransform(
                c, 0, s,
                0, 1, 0,
                -s, 0, c,
                x, y, z);
        }

        public static RigidTransform FromRowMajor(
            double a00, double a01, double a02, double a03,
            double a10, double a11, double a12, double a13,
            double a20, double a21, double a22, double a23)
        {
            return new RigidTransform(
                a00, a01, a02,
                a10, a11, a12,
                a20, a21, a22,
                a03, a13, a23);
        }

        public static RigidTransform FromQuaternion(
            double x, double y, double z,
            double qx, double qy, double qz, double qw)
        {
            var n = qx * qx + qy * qy + qz * qz + qw * qw;
            if (n < 1e-16)
            {
                return Translation(x, y, z);
            }

            var inv = 1.0 / Math.Sqrt(n);
            qx *= inv;
            qy *= inv;
            qz *= inv;
            qw *= inv;

            var xx = qx * qx;
            var yy = qy * qy;
            var zz = qz * qz;
            var xy = qx * qy;
            var xz = qx * qz;
            var yz = qy * qz;
            var wx = qw * qx;
            var wy = qw * qy;
            var wz = qw * qz;

            return new RigidTransform(
                1.0 - 2.0 * (yy + zz), 2.0 * (xy - wz), 2.0 * (xz + wy),
                2.0 * (xy + wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz - wx),
                2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - 2.0 * (xx + yy),
                x, y, z);
        }

        public static RigidTransform FromUnityPose(Pose pose)
        {
            return FromQuaternion(
                pose.position.x, pose.position.y, pose.position.z,
                pose.rotation.x, pose.rotation.y, pose.rotation.z, pose.rotation.w);
        }

        public RigidTransform Translated(double dx, double dy, double dz)
        {
            return new RigidTransform(
                M00, M01, M02,
                M10, M11, M12,
                M20, M21, M22,
                X + dx, Y + dy, Z + dz);
        }

        public RigidTransform Mul(RigidTransform child)
        {
            return new RigidTransform(
                M00 * child.M00 + M01 * child.M10 + M02 * child.M20,
                M00 * child.M01 + M01 * child.M11 + M02 * child.M21,
                M00 * child.M02 + M01 * child.M12 + M02 * child.M22,
                M10 * child.M00 + M11 * child.M10 + M12 * child.M20,
                M10 * child.M01 + M11 * child.M11 + M12 * child.M21,
                M10 * child.M02 + M11 * child.M12 + M12 * child.M22,
                M20 * child.M00 + M21 * child.M10 + M22 * child.M20,
                M20 * child.M01 + M21 * child.M11 + M22 * child.M21,
                M20 * child.M02 + M21 * child.M12 + M22 * child.M22,
                M00 * child.X + M01 * child.Y + M02 * child.Z + X,
                M10 * child.X + M11 * child.Y + M12 * child.Z + Y,
                M20 * child.X + M21 * child.Y + M22 * child.Z + Z);
        }

        public RigidTransform Inverse()
        {
            var x = -(M00 * X + M10 * Y + M20 * Z);
            var y = -(M01 * X + M11 * Y + M21 * Z);
            var z = -(M02 * X + M12 * Y + M22 * Z);
            return new RigidTransform(
                M00, M10, M20,
                M01, M11, M21,
                M02, M12, M22,
                x, y, z);
        }

        public void TransformPoint(double px, double py, double pz, out double x, out double y, out double z)
        {
            x = M00 * px + M01 * py + M02 * pz + X;
            y = M10 * px + M11 * py + M12 * pz + Y;
            z = M20 * px + M21 * py + M22 * pz + Z;
        }

        public void TransformDirection(double px, double py, double pz, out double x, out double y, out double z)
        {
            x = M00 * px + M01 * py + M02 * pz;
            y = M10 * px + M11 * py + M12 * pz;
            z = M20 * px + M21 * py + M22 * pz;
        }

        public bool TryGetQuaternion(out double qx, out double qy, out double qz, out double qw)
        {
            double x;
            double y;
            double z;
            double w;
            var trace = M00 + M11 + M22;
            if (trace > 0.0)
            {
                var s = Math.Sqrt(trace + 1.0) * 2.0;
                w = 0.25 * s;
                x = (M21 - M12) / s;
                y = (M02 - M20) / s;
                z = (M10 - M01) / s;
            }
            else if (M00 > M11 && M00 > M22)
            {
                var s = Math.Sqrt(1.0 + M00 - M11 - M22) * 2.0;
                w = (M21 - M12) / s;
                x = 0.25 * s;
                y = (M01 + M10) / s;
                z = (M02 + M20) / s;
            }
            else if (M11 > M22)
            {
                var s = Math.Sqrt(1.0 + M11 - M00 - M22) * 2.0;
                w = (M02 - M20) / s;
                x = (M01 + M10) / s;
                y = 0.25 * s;
                z = (M12 + M21) / s;
            }
            else
            {
                var s = Math.Sqrt(1.0 + M22 - M00 - M11) * 2.0;
                w = (M10 - M01) / s;
                x = (M02 + M20) / s;
                y = (M12 + M21) / s;
                z = 0.25 * s;
            }

            var n = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (n < 1e-8 || double.IsNaN(n) || double.IsInfinity(n))
            {
                qx = 0;
                qy = 0;
                qz = 0;
                qw = 1;
                return false;
            }

            qx = x / n;
            qy = y / n;
            qz = z / n;
            qw = w / n;
            return true;
        }

        public Pose ToUnityPose()
        {
            if (!TryGetQuaternion(out var qx, out var qy, out var qz, out var qw))
            {
                qw = 1;
            }

            return new Pose(
                new Vector3((float)X, (float)Y, (float)Z),
                new Quaternion((float)qx, (float)qy, (float)qz, (float)qw));
        }

        public static double PositionDistance(RigidTransform a, RigidTransform b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static double AngleRadians(RigidTransform a, RigidTransform b)
        {
            var relative = a.Inverse().Mul(b);
            var cos = (relative.M00 + relative.M11 + relative.M22 - 1.0) * 0.5;
            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;
            return Math.Acos(cos);
        }

        public static RigidTransform Interpolate(RigidTransform a, RigidTransform b, double t)
        {
            if (t <= 0.0) return a;
            if (t >= 1.0) return b;
            var x = a.X + (b.X - a.X) * t;
            var y = a.Y + (b.Y - a.Y) * t;
            var z = a.Z + (b.Z - a.Z) * t;
            if (!a.TryGetQuaternion(out var ax, out var ay, out var az, out var aw)
                || !b.TryGetQuaternion(out var bx, out var by, out var bz, out var bw))
            {
                return b.Translated(x - b.X, y - b.Y, z - b.Z);
            }

            Slerp(ax, ay, az, aw, bx, by, bz, bw, t, out var qx, out var qy, out var qz, out var qw);
            return FromQuaternion(x, y, z, qx, qy, qz, qw);
        }

        public bool IsApproximatelyIdentity(double positionMeters, double radians)
        {
            var position = Math.Sqrt(X * X + Y * Y + Z * Z);
            if (position > positionMeters)
            {
                return false;
            }

            return AngleRadians(Identity, this) <= radians;
        }

        /// <summary>
        /// Rebuilds the rotation as an orthonormal proper rotation. The OHH-223
        /// matrices are printed with truncated coefficients.
        /// </summary>
        public RigidTransform Orthonormalized()
        {
            var x0 = M00;
            var y0 = M10;
            var z0 = M20;
            Normalize(ref x0, ref y0, ref z0);

            var x1 = M01 - (x0 * M01 + y0 * M11 + z0 * M21) * x0;
            var y1 = M11 - (x0 * M01 + y0 * M11 + z0 * M21) * y0;
            var z1 = M21 - (x0 * M01 + y0 * M11 + z0 * M21) * z0;
            Normalize(ref x1, ref y1, ref z1);

            var x2 = y0 * z1 - z0 * y1;
            var y2 = z0 * x1 - x0 * z1;
            var z2 = x0 * y1 - y0 * x1;
            Normalize(ref x2, ref y2, ref z2);

            return new RigidTransform(
                x0, x1, x2,
                y0, y1, y2,
                z0, z1, z2,
                X, Y, Z);
        }

        private static void Normalize(ref double x, ref double y, ref double z)
        {
            var n = Math.Sqrt(x * x + y * y + z * z);
            if (n < 1e-12)
            {
                return;
            }

            x /= n;
            y /= n;
            z /= n;
        }

        private static void Slerp(
            double ax, double ay, double az, double aw,
            double bx, double by, double bz, double bw,
            double t,
            out double qx, out double qy, out double qz, out double qw)
        {
            var dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0.0)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
                dot = -dot;
            }

            if (dot > 0.9995)
            {
                qx = ax + t * (bx - ax);
                qy = ay + t * (by - ay);
                qz = az + t * (bz - az);
                qw = aw + t * (bw - aw);
                var n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                if (n < 1e-12)
                {
                    qx = 0;
                    qy = 0;
                    qz = 0;
                    qw = 1;
                    return;
                }

                qx /= n;
                qy /= n;
                qz /= n;
                qw /= n;
                return;
            }

            var theta = Math.Acos(dot > 1.0 ? 1.0 : dot);
            var sin = Math.Sin(theta);
            var w1 = Math.Sin((1.0 - t) * theta) / sin;
            var w2 = Math.Sin(t * theta) / sin;
            qx = w1 * ax + w2 * bx;
            qy = w1 * ay + w2 * by;
            qz = w1 * az + w2 * bz;
            qw = w1 * aw + w2 * bw;
        }

        private static bool IsFiniteNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>
    /// Frame changes shared by the tag calibration and the live detector.
    /// ROS base_link is x forward, y left, z up. Unity is x right, y up, z forward.
    /// OpenCV camera is x right, y down, z forward. Unity camera is x right, y up, z forward.
    /// </summary>
    public static class DetectionFrames
    {
        // p_unity = C * p_ros. det(C) = -1.
        private static readonly RigidTransform RosPointToUnity = new RigidTransform(
            0, -1, 0,
            0, 0, 1,
            1, 0, 0,
            0, 0, 0);

        // OpenCV camera point (x, y, z) -> Unity camera point (x, -y, z).
        private static readonly RigidTransform OpenCvToUnityCamera = new RigidTransform(
            1, 0, 0,
            0, -1, 0,
            0, 0, 1,
            0, 0, 0);

        /// <summary>
        /// OHH-223 <c>T_tag_base</c> (tag frame to ROS base_link) as a Unity pose.
        /// The tag frame's Y is flipped with the camera Y so the result is a proper rotation.
        /// </summary>
        public static RigidTransform TagToBaseUnity(RigidTransform tagToBaseRos)
        {
            return RosPointToUnity.Mul(tagToBaseRos).Mul(OpenCvToUnityCamera);
        }

        /// <summary>
        /// nav_msgs/Odometry pose, expressed as Unity base_link in a Unity odom frame.
        /// Only deltas of this pose are applied to the tracked robot.
        /// </summary>
        public static RigidTransform OdomPoseToUnity(RigidTransform odomRos)
        {
            return RosPointToUnity.Mul(odomRos).Mul(RosPointToUnity.Inverse());
        }

        public static RigidTransform OpenCvCameraToUnityCamera(RigidTransform tagInOpenCvCamera)
        {
            return OpenCvToUnityCamera.Mul(tagInOpenCvCamera).Mul(OpenCvToUnityCamera);
        }

        public static RigidTransform UnityCameraToOpenCv(RigidTransform tagInUnityCamera)
        {
            return OpenCvToUnityCamera.Mul(tagInUnityCamera).Mul(OpenCvToUnityCamera);
        }
    }
}
