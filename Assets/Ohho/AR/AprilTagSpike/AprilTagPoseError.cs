using UnityEngine;

namespace OmniBot.VR.AprilTagSpike
{
    /// <summary>
    /// Pose-error math for the OHH-213 gate. No camera and no native library.
    /// </summary>
    public static class AprilTagPoseError
    {
        public const float AcceptanceMaxPositionErrorMeters = 0.02f;
        public const float AcceptanceMinHertz = 10f;
        public const float AcceptanceRangeMeters = 1.5f;
        public const float AcceptanceRangeToleranceMeters = 0.01f;

        public static float PositionErrorMeters(Vector3 estimated, Vector3 reference)
        {
            return Vector3.Distance(estimated, reference);
        }

        public static float AngularErrorDegrees(Quaternion estimated, Quaternion reference)
        {
            var dot = Mathf.Abs(Quaternion.Dot(estimated, reference));
            dot = Mathf.Clamp(dot, 0f, 1f);
            return 2f * Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        /// <summary>Strict: exactly 2 cm does not pass.</summary>
        public static bool MeetsPositionAcceptance(float positionErrorMeters)
        {
            return positionErrorMeters < AcceptanceMaxPositionErrorMeters;
        }

        public static bool MeetsRateAcceptance(float hertz)
        {
            return hertz >= AcceptanceMinHertz;
        }

        public static bool IsAcceptanceRange(float rangeMeters)
        {
            return Mathf.Abs(rangeMeters - AcceptanceRangeMeters) <= AcceptanceRangeToleranceMeters;
        }

        public static float Hertz(int detectionCount, float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f || detectionCount <= 0)
            {
                return 0f;
            }

            return detectionCount / elapsedSeconds;
        }
    }

    /// <summary>
    /// Frame helpers the Quest script uses before comparing to the manual baseline.
    /// The point flip is the OpenCV camera y-down axis. Tag rotation is not
    /// converted here; confirm yaw on device before recording angular error.
    /// </summary>
    public static class AprilTagFrames
    {
        public static Vector3 OpenCvCameraPointToUnityCameraPoint(Vector3 openCvCameraPointMeters)
        {
            return new Vector3(
                openCvCameraPointMeters.x,
                -openCvCameraPointMeters.y,
                openCvCameraPointMeters.z);
        }

        public static Pose ComposeWorld(Pose parentWorld, Pose childLocal)
        {
            return new Pose(
                parentWorld.position + parentWorld.rotation * childLocal.position,
                parentWorld.rotation * childLocal.rotation);
        }
    }
}
