using System.Collections.Generic;

namespace OmniBot.VR.UI.Operate
{
    /// <summary>One axis-aligned region of the operate dashboard, in normalized canvas space.</summary>
    public readonly struct OperateRect
    {
        public readonly float XMin;
        public readonly float YMin;
        public readonly float XMax;
        public readonly float YMax;

        public OperateRect(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float CenterX => (XMin + XMax) * 0.5f;
        public float CenterY => (YMin + YMax) * 0.5f;

        public bool Overlaps(OperateRect other)
            => XMin < other.XMax && XMax > other.XMin && YMin < other.YMax && YMax > other.YMin;
    }

    /// <summary>
    /// Operate screen: mode/safety bar on top, telemetry on the left, sim screen
    /// in the centre, arm joints on the right, controls legend along the bottom.
    /// Gaps keep the regions from sharing pixels.
    /// </summary>
    public static class OperateLayoutSpec
    {
        public const string ModeBarName = "Mode Bar";
        public const string TelemetryName = "Telemetry Column";
        public const string SimScreenName = "Sim Screen";
        public const string ArmName = "Arm Column";
        public const string LegendName = "Controls Legend";
        public const string RecentreButtonName = "Recentre Button";
        public const string TeleopButtonName = "Teleop Button";
        public const string Nav2ButtonName = "Nav2 Button";
        public const string PolicyButtonName = "Policy Button";
        public const string RecordButtonName = "Record Button";
        public const string EStopButtonName = "E-Stop Button";
        public const string ResetButtonName = "Reset Button";
        public const string PolicyConfirmName = "Policy Confirm";
        public const string PolicyConfirmYesName = "Policy Confirm Yes";
        public const string PolicyConfirmNoName = "Policy Confirm No";

        public static readonly OperateRect ModeBar = new OperateRect(0.012f, 0.792f, 0.988f, 0.988f);
        public static readonly OperateRect Telemetry = new OperateRect(0.012f, 0.168f, 0.262f, 0.776f);
        public static readonly OperateRect SimScreen = new OperateRect(0.274f, 0.168f, 0.726f, 0.776f);
        public static readonly OperateRect Arm = new OperateRect(0.738f, 0.168f, 0.988f, 0.776f);
        public static readonly OperateRect Legend = new OperateRect(0.012f, 0.012f, 0.988f, 0.152f);

        public static IReadOnlyList<OperateRect> Regions { get; } = new[]
        {
            ModeBar, Telemetry, SimScreen, Arm, Legend
        };

        public const string LegendBody =
            "Drive   left stick moves  ·  right stick yaws  ·  right grip turbo\n" +
            "Arm   right hand aims the gripper  ·  pinch opens and closes  ·  left thumbs-up (0.5 s) enables the arm\n" +
            "Safety   both grips e-stop  ·  E-STOP stays on until Reset  ·  policy asks before it runs";
    }
}
