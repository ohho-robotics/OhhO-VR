namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Steady-state Kalman update on the robot pose. Odometry is the control
    /// input, so the gain is a low-pass on the tag measurement. A variance floor
    /// keeps the gain from collapsing onto the first sample.
    /// </summary>
    public sealed class RobotPoseKalmanFilter
    {
        public const double MeasurementStdMeters = 0.015;
        public const double MeasurementVariance = MeasurementStdMeters * MeasurementStdMeters;
        public const double VarianceFloor = MeasurementVariance;
        public const double ProcessVariancePerSecond = 4e-6;

        public bool HasPose { get; private set; }
        public RigidTransform Pose { get; private set; }
        public double Variance { get; private set; }

        public void Reset(RigidTransform measurement)
        {
            Pose = measurement;
            Variance = MeasurementVariance;
            HasPose = true;
        }

        public void SetPose(RigidTransform pose)
        {
            Pose = pose;
            HasPose = true;
        }

        public void Predict(RigidTransform deltaInPreviousBase, double dtSeconds)
        {
            if (!HasPose)
            {
                return;
            }

            Pose = Pose.Mul(deltaInPreviousBase);
            if (dtSeconds > 0.0)
            {
                Variance += ProcessVariancePerSecond * dtSeconds;
            }

            if (Variance < VarianceFloor)
            {
                Variance = VarianceFloor;
            }
        }

        public void Update(RigidTransform measurement)
        {
            if (!HasPose)
            {
                Reset(measurement);
                return;
            }

            var gain = Variance / (Variance + MeasurementVariance);
            if (gain < 0.0) gain = 0.0;
            if (gain > 1.0) gain = 1.0;
            Pose = RigidTransform.Interpolate(Pose, measurement, gain);
            Variance = (1.0 - gain) * Variance;
            if (Variance < 1e-8)
            {
                Variance = 1e-8;
            }
        }
    }
}
