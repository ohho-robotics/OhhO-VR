using UnityEngine;

namespace OmniBot.VR.Control.Session
{
    /// <summary>
    /// Copy and acknowledgement for driving a physical robot. The deadman stays on:
    /// nothing in the session picker can clear <see cref="RobotProfile.RequireDeadman"/>
    /// for a real session. OHH-59 / PR 6 owns the grip check in the drive loop.
    /// </summary>
    public static class RealRobotSafety
    {
        public const string SafetyBlock = "Acknowledge the real-robot safety notice before connecting.";

        public const string Notice =
            "This is a real robot. Keep people and obstacles clear before it moves. " +
            "The deadman is on and cannot be turned off: hold one grip to drive, and release it to stop. " +
            "Squeeze both grips for the emergency stop.";

        public const string Tutorial =
            "Deadman tutorial. Hold one grip to drive and release it to stop. " +
            "Both grips together are the emergency stop, not a faster drive. " +
            "Turbo is the right grip alone. The deadman stays on for a real robot — this headset will not turn it off.";

        /// <summary>True when the operator has dismissed the first-run deadman tutorial.</summary>
        public static bool TutorialSeen => PlayerPrefs.GetInt(SeenKey, 0) == 1;

        private const string SeenKey = "ohho.real_deadman_tutorial_seen";

        public static void MarkTutorialSeen()
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
        }

        public static void ResetTutorial()
        {
            PlayerPrefs.DeleteKey(SeenKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Real sessions always require the deadman. Returns false when a caller
        /// tries to switch it off — that request is ignored.
        /// </summary>
        public static bool TryDisableDeadman()
        {
            return false;
        }

        /// <summary>
        /// Put the profile back on the safe default before teleop starts.
        /// Simulation launched from this picker does not opt out either.
        /// </summary>
        public static void Apply(RobotProfile profile, SessionKind kind)
        {
            if (profile == null) return;
            profile.RequireDeadman = true;
            if (kind == SessionKind.Real)
                profile.RequireDeadman = true;
        }
    }

    /// <summary>Blue SIM and orange REAL. Used by every session banner.</summary>
    public static class SessionBannerStyle
    {
        public static readonly Color SimBlue = new Color32(0x1E, 0x73, 0xF2, 0xFF);
        public static readonly Color RealOrange = new Color32(0xF2, 0x7A, 0x14, 0xFF);

        public static Color ColorFor(SessionKind kind)
        {
            return kind == SessionKind.Real ? RealOrange : SimBlue;
        }

        public static string LabelFor(SessionKind kind)
        {
            return kind == SessionKind.Real ? "REAL" : "SIM";
        }
    }
}
