using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Headset-side joint-zero offsets for the SO-101, loaded from
    /// <c>Resources/so101_joint_zeros.json</c>.
    ///
    /// The shipped file is uncalibrated and all zeros. Servo tick homes live
    /// on the robot (<c>arm_params.yaml</c> <c>home_ticks</c>) and are still
    /// the uncalibrated STS3215 midpoint. This loader will not turn those
    /// ticks into radians and will not apply a non-zero offset unless the
    /// file says <c>calibrated: true</c> (OHH-94).
    /// </summary>
    public sealed class So101JointZeroSnapshot
    {
        public bool Calibrated;
        public float[] OffsetsRad;
        public string Note;

        public static So101JointZeroSnapshot UncalibratedZeros(int n)
        {
            return new So101JointZeroSnapshot
            {
                Calibrated = false,
                OffsetsRad = new float[n],
                Note = "No so101_joint_zeros asset. Kinematic zeros stay 0 rad. OHH-94 owns real servo homes.",
            };
        }
    }

    public static class So101JointZeroConfig
    {
        private const string ResourceName = "so101_joint_zeros";
        private static So101JointZeroSnapshot _cached;

        [System.Serializable]
        private class FileDto
        {
            public bool calibrated;
            public float[] jointZeroOffsetsRad;
            public string note;
        }

        /// <summary>Parse JSON. Does not touch <see cref="Resources"/>.</summary>
        public static So101JointZeroSnapshot Parse(string json)
        {
            int n = RobotDescriptor.So101.JointCount;
            var zeros = new float[n];
            if (string.IsNullOrEmpty(json))
                return So101JointZeroSnapshot.UncalibratedZeros(n);

            FileDto dto;
            try { dto = JsonUtility.FromJson<FileDto>(json); }
            catch
            {
                return So101JointZeroSnapshot.UncalibratedZeros(n);
            }
            if (dto == null || dto.jointZeroOffsetsRad == null || dto.jointZeroOffsetsRad.Length != n)
            {
                return new So101JointZeroSnapshot
                {
                    Calibrated = false,
                    OffsetsRad = zeros,
                    Note = dto != null ? dto.note : "Joint-zero config missing or the wrong length. Offsets forced to 0.",
                };
            }

            bool nonzero = false;
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(dto.jointZeroOffsetsRad[i]) || float.IsInfinity(dto.jointZeroOffsetsRad[i]))
                    return So101JointZeroSnapshot.UncalibratedZeros(n);
                if (Mathf.Abs(dto.jointZeroOffsetsRad[i]) > 1e-6f) nonzero = true;
            }

            // An uncalibrated file must not move the arm. OHH-94 flips
            // calibrated only after the real mechanism is measured.
            if (!dto.calibrated && nonzero)
            {
                return new So101JointZeroSnapshot
                {
                    Calibrated = false,
                    OffsetsRad = zeros,
                    Note = "Ignored non-zero offsets because calibrated is false. OHH-94 has not landed.",
                };
            }

            return new So101JointZeroSnapshot
            {
                Calibrated = dto.calibrated,
                OffsetsRad = (float[])dto.jointZeroOffsetsRad.Clone(),
                Note = dto.note ?? "",
            };
        }

        public static So101JointZeroSnapshot Load()
        {
            if (_cached != null) return _cached;
            var asset = Resources.Load<TextAsset>(ResourceName);
            _cached = asset != null
                ? Parse(asset.text)
                : So101JointZeroSnapshot.UncalibratedZeros(RobotDescriptor.So101.JointCount);
            return _cached;
        }

        /// <summary>Test hook. Production code loads once.</summary>
        public static void ClearCache() => _cached = null;
    }
}
