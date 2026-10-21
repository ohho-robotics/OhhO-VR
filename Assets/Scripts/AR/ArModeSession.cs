using OmniBot.VR.Detection;
using UnityEngine;

namespace OmniBot.VR.AR
{
    public enum ArPlacementMode
    {
        Unplaced = 0,
        Tracking = 1,
        Manual = 2,
    }

    /// <summary>
    /// Where the robot-anchored HUD sits in the base frame, and which way its
    /// text faces. Position stays on the robot. Facing is yaw-only toward the headset.
    /// </summary>
    public static class ArAnchoring
    {
        /// <summary>Metres above and in front of the detected base origin.</summary>
        public static readonly Vector3 HudLocalOffset = new Vector3(0f, 0.55f, 0.12f);

        public static Vector3 HudWorldPosition(Pose robotBase)
        {
            return robotBase.position + robotBase.rotation * HudLocalOffset;
        }

        public static Quaternion HudFacing(Vector3 hudWorldPosition, Pose head)
        {
            var toward = head.position - hudWorldPosition;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-8f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(toward.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// Places the twin in front of the headset when AprilTag tracking is not locked.
    /// No floor raycast: pitch is flattened so the base stays upright.
    /// </summary>
    public static class ArManualPlacement
    {
        public const float DefaultDistanceMeters = 1.2f;
        public const float DefaultDropMeters = 1.2f;

        public static Pose InFrontOf(Pose head, float distanceMeters, float dropMeters)
        {
            if (distanceMeters < 0.2f)
            {
                distanceMeters = 0.2f;
            }

            var forward = head.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            var position = head.position + forward * distanceMeters - Vector3.up * dropMeters;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            return new Pose(position, rotation);
        }
    }

    /// <summary>
    /// Chooses the twin root pose. A locked detection wins. Otherwise a stored
    /// manual pose is used. With neither, the twin stays hidden.
    /// </summary>
    public sealed class ArModeSession
    {
        private RobotDetectionSnapshot _snapshot = RobotDetectionSnapshot.Inactive(0);
        private bool _hasManual;
        private Pose _manual;

        public RobotDetectionSnapshot Snapshot => _snapshot;
        public ArPlacementMode Mode { get; private set; } = ArPlacementMode.Unplaced;
        public bool TwinVisible { get; private set; }
        public Pose TwinPose { get; private set; } = new Pose(Vector3.zero, Quaternion.identity);
        public bool HasManualPlacement => _hasManual;
        public Pose ManualPose => _manual;

        public static bool IsTracking(RobotDetectionSnapshot snapshot)
        {
            return snapshot.State == RobotTrackState.Locked && snapshot.HasPose;
        }

        public void ApplyDetection(RobotDetectionSnapshot snapshot)
        {
            _snapshot = snapshot;
            Recompute();
        }

        public void PlaceManually(Pose worldPose)
        {
            _hasManual = true;
            _manual = worldPose;
            Recompute();
        }

        public void ClearManualPlacement()
        {
            _hasManual = false;
            Recompute();
        }

        private void Recompute()
        {
            if (IsTracking(_snapshot))
            {
                Mode = ArPlacementMode.Tracking;
                TwinVisible = true;
                TwinPose = _snapshot.Pose;
                return;
            }

            if (_hasManual)
            {
                Mode = ArPlacementMode.Manual;
                TwinVisible = true;
                TwinPose = _manual;
                return;
            }

            Mode = ArPlacementMode.Unplaced;
            TwinVisible = false;
        }
    }
}
