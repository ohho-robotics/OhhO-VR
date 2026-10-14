using UnityEngine;

namespace OmniBot.VR.AR
{
    /// <summary>
    /// World-space readout parented to the twin. Local position is the robot
    /// offset; yaw turns toward the headset so the text stays readable.
    /// </summary>
    public sealed class ArRobotHudView : MonoBehaviour
    {
        private TextMesh _text;

        public string Text
        {
            get
            {
                EnsureText();
                return _text.text ?? "";
            }
        }

        public void SetText(string value)
        {
            EnsureText();
            _text.text = value ?? "";
        }

        public void ApplyAnchor(Transform robot, Vector3 localOffset, Pose head)
        {
            EnsureText();
            if (robot == null)
            {
                return;
            }

            transform.SetParent(robot, false);
            transform.localPosition = localOffset;
            transform.localScale = Vector3.one;
            transform.rotation = ArAnchoring.HudFacing(transform.position, head);
        }

        private void EnsureText()
        {
            if (_text != null)
            {
                return;
            }

            _text = GetComponent<TextMesh>();
            if (_text == null)
            {
                _text = gameObject.AddComponent<TextMesh>();
            }

            _text.fontSize = 64;
            _text.characterSize = 0.0045f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(0.75f, 0.95f, 1f, 1f);
            _text.richText = false;
        }
    }
}
