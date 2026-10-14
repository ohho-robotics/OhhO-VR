using UnityEngine;

namespace OmniBot.VR.Safety
{
    /// <summary>
    /// Which session the operator is about to start.
    /// <see cref="Unspecified"/> fails closed: it is blocked until the current
    /// operator-rules version is accepted, same as <see cref="Real"/>.
    /// </summary>
    public enum SessionTarget
    {
        Unspecified = 0,
        Sim = 1,
        Real = 2,
    }

    /// <summary>
    /// Persisted, versioned acceptance of <c>docs/operator-rules.md</c>.
    /// A real-robot session stays blocked until the stored version equals
    /// <see cref="RulesVersion"/>. Bump that constant in the same change as
    /// the rules document so a rules change asks again. Sim sessions are
    /// never blocked.
    /// </summary>
    public static class OperatorRulesAcknowledgement
    {
        /// <summary>Matches the "Rules version:" line in docs/operator-rules.md.</summary>
        public const int RulesVersion = 1;

        public const string PrefKey = "ohho.operator_rules.accepted_version";

        public const string RulesDocument = "docs/operator-rules.md";

        /// <summary>Text the in-app acknowledgement shows. Keep it in the rules document.</summary>
        public const string Summary =
            "Hardware e-stop is mandatory and must be in reach before a REAL session. " +
            "Keep line of sight to the robot and everyone near it. " +
            "Speed cap for OmniBot is 0.2 m/s linear and 1.0 rad/s angular; do not raise it in calibration. " +
            "Clear the area of people, animals, and loose objects inside the robot's reach. " +
            "Varun must be present for every real robot.";

        public static readonly string[] MandatoryPhrases =
        {
            "Hardware e-stop",
            "line of sight",
            "0.2 m/s",
            "1.0 rad/s",
            "Clear the area",
            "Varun must be present",
        };

        public static int AcceptedVersion => PlayerPrefs.GetInt(PrefKey, 0);

        public static bool IsCurrentVersionAccepted => AcceptedVersion == RulesVersion;

        /// <summary>
        /// True when <paramref name="target"/> must not start. Sim is always false.
        /// Every other target is blocked until <paramref name="rulesVersion"/> is stored.
        /// </summary>
        public static bool Blocks(SessionTarget target, int rulesVersion)
        {
            if (target == SessionTarget.Sim)
                return false;
            return AcceptedVersion != rulesVersion;
        }

        public static bool Blocks(SessionTarget target) => Blocks(target, RulesVersion);

        public static string RefusalMessage =>
            "Real-robot teleop is blocked until operator rules version " +
            RulesVersion + " are accepted (" + RulesDocument + ").";

        public static void Accept()
        {
            PlayerPrefs.SetInt(PrefKey, RulesVersion);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }
    }
}
