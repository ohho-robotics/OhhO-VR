using UnityEngine;
using TMPro;

namespace OmniBot.VR.UI
{
    /// <summary>
    /// First-run onboarding overlay — shows a one-time panel with the control
    /// scheme hints when the user starts teleop for the first time (or when they
    /// hold the menu button). Dismissed by a button press or the B button; never
    /// shown again unless the user resets it from settings.
    ///
    /// The hint text is populated from the active
    /// <see cref="OmniBot.VR.Control.IDriveScheme"/> + manipulation scheme's
    /// <c>HudHint</c> so it's always correct for the selected robot.
    /// </summary>
    public class OnboardingController : MonoBehaviour
    {
        [SerializeField] private GameObject onboardingPanel;
        [SerializeField] private TMP_Text driveHintText;
        [SerializeField] private TMP_Text manipHintText;
        [SerializeField] private TMP_Text safetyHintText;

        private const string SeenKey = "ohho.onboarding_seen";

        /// <summary>
        /// Safety line shown on the onboarding panel. E-stop is latched: releasing
        /// the buttons or grips does not clear it.
        /// </summary>
        public const string SafetyHint =
            "Left grip: deadman (on unless this is a sim session). A+B or the HUD button latches e-stop. Releasing the buttons or grips does not clear it. Reset, release the deadman, then press it again. Right-trigger turbo is sim-only and clamped to 0.2 m/s on OmniBot.";

        private void Start()
        {
            if (onboardingPanel != null) onboardingPanel.SetActive(false);
        }

        private void Update()
        {
            // B button dismisses
            if (onboardingPanel != null && onboardingPanel.activeSelf &&
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
            {
                Dismiss();
            }
        }

        /// <summary>True if the user has seen the onboarding at least once.</summary>
        public static bool HasSeenOnboarding => PlayerPrefs.GetInt(SeenKey, 0) == 1;

        /// <summary>
        /// Show the onboarding panel with the given scheme hints, unless the user
        /// has already dismissed it. Called by TeleopController on StartTeleop.
        /// </summary>
        public void ShowIfFirstTime(string driveHint, string manipHint)
        {
            if (HasSeenOnboarding || onboardingPanel == null) return;
            if (driveHintText != null) driveHintText.text = driveHint ?? "";
            if (manipHintText != null) manipHintText.text = manipHint ?? "No arm on this robot";
            if (safetyHintText != null)
                safetyHintText.text = SafetyHint;
            onboardingPanel.SetActive(true);
        }

        /// <summary>Force-show the onboarding (e.g. from a settings button).</summary>
        public void Show(string driveHint, string manipHint)
        {
            if (driveHintText != null) driveHintText.text = driveHint ?? "";
            if (manipHintText != null) manipHintText.text = manipHint ?? "No arm on this robot";
            if (safetyHintText != null)
                safetyHintText.text = SafetyHint;
            if (onboardingPanel != null) onboardingPanel.SetActive(true);
        }

        /// <summary>Dismiss the panel and mark onboarding as seen.</summary>
        public void Dismiss()
        {
            if (onboardingPanel != null) onboardingPanel.SetActive(false);
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Reset the onboarding flag so it shows again next session.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(SeenKey);
            PlayerPrefs.Save();
        }
    }
}
