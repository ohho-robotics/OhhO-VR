using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Quest frame budget from OHH-231. Evaluation is pure data so edit-mode
    /// tests can run it without a headset. The numbers are limits, not measurements.
    /// </summary>
    public struct QuestPerfBudgets
    {
        public float WorstFrameMs;
        public float TargetFrameMs;
        public float AppCpuMs;
        public float GpuMs;
        public int MaxDrawCalls;
        public int MaxTriangles;
        public long MaxGcBytes;
        public long MaxTextureBytes;

        public static QuestPerfBudgets Default => new QuestPerfBudgets
        {
            WorstFrameMs = 13.8f,
            TargetFrameMs = 11.1f,
            AppCpuMs = 8f,
            GpuMs = 9f,
            MaxDrawCalls = 150,
            MaxTriangles = 300000,
            MaxGcBytes = 0,
            MaxTextureBytes = 400L * 1024L * 1024L,
        };
    }

    public enum QuestPerfSeverity
    {
        MissedTarget = 1,
        OverBudget = 2,
    }

    public struct QuestPerfViolation
    {
        public string Metric;
        public double Observed;
        public double Limit;
        public QuestPerfSeverity Severity;
    }

    /// <summary>
    /// One frame of counters. Flags stay false when that counter was not sampled,
    /// so a zero from a missing recorder is not treated as a real zero.
    /// </summary>
    public struct QuestPerfSample
    {
        public bool HasFrame;
        public float FrameMs;
        public bool HasCpu;
        public float CpuMs;
        public bool HasGpu;
        public float GpuMs;
        public bool HasDrawCalls;
        public long DrawCalls;
        public bool HasTriangles;
        public long Triangles;
        public bool HasGc;
        public long GcBytes;
        public bool HasTexture;
        public long TextureBytes;
    }

    public static class QuestPerfBudget
    {
        public static bool ShouldSample(bool isDevelopmentBuild, bool runInDevelopmentBuilds)
        {
            return isDevelopmentBuild && runInDevelopmentBuilds;
        }

        /// <summary>
        /// Map one <c>FrameTiming</c> into a sample. Non-positive times are
        /// "not measured": the editor and the first device frames often report 0.
        /// Frame time is the slower of the CPU frame and the GPU frame.
        /// </summary>
        public static QuestPerfSample FromFrameTiming(bool captured, double cpuFrameMs, double cpuMainMs, double gpuMs)
        {
            var sample = new QuestPerfSample();
            if (!captured) return sample;
            if (cpuFrameMs <= 0 && cpuMainMs <= 0 && gpuMs <= 0) return sample;

            if (cpuMainMs > 0 || cpuFrameMs > 0)
            {
                sample.HasCpu = true;
                sample.CpuMs = (float)(cpuMainMs > 0 ? cpuMainMs : cpuFrameMs);
            }

            double frame = 0;
            bool hasFrame = false;
            if (cpuFrameMs > 0)
            {
                frame = cpuFrameMs;
                hasFrame = true;
            }
            else if (cpuMainMs > 0)
            {
                frame = cpuMainMs;
                hasFrame = true;
            }

            if (gpuMs > 0)
            {
                sample.HasGpu = true;
                sample.GpuMs = (float)gpuMs;
                if (gpuMs > frame) frame = gpuMs;
                hasFrame = true;
            }

            if (hasFrame)
            {
                sample.HasFrame = true;
                sample.FrameMs = (float)frame;
            }
            return sample;
        }

        public static void Evaluate(QuestPerfSample sample, QuestPerfBudgets budgets, List<QuestPerfViolation> into)
        {
            if (into == null) return;

            if (sample.HasFrame)
            {
                if (sample.FrameMs > budgets.WorstFrameMs)
                    Add(into, "frameMs", sample.FrameMs, budgets.WorstFrameMs, QuestPerfSeverity.OverBudget);
                else if (sample.FrameMs > budgets.TargetFrameMs)
                    Add(into, "frameMs", sample.FrameMs, budgets.TargetFrameMs, QuestPerfSeverity.MissedTarget);
            }

            if (sample.HasCpu && sample.CpuMs > budgets.AppCpuMs)
                Add(into, "cpuMs", sample.CpuMs, budgets.AppCpuMs, QuestPerfSeverity.OverBudget);
            if (sample.HasGpu && sample.GpuMs > budgets.GpuMs)
                Add(into, "gpuMs", sample.GpuMs, budgets.GpuMs, QuestPerfSeverity.OverBudget);
            if (sample.HasDrawCalls && sample.DrawCalls > budgets.MaxDrawCalls)
                Add(into, "drawCalls", sample.DrawCalls, budgets.MaxDrawCalls, QuestPerfSeverity.OverBudget);
            if (sample.HasTriangles && sample.Triangles > budgets.MaxTriangles)
                Add(into, "triangles", sample.Triangles, budgets.MaxTriangles, QuestPerfSeverity.OverBudget);
            if (sample.HasGc && sample.GcBytes > budgets.MaxGcBytes)
                Add(into, "gcBytes", sample.GcBytes, budgets.MaxGcBytes, QuestPerfSeverity.OverBudget);
            if (sample.HasTexture && sample.TextureBytes > budgets.MaxTextureBytes)
                Add(into, "textureBytes", sample.TextureBytes, budgets.MaxTextureBytes, QuestPerfSeverity.OverBudget);
        }

        public static void AppendReport(StringBuilder sb, List<QuestPerfViolation> violations)
        {
            if (sb == null) return;
            sb.Append("[QuestPerf] ");
            if (violations == null || violations.Count == 0)
            {
                sb.Append("within budget");
                return;
            }

            for (int i = 0; i < violations.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                QuestPerfViolation v = violations[i];
                sb.Append(v.Severity == QuestPerfSeverity.MissedTarget ? "missed target " : "over budget ");
                sb.Append(v.Metric);
                sb.Append(' ');
                sb.Append(v.Observed.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(" > ");
                sb.Append(v.Limit.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        private static void Add(List<QuestPerfViolation> into, string metric, double observed, double limit, QuestPerfSeverity severity)
        {
            into.Add(new QuestPerfViolation
            {
                Metric = metric,
                Observed = observed,
                Limit = limit,
                Severity = severity,
            });
        }
    }
}
