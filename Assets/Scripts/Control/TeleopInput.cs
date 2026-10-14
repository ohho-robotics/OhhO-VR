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

        /// <summary>Left grip held this frame. This is the deadman.</summary>
        public bool LeftGrip;

        /// <summary>Right grip held this frame. Not a deadman and not turbo.</summary>
        public bool RightGrip;

        /// <summary>Right index trigger. Turbo in a kind=sim session; ignored on a real robot.</summary>
        public bool RightTrigger;

        /// <summary>Right-controller A button.</summary>
        public bool ButtonA;

        /// <summary>Right-controller B button.</summary>
        public bool ButtonB;

        /// <summary>Both grips held. Not an e-stop chord.</summary>
        public bool BothGrips;

        /// <summary>Rising edge of both grips. Does not latch e-stop.</summary>
        public bool BothGripsPressed;

        /// <summary>Falling edge of both grips. Does not clear e-stop.</summary>
        public bool BothGripsReleased;

        /// <summary>Rising edge of A+B this frame. Latches emergency stop.</summary>
        public bool EmergencyStopPressed;

        /// <summary>HUD e-stop button pressed this frame. Latches emergency stop.</summary>
        public bool HudEmergencyStop;

        /// <summary>
        /// Explicit e-stop Reset this frame (left controller X). Does not clear the
        /// latch by itself: the deadman must then be released and pressed again.
        /// Ignored while A and B are still held.
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
