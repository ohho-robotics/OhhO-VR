using System.Globalization;
using System.Text;

namespace OmniBot.VR.AprilTagSpike
{
    public readonly struct AprilTagMeasurementSample
    {
        public AprilTagMeasurementSample(
            float rangeMeters,
            float hertz,
            float frameAgeMilliseconds,
            float detectCpuMilliseconds,
            float cpuPercent,
            float positionErrorMeters,
            float angularErrorDegrees,
            string detectorName)
        {
            RangeMeters = rangeMeters;
            Hertz = hertz;
            FrameAgeMilliseconds = frameAgeMilliseconds;
            DetectCpuMilliseconds = detectCpuMilliseconds;
            CpuPercent = cpuPercent;
            PositionErrorMeters = positionErrorMeters;
            AngularErrorDegrees = angularErrorDegrees;
            DetectorName = detectorName ?? "";
        }

        public float RangeMeters { get; }
        public float Hertz { get; }
        public float FrameAgeMilliseconds { get; }
        public float DetectCpuMilliseconds { get; }
        public float CpuPercent { get; }
        public float PositionErrorMeters { get; }
        public float AngularErrorDegrees { get; }
        public string DetectorName { get; }

        public bool MeetsSpikeAcceptance =>
            AprilTagPoseError.IsAcceptanceRange(RangeMeters)
            && AprilTagPoseError.MeetsRateAcceptance(Hertz)
            && AprilTagPoseError.MeetsPositionAcceptance(PositionErrorMeters);

        public string GateLabel =>
            AprilTagPoseError.IsAcceptanceRange(RangeMeters)
                ? (MeetsSpikeAcceptance ? "yes" : "no")
                : "record";
    }

    public static class AprilTagMeasurementLog
    {
        public const string Header =
            "| Range (m) | Hz | Frame age (ms) | Detect CPU (ms) | App CPU (%) | Position error (m) | Angular error (deg) | Detector | Gate |";

        public const string Separator =
            "|---:|---:|---:|---:|---:|---:|---:|---|---|";

        public static string FormatRow(AprilTagMeasurementSample sample)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "| {0:0.00} | {1:0.00} | {2:0.0} | {3:0.0} | {4:0.0} | {5:0.000} | {6:0.00} | {7} | {8} |",
                sample.RangeMeters,
                sample.Hertz,
                sample.FrameAgeMilliseconds,
                sample.DetectCpuMilliseconds,
                sample.CpuPercent,
                sample.PositionErrorMeters,
                sample.AngularErrorDegrees,
                sample.DetectorName,
                sample.GateLabel);
        }

        public static string FormatProtocolTemplate()
        {
            var builder = new StringBuilder();
            builder.AppendLine(Header);
            builder.AppendLine(Separator);
            var ranges = AprilTagSpikeConfig.ProtocolRangeMeters;
            for (var i = 0; i < ranges.Length; i++)
            {
                var gate = AprilTagPoseError.IsAcceptanceRange(ranges[i]) ? "gate" : "record";
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "| {0:0.00} | - | - | - | - | - | - | - | {1} |",
                    ranges[i],
                    gate);
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
