using System;
using System.Collections.Generic;
using OmniBot.VR.AprilTagSpike;
using UnityEngine;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Rates and acceptance numbers from OHH-224. The Quest video check is a human TODO.
    /// </summary>
    public static class RobotDetectionLimits
    {
        public const float MinRateHz = 10f;
        public const float MaxRateHz = 15f;
        public const float DefaultRateHz = 12f;

        public const double LockWithinSeconds = 2.0;
        public const double OverlayErrorMeters = 0.03;
        public const double ReferenceDriveSpeedMetersPerSecond = 0.2;

        public const double OutlierMeters = 0.05;
        public const double OutlierRadians = 20.0 * Math.PI / 180.0;
        public const double TemporalOutlierMeters = 0.15;
        public const double LockAgreementMeters = 0.08;
        public const double LockAgreementRadians = 15.0 * Math.PI / 180.0;
        public const double LostTimeoutSeconds = 1.0;

        public static int PeriodMilliseconds(float rateHz)
        {
            var hz = rateHz;
            if (hz < MinRateHz) hz = MinRateHz;
            if (hz > MaxRateHz) hz = MaxRateHz;
            return (int)Math.Round(1000.0 / hz);
        }
    }

    public readonly struct RobotDetectionSnapshot
    {
        public RobotDetectionSnapshot(
            RobotTrackState state,
            bool hasPose,
            double x, double y, double z,
            double qx, double qy, double qz, double qw,
            float confidence,
            int inlierCount,
            double timestampSeconds,
            double secondsToLock)
        {
            State = state;
            HasPose = hasPose;
            X = x;
            Y = y;
            Z = z;
            Qx = qx;
            Qy = qy;
            Qz = qz;
            Qw = qw;
            Confidence = confidence;
            InlierCount = inlierCount;
            TimestampSeconds = timestampSeconds;
            SecondsToLock = secondsToLock;
        }

        public RobotTrackState State { get; }
        public bool HasPose { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Qx { get; }
        public double Qy { get; }
        public double Qz { get; }
        public double Qw { get; }
        public float Confidence { get; }
        public int InlierCount { get; }
        public double TimestampSeconds { get; }
        public double SecondsToLock { get; }

        public static RobotDetectionSnapshot Inactive(double timestampSeconds)
        {
            return new RobotDetectionSnapshot(
                RobotTrackState.Searching,
                false,
                0, 0, 0,
                0, 0, 0, 1,
                0f,
                0,
                timestampSeconds,
                double.NaN);
        }

        public Pose Pose
        {
            get
            {
                return new Pose(
                    new Vector3((float)X, (float)Y, (float)Z),
                    new Quaternion((float)Qx, (float)Qy, (float)Qz, (float)Qw));
            }
        }

        public double PositionErrorMeters(double x, double y, double z)
        {
            if (!HasPose)
            {
                return double.PositiveInfinity;
            }

            var dx = X - x;
            var dy = Y - y;
            var dz = Z - z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public static class RobotDetectionConfidence
    {
        public static float Compute(int inliers, double residualMeters, double ageSeconds)
        {
            var coverage = inliers / 3.0;
            if (coverage < 0.0) coverage = 0.0;
            if (coverage > 1.0) coverage = 1.0;
            var fit = 1.0 - residualMeters / RobotDetectionLimits.OutlierMeters;
            if (fit < 0.0) fit = 0.0;
            if (fit > 1.0) fit = 1.0;
            var fresh = 1.0 - ageSeconds / RobotDetectionLimits.LostTimeoutSeconds;
            if (fresh < 0.0) fresh = 0.0;
            if (fresh > 1.0) fresh = 1.0;
            return (float)(coverage * 0.5 + fit * 0.3 + fresh * 0.2);
        }
    }

    /// <summary>
    /// Tag pose to filtered robot pose. Odom deltas carry the pose between
    /// detections. The camera world pose is the headset pose captured with the frame.
    /// </summary>
    public sealed class RobotDetectionPipeline
    {
        private readonly OmniBotTagCalibration _calibration;
        private readonly RobotPoseKalmanFilter _filter = new RobotPoseKalmanFilter();
        private readonly RobotTrackStateMachine _track = new RobotTrackStateMachine();
        private RigidTransform _lastOdom;
        private bool _hasOdom;
        private bool _hasTime;
        private double _lastTime;
        private bool _hasSeen;
        private double _firstSeen;
        private double _secondsToLock = double.NaN;
        private double _lastGoodTime;
        private int _lastInliers;
        private double _lastResidual;

        public RobotDetectionPipeline()
            : this(OmniBotTagCalibration.Nominal)
        {
        }

        public RobotDetectionPipeline(OmniBotTagCalibration calibration)
        {
            _calibration = calibration ?? OmniBotTagCalibration.Nominal;
        }

        public OmniBotTagCalibration Calibration => _calibration;

        public RobotDetectionSnapshot Step(
            double timeSeconds,
            RigidTransform cameraWorld,
            IReadOnlyList<AprilTagDetection> detections,
            bool hasOdom,
            RigidTransform odomBaseInOdomUnity)
        {
            var dt = 0.0;
            if (_hasTime)
            {
                dt = timeSeconds - _lastTime;
                if (dt < 0.0) dt = 0.0;
            }

            _hasTime = true;
            _lastTime = timeSeconds;

            if (hasOdom && odomBaseInOdomUnity.IsFinite)
            {
                if (_hasOdom)
                {
                    var delta = _lastOdom.Inverse().Mul(odomBaseInOdomUnity);
                    if (_track.State == RobotTrackState.Locked)
                    {
                        _filter.Predict(delta, dt);
                    }

                    _track.Propagate(delta);
                }

                _lastOdom = odomBaseInOdomUnity;
                _hasOdom = true;
            }
            else if (_track.State == RobotTrackState.Locked)
            {
                _filter.Predict(RigidTransform.Identity, dt);
            }

            var fused = RobotPoseFusion.TryFuse(
                cameraWorld,
                detections,
                _calibration,
                out var fusedBase,
                out var inliers,
                out var residual);

            if (fused && !_hasSeen)
            {
                _hasSeen = true;
                _firstSeen = timeSeconds;
            }

            var accepted = fused;
            if (accepted
                && _track.State == RobotTrackState.Locked
                && _filter.HasPose
                && RigidTransform.PositionDistance(_filter.Pose, fusedBase) > RobotDetectionLimits.TemporalOutlierMeters)
            {
                accepted = false;
            }

            if (accepted)
            {
                var publish = _track.NoteMeasurement(timeSeconds, fusedBase, inliers);
                if (publish)
                {
                    if (_track.RelockedThisStep || _track.LockedThisStep || !_filter.HasPose)
                    {
                        _filter.Reset(fusedBase);
                    }
                    else
                    {
                        _filter.Update(fusedBase);
                    }

                    _lastInliers = inliers;
                    _lastResidual = residual;
                    _lastGoodTime = timeSeconds;
                    if (_track.LockedThisStep && _hasSeen)
                    {
                        _secondsToLock = timeSeconds - _firstSeen;
                    }
                }
            }
            else
            {
                _track.NoteMiss(timeSeconds);
            }

            return BuildSnapshot(timeSeconds);
        }

        /// <summary>
        /// Moves the tracked pose when the session-origin anchor jumps in tracking space.
        /// </summary>
        public void ApplyWorldCorrection(RigidTransform previousAnchorToCurrent)
        {
            if (!previousAnchorToCurrent.IsFinite)
            {
                return;
            }

            if (_filter.HasPose)
            {
                _filter.SetPose(previousAnchorToCurrent.Mul(_filter.Pose));
            }

            _track.ApplyWorldCorrection(previousAnchorToCurrent);
        }

        public RobotDetectionSnapshot Capture(double timeSeconds)
        {
            return BuildSnapshot(timeSeconds);
        }

        private RobotDetectionSnapshot BuildSnapshot(double timeSeconds)
        {
            var hasPose = _track.State == RobotTrackState.Locked && _filter.HasPose;
            var confidence = 0f;
            if (hasPose)
            {
                var age = timeSeconds - _lastGoodTime;
                if (age < 0.0) age = 0.0;
                confidence = RobotDetectionConfidence.Compute(_lastInliers, _lastResidual, age);
            }

            if (!hasPose || !_filter.Pose.TryGetQuaternion(out var qx, out var qy, out var qz, out var qw))
            {
                qx = 0;
                qy = 0;
                qz = 0;
                qw = 1;
            }

            return new RobotDetectionSnapshot(
                _track.State,
                hasPose,
                hasPose ? _filter.Pose.X : 0,
                hasPose ? _filter.Pose.Y : 0,
                hasPose ? _filter.Pose.Z : 0,
                qx, qy, qz, qw,
                confidence,
                hasPose ? _lastInliers : 0,
                timeSeconds,
                _secondsToLock);
        }
    }
}
