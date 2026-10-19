using UnityEngine;
using UnityEngine.UI;
using TMPro;
using OmniBot.VR.Control;

namespace OmniBot.VR.UI.Teleop
{
    /// <summary>
    /// Takeoff and land buttons. Each click reads the confirmation field and
    /// refuses the request when the phrase does not match. Sticks are not read.
    /// </summary>
    public class AerialFlightButtons : MonoBehaviour
    {
        [SerializeField] private TeleopController teleop;
        [SerializeField] private Button takeoffButton;
        [SerializeField] private Button landButton;
        [SerializeField] private TMP_InputField confirmationInput;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (teleop == null) teleop = FindObjectOfType<TeleopController>();
            if (takeoffButton != null) takeoffButton.onClick.AddListener(OnTakeoff);
            if (landButton != null) landButton.onClick.AddListener(OnLand);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (takeoffButton != null) takeoffButton.onClick.RemoveListener(OnTakeoff);
            if (landButton != null) landButton.onClick.RemoveListener(OnLand);
        }

        void OnTakeoff()
        {
            if (teleop == null) return;
            if (!teleop.RequestAerialTakeoff(Phrase()))
                Debug.LogWarning("[AerialFlightButtons] Takeoff refused. Type the confirmation phrase.");
        }

        void OnLand()
        {
            if (teleop == null) return;
            if (!teleop.RequestAerialLand(Phrase()))
                Debug.LogWarning("[AerialFlightButtons] Land refused. Type the confirmation phrase.");
        }

        string Phrase()
        {
            return confirmationInput != null ? confirmationInput.text : "";
        }
    }
}
