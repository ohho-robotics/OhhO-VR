using UnityEngine;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// A snapshot of the VR controller state fed to <see cref="Drive.IDriveScheme"/>
    /// each frame. Filled by <see cref="TeleopController"/> from OVRInput so the
    /// drive schemes stay testable (no OVR dependency inside the scheme itself).
    /// </summary>
    public struct TeleopInput
    {
        /// <summary>Left thumbstick, range [-1, 1] on each axis after dead-zone.</summary>
        public Vector2 LeftStick;

        /// <summary>Right thumbstick, range [-1, 1] on each axis after dead-zone.</summary>
        public Vector2 RightStick;

        /// <summary>Left grip held this frame.</summary>
        public bool LeftGrip;

        /// <summary>Right grip held this frame.</summary>
        public bool RightGrip;

        /// <summary>Both grips held. This latches emergency stop; it is not a drive or turbo chord.</summary>
        public bool BothGrips;

        /// <summary>Rising edge of both-grips this frame (latches e-stop).</summary>
        public bool BothGripsPressed;

        /// <summary>
        /// Falling edge of both-grips this frame. Does not clear e-stop: the grips are
        /// also the deadman and turbo, so release-to-clear would resume driving.
        /// </summary>
        public bool BothGripsReleased;

        /// <summary>
        /// Explicit e-stop reset this frame (left controller X). Ignored while both
        /// grips are still held. This is the only input that clears a latched e-stop.
        /// </summary>
        public bool ResetEmergencyStop;

        /// <summary>Seconds since the previous frame.</summary>
        public float DeltaTime;
    }

    /// <summary>
    /// A snapshot of one tracked hand, fed to
    /// <see cref="Manip.IManipulationScheme"/>. Position/rotation are in world
    /// space; <see cref="TeleopController"/> resolves them relative to the arm
    /// workspace origin inside the scheme.
    /// </summary>
    public struct HandPose
    {
        public bool Tracked;
        public Vector3 Position;
        public Quaternion Rotation;
        public float PinchStrength; // [0..1]
    }
}
