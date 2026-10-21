namespace OmniBot.VR.Detection
{
    public enum RobotTrackState
    {
        Searching = 0,
        Locked = 1,
        Lost = 2,
    }

    /// <summary>
    /// Searching until tags agree, locked while detections or odom keep the
    /// pose fresh, lost after <see cref="RobotDetectionLimits.LostTimeoutSeconds"/>
    /// without an accepted detection. A later detection re-locks immediately.
    /// </summary>
    public sealed class RobotTrackStateMachine
    {
        private RigidTransform _candidate;
        private bool _hasCandidate;
        private double _lastDetectionTime;
        private bool _seenDetection;

        public RobotTrackState State { get; private set; } = RobotTrackState.Searching;
        public bool LockedThisStep { get; private set; }
        public bool RelockedThisStep { get; private set; }
        public double LockTimeSeconds { get; private set; } = double.NaN;

        public void Propagate(RigidTransform deltaInPreviousBase)
        {
            if (_hasCandidate)
            {
                _candidate = _candidate.Mul(deltaInPreviousBase);
            }
        }

        /// <summary>
        /// Left-multiply. The anchor moved in the world; the robot did not.
        /// </summary>
        public void ApplyWorldCorrection(RigidTransform previousAnchorToCurrent)
        {
            if (_hasCandidate)
            {
                _candidate = previousAnchorToCurrent.Mul(_candidate);
            }
        }

        public void NoteMiss(double timeSeconds)
        {
            LockedThisStep = false;
            RelockedThisStep = false;
            if (!_seenDetection)
            {
                return;
            }

            if (timeSeconds - _lastDetectionTime <= RobotDetectionLimits.LostTimeoutSeconds)
            {
                return;
            }

            if (State == RobotTrackState.Locked)
            {
                State = RobotTrackState.Lost;
            }

            _hasCandidate = false;
        }

        /// <summary>
        /// Returns true when the measurement should update the published pose.
        /// </summary>
        public bool NoteMeasurement(double timeSeconds, RigidTransform measurement, int inlierCount)
        {
            LockedThisStep = false;
            RelockedThisStep = false;
            var previousDetectionTime = _lastDetectionTime;
            var hadDetection = _seenDetection;
            _lastDetectionTime = timeSeconds;
            _seenDetection = true;

            if (State == RobotTrackState.Lost)
            {
                State = RobotTrackState.Locked;
                RelockedThisStep = true;
                _hasCandidate = false;
                return true;
            }

            if (State == RobotTrackState.Locked)
            {
                return true;
            }

            if (inlierCount >= 2)
            {
                EnterLock(timeSeconds);
                return true;
            }

            var staleCandidate = hadDetection
                && timeSeconds - previousDetectionTime > RobotDetectionLimits.LostTimeoutSeconds;
            if (!_hasCandidate || staleCandidate)
            {
                _candidate = measurement;
                _hasCandidate = true;
                return false;
            }

            var position = RigidTransform.PositionDistance(_candidate, measurement);
            var angle = RigidTransform.AngleRadians(_candidate, measurement);
            if (position <= RobotDetectionLimits.LockAgreementMeters
                && angle <= RobotDetectionLimits.LockAgreementRadians)
            {
                EnterLock(timeSeconds);
                return true;
            }

            _candidate = measurement;
            _hasCandidate = true;
            return false;
        }

        private void EnterLock(double timeSeconds)
        {
            State = RobotTrackState.Locked;
            LockedThisStep = true;
            if (double.IsNaN(LockTimeSeconds))
            {
                LockTimeSeconds = timeSeconds;
            }

            _hasCandidate = false;
        }
    }
}
