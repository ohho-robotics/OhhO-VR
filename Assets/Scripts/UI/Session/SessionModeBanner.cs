using TMPro;
using UnityEngine;
using UnityEngine.UI;
using OmniBot.VR.Control.Session;

namespace OmniBot.VR.UI.Session
{
    /// <summary>
    /// Blue SIM / orange REAL strip. <see cref="Apply"/> drops one on a page so the
    /// mode stays visible on teleop, the camera, the controls, and the garage.
    /// </summary>
    public static class SessionModeBanner
    {
        public static void Apply(GameObject root, SessionKind kind)
        {
            if (root == null) return;
            var view = root.GetComponentInChildren<SessionModeBannerView>(true);
            if (view == null || view.transform.parent != root.transform)
                view = SessionModeBannerView.Create(root.transform);
            view.Show(kind);
        }

        public static void Hide(GameObject root)
        {
            if (root == null) return;
            var view = root.GetComponentInChildren<SessionModeBannerView>(true);
            if (view != null) view.Hide();
        }
    }

    public sealed class SessionModeBannerView : MonoBehaviour
    {
        private Image _image;
        private TMP_Text _label;

        public static SessionModeBannerView Create(Transform parent)
        {
            var go = new GameObject("Session Mode Banner", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 36f);
            rt.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.sprite = SessionUi.WhiteSprite;
            image.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 20;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.raycastTarget = false;
            var sample = parent.GetComponentInChildren<TMP_Text>(true);
            if (sample != null && sample.font != null) text.font = sample.font;

            var view = go.AddComponent<SessionModeBannerView>();
            view._image = image;
            view._label = text;
            go.transform.SetAsLastSibling();
            return view;
        }

        public void Show(SessionKind kind)
        {
            if (_image != null) _image.color = SessionBannerStyle.ColorFor(kind);
            if (_label != null) _label.text = SessionBannerStyle.LabelFor(kind);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>1×1 sprite so runtime-built UI Images actually draw.</summary>
    public static class SessionUi
    {
        private static Sprite _white;

        public static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return _white;
            }
        }
    }
}
