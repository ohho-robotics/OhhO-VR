using System;
using System.Collections.Generic;
using OmniBot.VR.AprilTagSpike;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Turns camera-frame AprilTag hits into one base_link pose.
    /// Tags that disagree with the largest agreeing cluster are dropped.
    /// </summary>
    public static class RobotPoseFusion
    {
        private struct Estimate
        {
            public RigidTransform Base;
            public double Margin;
        }

        public static bool TryFuse(
            RigidTransform cameraWorld,
            IReadOnlyList<AprilTagDetection> detections,
            OmniBotTagCalibration calibration,
            out RigidTransform fusedBase,
            out int inlierCount,
            out double residualMeters)
        {
            fusedBase = RigidTransform.Identity;
            inlierCount = 0;
            residualMeters = 0;
            if (detections == null || calibration == null || !cameraWorld.IsFinite)
            {
                return false;
            }

            var estimates = new List<Estimate>(detections.Count);
            for (var i = 0; i < detections.Count; i++)
            {
                var detection = detections[i];
                if (!detection.CountsTowardHertz)
                {
                    continue;
                }

                if (!string.Equals(detection.Family, OmniBotTagCalibration.Family, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!calibration.TryGet(detection.Id, out var mount))
                {
                    continue;
                }

                if (!TryBaseFromDetection(detection, cameraWorld, mount.TagToBaseUnity, out var worldBase))
                {
                    continue;
                }

                var margin = (double)detection.DecisionMargin;
                if (double.IsNaN(margin) || double.IsInfinity(margin) || margin < 0.0)
                {
                    margin = 0.0;
                }

                estimates.Add(new Estimate
                {
                    Base = worldBase,
                    Margin = margin + 1e-3,
                });
            }

            if (estimates.Count == 0)
            {
                return false;
            }

            var inliers = LargestCluster(estimates);
            if (inliers.Count == 0)
            {
                return false;
            }

            fusedBase = Average(estimates, inliers);
            inlierCount = inliers.Count;
            double residual = 0;
            for (var i = 0; i < inliers.Count; i++)
            {
                residual += RigidTransform.PositionDistance(estimates[inliers[i]].Base, fusedBase);
            }

            residualMeters = residual / inliers.Count;
            return fusedBase.IsFinite;
        }

        private static bool TryBaseFromDetection(
            AprilTagDetection detection,
            RigidTransform cameraWorld,
            RigidTransform tagToBaseUnity,
            out RigidTransform worldBase)
        {
            worldBase = RigidTransform.Identity;
            var position = detection.PositionMeters;
            var rotation = detection.Rotation;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z)
                || !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w))
            {
                return false;
            }

            var openCv = RigidTransform.FromQuaternion(
                position.x, position.y, position.z,
                rotation.x, rotation.y, rotation.z, rotation.w);
            var unityCamera = DetectionFrames.OpenCvCameraToUnityCamera(openCv);
            var worldTag = cameraWorld.Mul(unityCamera);
            worldBase = worldTag.Mul(tagToBaseUnity.Inverse());
            return worldBase.IsFinite;
        }

        private static List<int> LargestCluster(List<Estimate> estimates)
        {
            var best = new List<int>();
            var bestMargin = double.NegativeInfinity;
            var current = new List<int>(estimates.Count);
            for (var i = 0; i < estimates.Count; i++)
            {
                current.Clear();
                double margin = 0;
                for (var j = 0; j < estimates.Count; j++)
                {
                    var position = RigidTransform.PositionDistance(estimates[i].Base, estimates[j].Base);
                    var angle = RigidTransform.AngleRadians(estimates[i].Base, estimates[j].Base);
                    if (position <= RobotDetectionLimits.OutlierMeters
                        && angle <= RobotDetectionLimits.OutlierRadians)
                    {
                        current.Add(j);
                        margin += estimates[j].Margin;
                    }
                }

                if (current.Count > best.Count || (current.Count == best.Count && margin > bestMargin))
                {
                    best = new List<int>(current);
                    bestMargin = margin;
                }
            }

            return best;
        }

        private static RigidTransform Average(List<Estimate> estimates, List<int> inliers)
        {
            double x = 0;
            double y = 0;
            double z = 0;
            double qx = 0;
            double qy = 0;
            double qz = 0;
            double qw = 0;
            double weight = 0;
            var haveQuaternion = false;
            for (var i = 0; i < inliers.Count; i++)
            {
                var estimate = estimates[inliers[i]];
                var w = estimate.Margin;
                x += estimate.Base.X * w;
                y += estimate.Base.Y * w;
                z += estimate.Base.Z * w;
                weight += w;
                if (!estimate.Base.TryGetQuaternion(out var ex, out var ey, out var ez, out var ew))
                {
                    continue;
                }

                if (haveQuaternion && qx * ex + qy * ey + qz * ez + qw * ew < 0.0)
                {
                    ex = -ex;
                    ey = -ey;
                    ez = -ez;
                    ew = -ew;
                }

                qx += ex * w;
                qy += ey * w;
                qz += ez * w;
                qw += ew * w;
                haveQuaternion = true;
            }

            if (weight <= 0.0)
            {
                return estimates[inliers[0]].Base;
            }

            x /= weight;
            y /= weight;
            z /= weight;
            if (!haveQuaternion)
            {
                return estimates[inliers[0]].Base.Translated(x - estimates[inliers[0]].Base.X, y - estimates[inliers[0]].Base.Y, z - estimates[inliers[0]].Base.Z);
            }

            return RigidTransform.FromQuaternion(x, y, z, qx, qy, qz, qw);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
