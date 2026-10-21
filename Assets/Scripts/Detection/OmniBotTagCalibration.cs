using System;
using System.Collections.Generic;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// One AprilTag mount. <see cref="TagToBaseRos"/> is the OHH-223
    /// <c>T_tag_base</c> matrix (p_base = T * p_tag). Poses are nominal CAD,
    /// not a tape or camera measurement.
    /// </summary>
    public sealed class TagMount
    {
        public TagMount(
            int id,
            string frame,
            double sizeMeters,
            RigidTransform tagToBaseRos,
            RigidTransform tagToBaseUnity)
        {
            Id = id;
            Frame = frame ?? "";
            SizeMeters = sizeMeters;
            TagToBaseRos = tagToBaseRos;
            TagToBaseUnity = tagToBaseUnity;
        }

        public int Id { get; }
        public string Frame { get; }
        public double SizeMeters { get; }
        public RigidTransform TagToBaseRos { get; }
        public RigidTransform TagToBaseUnity { get; }
    }

    /// <summary>
    /// Nominal OmniBot tag36h11 mounts from OmniBot branch
    /// <c>ohh-223-apriltag-mount</c>, <c>docs/apriltags.md</c> and
    /// <c>robot_descriptor.yaml</c>. Black-square side is 0.08 m.
    /// These numbers are nominal, to be measured on the robot.
    /// </summary>
    public sealed class OmniBotTagCalibration
    {
        public const string Family = "tag36h11";

        public const string NominalNote =
            "OHH-223 nominal CAD, to be measured. Do not treat T_tag_base as a calibrated mount.";

        private readonly Dictionary<int, TagMount> _byId;

        public OmniBotTagCalibration(IReadOnlyList<TagMount> mounts)
        {
            Mounts = mounts ?? Array.Empty<TagMount>();
            _byId = new Dictionary<int, TagMount>(Mounts.Count);
            for (var i = 0; i < Mounts.Count; i++)
            {
                _byId[Mounts[i].Id] = Mounts[i];
            }
        }

        public static OmniBotTagCalibration Nominal { get; } = BuildNominal();

        public IReadOnlyList<TagMount> Mounts { get; }

        public bool TryGet(int id, out TagMount mount)
        {
            return _byId.TryGetValue(id, out mount);
        }

        private static OmniBotTagCalibration BuildNominal()
        {
            return new OmniBotTagCalibration(new[]
            {
                Mount(0, "apriltag_front_link",
                    0.0, 0.707106781, -0.707106781, 0.137855339,
                    1.0, 0.0, 0.0, 0.0,
                    0.0, -0.707106781, -0.707106781, 0.208355339),
                Mount(1, "apriltag_rear_link",
                    0.0, -0.707106781, 0.707106781, -0.092144661,
                    -1.0, 0.0, 0.0, -0.07,
                    0.0, -0.707106781, -0.707106781, 0.208355339),
                Mount(2, "apriltag_arm_base_link",
                    -1.0, 0.0, 0.0, -0.078,
                    0.0, 0.707106781, -0.707106781, 0.079855339,
                    0.0, -0.707106781, -0.707106781, 0.208355339),
            });
        }

        private static TagMount Mount(
            int id,
            string frame,
            double a00, double a01, double a02, double a03,
            double a10, double a11, double a12, double a13,
            double a20, double a21, double a22, double a23)
        {
            var ros = RigidTransform.FromRowMajor(
                a00, a01, a02, a03,
                a10, a11, a12, a13,
                a20, a21, a22, a23);
            return new TagMount(id, frame, 0.08, ros, DetectionFrames.TagToBaseUnity(ros).Orthonormalized());
        }
    }
}
