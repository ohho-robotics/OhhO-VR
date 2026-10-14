using System;
using System.Collections.Generic;
using UnityEngine;

namespace OmniBot.VR.AprilTagSpike
{
    /// <summary>
    /// Pinhole intrinsics in pixels. Filled from MRUK
    /// <c>PassthroughCameraAccess.Intrinsics</c> once that package is installed.
    /// </summary>
    public readonly struct PinholeIntrinsics
    {
        public PinholeIntrinsics(float fx, float fy, float cx, float cy, int width, int height)
        {
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
            Width = width;
            Height = height;
        }

        public float Fx { get; }
        public float Fy { get; }
        public float Cx { get; }
        public float Cy { get; }
        public int Width { get; }
        public int Height { get; }

        public bool IsValid => Width > 0 && Height > 0 && Fx > 0f && Fy > 0f;
    }

    /// <summary>
    /// One camera sample. Pixel bytes stay in the camera API until a native
    /// detector exists; this value is the pose and intrinsics the detector needs.
    /// </summary>
    public readonly struct PassthroughFrame
    {
        public PassthroughFrame(
            int width,
            int height,
            PinholeIntrinsics intrinsics,
            Pose cameraPose,
            double timestampSeconds)
        {
            Width = width;
            Height = height;
            Intrinsics = intrinsics;
            CameraPose = cameraPose;
            TimestampSeconds = timestampSeconds;
        }

        public int Width { get; }
        public int Height { get; }
        public PinholeIntrinsics Intrinsics { get; }
        public Pose CameraPose { get; }
        public double TimestampSeconds { get; }

        public bool HasMatchingResolution =>
            Intrinsics.Width == Width && Intrinsics.Height == Height;
    }

    public readonly struct AprilTagDetection
    {
        public AprilTagDetection(
            int id,
            string family,
            Vector3 positionMeters,
            Quaternion rotation,
            int hamming,
            float decisionMargin)
        {
            Id = id;
            Family = family ?? "";
            PositionMeters = positionMeters;
            Rotation = rotation;
            Hamming = hamming;
            DecisionMargin = decisionMargin;
        }

        public int Id { get; }
        public string Family { get; }
        public Vector3 PositionMeters { get; }
        public Quaternion Rotation { get; }
        public int Hamming { get; }
        public float DecisionMargin { get; }

        public Pose Pose => new Pose(PositionMeters, Rotation);

        /// <summary>Hz counts only Hamming-0 hits. Decision margin is recorded, not gated.</summary>
        public bool CountsTowardHertz => Hamming == 0;
    }

    public interface IAprilTagDetector
    {
        string Name { get; }
        bool IsReady { get; }
        IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame);
    }

    /// <summary>
    /// Stand-in until <c>libapriltag.so</c> (BSD-2-Clause, tag36h11) is added.
    /// </summary>
    public sealed class StubAprilTagDetector : IAprilTagDetector
    {
        public string Name => AprilTagSpikeConfig.StubDetectorName;
        public bool IsReady => false;

        public IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
        {
            return Array.Empty<AprilTagDetection>();
        }
    }

    /// <summary>
    /// Operator-placed tag pose. This is the ground truth for pose error and
    /// the fallback robot pose while the detector gate is unmet.
    /// </summary>
    public sealed class ManualPlacementBaseline : IAprilTagDetector
    {
        private readonly AprilTagDetection[] _one = new AprilTagDetection[1];
        private bool _hasPlacement;

        public ManualPlacementBaseline()
        {
        }

        public ManualPlacementBaseline(AprilTagDetection placed)
        {
            SetPlaced(placed);
        }

        public string Name => AprilTagSpikeConfig.ManualBaselineName;
        public bool IsReady => _hasPlacement;
        public bool HasPlacement => _hasPlacement;

        public void SetPlaced(AprilTagDetection placed)
        {
            _one[0] = placed;
            _hasPlacement = true;
        }

        public IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
        {
            if (!_hasPlacement)
            {
                return Array.Empty<AprilTagDetection>();
            }

            return _one;
        }
    }

    public interface IPassthroughFrameSource
    {
        bool IsAvailable { get; }
        string UnavailableReason { get; }
        bool TryGetLatest(out PassthroughFrame frame);
    }

    public sealed class UnavailablePassthroughFrameSource : IPassthroughFrameSource
    {
        public const string DefaultReason =
            "PassthroughCameraAccess is not in this project. It ships in com.meta.xr.mrutilitykit 203.0.0, which is not installed. See docs/adr/213-apriltag-passthrough.md.";

        public UnavailablePassthroughFrameSource()
            : this(DefaultReason)
        {
        }

        public UnavailablePassthroughFrameSource(string reason)
        {
            UnavailableReason = string.IsNullOrEmpty(reason) ? DefaultReason : reason;
        }

        public bool IsAvailable => false;
        public string UnavailableReason { get; }

        public bool TryGetLatest(out PassthroughFrame frame)
        {
            frame = default;
            return false;
        }
    }

    /// <summary>
    /// Edit-mode and dry-run source. It returns the frame assigned in code and
    /// never pretends to be the headset camera.
    /// </summary>
    public sealed class FakePassthroughFrameSource : IPassthroughFrameSource
    {
        public PassthroughFrame Frame;

        public bool IsAvailable => true;
        public string UnavailableReason => "";

        public bool TryGetLatest(out PassthroughFrame frame)
        {
            frame = Frame;
            return true;
        }
    }

    public static class AprilTagSpikePipeline
    {
        public static bool TryPositionError(
            IAprilTagDetector detector,
            IAprilTagDetector baseline,
            PassthroughFrame frame,
            int tagId,
            out float positionErrorMeters)
        {
            positionErrorMeters = 0f;
            if (detector == null || baseline == null)
            {
                return false;
            }

            var found = detector.Detect(frame);
            var placed = baseline.Detect(frame);
            for (var i = 0; i < found.Count; i++)
            {
                if (found[i].Id != tagId || !found[i].CountsTowardHertz)
                {
                    continue;
                }

                if (AprilTagBaselineComparison.TryMatch(found[i], placed, out var match))
                {
                    positionErrorMeters = AprilTagBaselineComparison.PositionErrorMeters(found[i], match);
                    return true;
                }
            }

            return false;
        }
    }

    public static class AprilTagBaselineComparison
    {
        public static float PositionErrorMeters(AprilTagDetection estimated, AprilTagDetection baseline)
        {
            return AprilTagPoseError.PositionErrorMeters(estimated.PositionMeters, baseline.PositionMeters);
        }

        public static float AngularErrorDegrees(AprilTagDetection estimated, AprilTagDetection baseline)
        {
            return AprilTagPoseError.AngularErrorDegrees(estimated.Rotation, baseline.Rotation);
        }

        public static bool TryMatch(
            AprilTagDetection estimated,
            IReadOnlyList<AprilTagDetection> baseline,
            out AprilTagDetection match)
        {
            if (baseline != null)
            {
                for (var i = 0; i < baseline.Count; i++)
                {
                    if (baseline[i].Id == estimated.Id && baseline[i].Family == estimated.Family)
                    {
                        match = baseline[i];
                        return true;
                    }
                }
            }

            match = default;
            return false;
        }
    }
}
