using UnityEngine;

namespace OmniBot.VR.UI.Operate
{
    /// <summary>
    /// Readability for the operate dashboard in bright passthrough.
    /// Body text must subtend at least 0.9° at the operate distance, and
    /// colours must stay above WCAG AA against a white room showing through
    /// a translucent panel.
    /// </summary>
    public static class OperateComfort
    {
        public const float MinVisualAngleDegrees = 0.9f;
        public const float OperateDistanceMetres = 1.6f;
        public const float CanvasMetresPerUnit = 0.001f;
        public const float MinContrastRatio = 4.5f;

        public static readonly Color Panel = new Color(0.031f, 0.043f, 0.078f, 0.97f);
        public static readonly Color Chip = new Color(0.07f, 0.09f, 0.14f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.98f, 1f, 1f);
        public static readonly Color Muted = new Color(0.78f, 0.86f, 0.94f, 1f);
        public static readonly Color Danger = new Color(1f, 0.55f, 0.52f, 1f);
        public static readonly Color Safe = new Color(0.45f, 1f, 0.62f, 1f);
        public static readonly Color Amber = new Color(1f, 0.82f, 0.35f, 1f);
        public static readonly Color Accent = new Color(0f, 0.831f, 1f, 1f);
        public static readonly Color AccentText = new Color(0.02f, 0.04f, 0.06f, 1f);
        public static readonly Color DangerButton = new Color(0.55f, 0.08f, 0.09f, 1f);
        public static readonly Color DangerText = new Color(1f, 0.96f, 0.96f, 1f);

        public static float BodyFontSize =>
            Mathf.Ceil(MinFontSize(OperateDistanceMetres, CanvasMetresPerUnit));

        public static float MinTextHeightMetres(float distanceMetres)
        {
            float half = MinVisualAngleDegrees * 0.5f * Mathf.Deg2Rad;
            return 2f * Mathf.Max(0.05f, distanceMetres) * Mathf.Tan(half);
        }

        public static float MinFontSize(float distanceMetres, float canvasScale)
        {
            if (canvasScale < 1e-6f) canvasScale = CanvasMetresPerUnit;
            return MinTextHeightMetres(distanceMetres) / canvasScale;
        }

        public static float VisualAngleDegrees(float fontSize, float distanceMetres, float canvasScale)
        {
            if (distanceMetres <= 0f) return 0f;
            float height = fontSize * canvasScale;
            return 2f * Mathf.Atan(height / (2f * distanceMetres)) * Mathf.Rad2Deg;
        }

        public static bool MeetsVisualAngle(float fontSize, float distanceMetres, float canvasScale)
            => VisualAngleDegrees(fontSize, distanceMetres, canvasScale) + 1e-3f >= MinVisualAngleDegrees;

        public static float ContrastRatio(Color foreground, Color background)
        {
            float l1 = RelativeLuminance(foreground);
            float l2 = RelativeLuminance(background);
            float lighter = Mathf.Max(l1, l2);
            float darker = Mathf.Min(l1, l2);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary>Panel drawn over a fully lit passthrough room (white).</summary>
        public static Color CompositeOverWhite(Color panel)
        {
            float a = Mathf.Clamp01(panel.a);
            return new Color(
                panel.r * a + (1f - a),
                panel.g * a + (1f - a),
                panel.b * a + (1f - a),
                1f);
        }

        public static Color ReadableBackground(Color panel)
            => panel.a >= 0.999f ? panel : CompositeOverWhite(panel);

        public static bool PassthroughTextIsReadable(Color text, Color panel)
            => ContrastRatio(text, ReadableBackground(panel)) >= MinContrastRatio;

        public static float RelativeLuminance(Color color)
        {
            return 0.2126f * Linearize(color.r)
                 + 0.7152f * Linearize(color.g)
                 + 0.0722f * Linearize(color.b);
        }

        private static float Linearize(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
