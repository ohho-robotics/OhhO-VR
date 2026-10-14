using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Development-build frame sampler. Release players never start the recorders.
    /// Uncheck <see cref="runInDevelopmentBuilds"/> to silence it in a dev build.
    /// Logs at most once a second, and only when a budget is missed, so the log
    /// itself is not a per-frame allocation.
    /// </summary>
    public sealed class QuestPerfSampler : MonoBehaviour
    {
        [SerializeField] private bool runInDevelopmentBuilds = true;

        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _triangles;
        private ProfilerRecorder _gc;
        private ProfilerRecorder _texture;
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly List<QuestPerfViolation> _violations = new List<QuestPerfViolation>(8);
        private readonly StringBuilder _report = new StringBuilder(256);
        private float _nextLogUnscaled;
        private bool _recordersStarted;

        private void OnEnable()
        {
            if (!QuestPerfBudget.ShouldSample(Debug.isDebugBuild, runInDevelopmentBuilds))
            {
                enabled = false;
                return;
            }

            _drawCalls = StartStat(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = StartStat(ProfilerCategory.Render, "Triangles Count");
            _gc = StartStat(ProfilerCategory.Memory, "GC Allocated In Frame");
            _texture = StartStat(ProfilerCategory.Memory, "Texture Memory");
            _recordersStarted = true;
            var budgets = QuestPerfBudgets.Default;
            Debug.Log("[QuestPerf] Development sampler on. Budgets: frame " + budgets.WorstFrameMs.ToString("0.0")
                + " ms floor / " + budgets.TargetFrameMs.ToString("0.0")
                + " ms target, cpu " + budgets.AppCpuMs.ToString("0")
                + " ms, gpu " + budgets.GpuMs.ToString("0")
                + " ms, draws " + budgets.MaxDrawCalls
                + ", tris " + budgets.MaxTriangles
                + ", gc " + budgets.MaxGcBytes + " B/frame.");
        }

        private void OnDisable()
        {
            if (!_recordersStarted) return;
            DisposeStat(ref _drawCalls);
            DisposeStat(ref _triangles);
            DisposeStat(ref _gc);
            DisposeStat(ref _texture);
            _recordersStarted = false;
        }

        private void Update()
        {
            if (!_recordersStarted) return;

            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings((uint)_timings.Length, _timings);
            QuestPerfSample sample = default;
            if (count > 0)
            {
                FrameTiming timing = _timings[0];
                sample = QuestPerfBudget.FromFrameTiming(true, timing.cpuFrameTime, timing.cpuMainThreadFrameTime, timing.gpuFrameTime);
            }

            if (TryRead(_drawCalls, out long draws))
            {
                sample.HasDrawCalls = true;
                sample.DrawCalls = draws;
            }
            if (TryRead(_triangles, out long tris))
            {
                sample.HasTriangles = true;
                sample.Triangles = tris;
            }
            if (TryRead(_gc, out long gcBytes))
            {
                sample.HasGc = true;
                sample.GcBytes = gcBytes;
            }
            if (TryRead(_texture, out long textureBytes))
            {
                sample.HasTexture = true;
                sample.TextureBytes = textureBytes;
            }

            _violations.Clear();
            QuestPerfBudget.Evaluate(sample, QuestPerfBudgets.Default, _violations);
            if (_violations.Count == 0) return;
            if (Time.unscaledTime < _nextLogUnscaled) return;
            _nextLogUnscaled = Time.unscaledTime + 1f;
            _report.Length = 0;
            QuestPerfBudget.AppendReport(_report, _violations);
            Debug.LogWarning(_report.ToString());
        }

        private static bool TryRead(ProfilerRecorder recorder, out long value)
        {
            value = 0;
            if (!recorder.Valid || recorder.Count <= 0) return false;
            value = recorder.LastValue;
            return true;
        }

        private static ProfilerRecorder StartStat(ProfilerCategory category, string stat)
        {
            try
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, stat);
                if (recorder.Valid) return recorder;
                recorder.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[QuestPerf] Profiler stat unavailable: " + stat + " (" + ex.Message + ")");
            }
            return default;
        }

        private static void DisposeStat(ref ProfilerRecorder recorder)
        {
            if (!recorder.Valid) return;
            recorder.Dispose();
            recorder = default;
        }
    }
}
