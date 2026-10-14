using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using OmniBot.VR.Core;

namespace OmniBot.VR.Tests
{
    public class QuestPerfBudgetTests
    {
        [Test]
        public void ShouldSample_RequiresDevelopmentBuildAndToggle()
        {
            Assert.IsTrue(QuestPerfBudget.ShouldSample(true, true));
            Assert.IsFalse(QuestPerfBudget.ShouldSample(true, false));
            Assert.IsFalse(QuestPerfBudget.ShouldSample(false, true));
            Assert.IsFalse(QuestPerfBudget.ShouldSample(false, false));
        }

        [Test]
        public void GraphicsPlan_IsSustainedHighAndFixedMedium()
        {
            Assert.AreEqual(OVRManager.ProcessorPerformanceLevel.SustainedHigh, QuestGraphicsPlan.CpuLevel);
            Assert.AreEqual(OVRManager.ProcessorPerformanceLevel.SustainedHigh, QuestGraphicsPlan.GpuLevel);
            Assert.AreEqual(OVRManager.FoveatedRenderingLevel.Medium, QuestGraphicsPlan.Foveation);
            Assert.AreNotEqual(OVRManager.ProcessorPerformanceLevel.Boost, QuestGraphicsPlan.CpuLevel);
            Assert.AreNotEqual(OVRManager.ProcessorPerformanceLevel.Boost, QuestGraphicsPlan.GpuLevel);
        }

        [Test]
        public void ResolveFoveation_FixedLevelWins_LegacyBoolIsTheFallback()
        {
            Assert.AreEqual(
                OVRManager.FoveatedRenderingLevel.Medium,
                QuestGraphicsPlan.ResolveFoveation(true, OVRManager.FoveatedRenderingLevel.Medium, false));
            Assert.AreEqual(
                OVRManager.FoveatedRenderingLevel.High,
                QuestGraphicsPlan.ResolveFoveation(true, OVRManager.FoveatedRenderingLevel.High, true));
            Assert.AreEqual(
                OVRManager.FoveatedRenderingLevel.Off,
                QuestGraphicsPlan.ResolveFoveation(false, OVRManager.FoveatedRenderingLevel.Medium, false));
            Assert.AreEqual(
                OVRManager.FoveatedRenderingLevel.Low,
                QuestGraphicsPlan.ResolveFoveation(false, OVRManager.FoveatedRenderingLevel.Medium, true));
        }

        [Test]
        public void FromFrameTiming_IgnoresMissingAndZeroSamples()
        {
            QuestPerfSample missing = QuestPerfBudget.FromFrameTiming(false, 20, 20, 20);
            Assert.IsFalse(missing.HasFrame);
            Assert.IsFalse(missing.HasCpu);
            Assert.IsFalse(missing.HasGpu);

            QuestPerfSample zeros = QuestPerfBudget.FromFrameTiming(true, 0, 0, 0);
            Assert.IsFalse(zeros.HasFrame);

            QuestPerfSample cpuOnly = QuestPerfBudget.FromFrameTiming(true, 6, 5, 0);
            Assert.IsTrue(cpuOnly.HasCpu);
            Assert.IsFalse(cpuOnly.HasGpu);
            Assert.AreEqual(5f, cpuOnly.CpuMs, 0.001f);
            Assert.AreEqual(6f, cpuOnly.FrameMs, 0.001f);

            QuestPerfSample mainFallback = QuestPerfBudget.FromFrameTiming(true, 0, 7, 0);
            Assert.AreEqual(7f, mainFallback.FrameMs, 0.001f);
            Assert.AreEqual(7f, mainFallback.CpuMs, 0.001f);

            QuestPerfSample gpuSlower = QuestPerfBudget.FromFrameTiming(true, 4, 3, 10);
            Assert.IsTrue(gpuSlower.HasGpu);
            Assert.AreEqual(10f, gpuSlower.GpuMs, 0.001f);
            Assert.AreEqual(10f, gpuSlower.FrameMs, 0.001f);
        }

        [Test]
        public void Evaluate_InsideLimits_HasNoViolations()
        {
            var sample = new QuestPerfSample
            {
                HasFrame = true,
                FrameMs = 11.1f,
                HasCpu = true,
                CpuMs = 8f,
                HasGpu = true,
                GpuMs = 9f,
                HasDrawCalls = true,
                DrawCalls = 150,
                HasTriangles = true,
                Triangles = 300000,
                HasGc = true,
                GcBytes = 0,
                HasTexture = true,
                TextureBytes = 400L * 1024L * 1024L,
            };
            var found = Evaluate(sample);
            Assert.AreEqual(0, found.Count);
            Assert.AreEqual("[QuestPerf] within budget", Report(found));
        }

        [Test]
        public void Evaluate_UnsampledFields_AreNotViolations()
        {
            var sample = new QuestPerfSample
            {
                FrameMs = 100f,
                CpuMs = 100f,
                GpuMs = 100f,
                DrawCalls = 1000,
                Triangles = 1000000,
                GcBytes = 64,
                TextureBytes = long.MaxValue,
            };
            Assert.AreEqual(0, Evaluate(sample).Count);
        }

        [Test]
        public void Evaluate_OverFloor_IsOverBudget()
        {
            var sample = new QuestPerfSample
            {
                HasFrame = true,
                FrameMs = 13.81f,
                HasCpu = true,
                CpuMs = 8.01f,
                HasGpu = true,
                GpuMs = 9.01f,
                HasDrawCalls = true,
                DrawCalls = 151,
                HasTriangles = true,
                Triangles = 300001,
                HasGc = true,
                GcBytes = 1,
                HasTexture = true,
                TextureBytes = 400L * 1024L * 1024L + 1,
            };
            var found = Evaluate(sample);
            Assert.AreEqual(7, found.Count);
            Assert.AreEqual(QuestPerfSeverity.OverBudget, found[0].Severity);
            Assert.AreEqual("frameMs", found[0].Metric);
            string report = Report(found);
            StringAssert.Contains("over budget frameMs", report);
            StringAssert.Contains("over budget cpuMs", report);
            StringAssert.Contains("over budget gpuMs", report);
            StringAssert.Contains("over budget drawCalls", report);
            StringAssert.Contains("over budget triangles", report);
            StringAssert.Contains("over budget gcBytes", report);
            StringAssert.Contains("over budget textureBytes", report);
            StringAssert.DoesNotContain("missed target", report);
        }

        [Test]
        public void Evaluate_BetweenTargetAndFloor_IsMissedTarget()
        {
            var sample = new QuestPerfSample { HasFrame = true, FrameMs = 12f };
            var found = Evaluate(sample);
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(QuestPerfSeverity.MissedTarget, found[0].Severity);
            Assert.AreEqual(11.1f, found[0].Limit, 0.001f);
            Assert.AreEqual("[QuestPerf] missed target frameMs 12 > 11.1", Report(found));
        }

        [Test]
        public void Evaluate_FloorBoundary_MissesTargetWithoutExceedingFloor()
        {
            var sample = new QuestPerfSample { HasFrame = true, FrameMs = 13.8f };
            var found = Evaluate(sample);
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(QuestPerfSeverity.MissedTarget, found[0].Severity);
            Assert.AreEqual("frameMs", found[0].Metric);
            Assert.AreEqual(11.1f, found[0].Limit, 0.001f);
        }

        [Test]
        public void DefaultBudgets_MatchTheProposal()
        {
            QuestPerfBudgets budgets = QuestPerfBudgets.Default;
            Assert.AreEqual(13.8f, budgets.WorstFrameMs, 0.001f);
            Assert.AreEqual(11.1f, budgets.TargetFrameMs, 0.001f);
            Assert.AreEqual(8f, budgets.AppCpuMs, 0.001f);
            Assert.AreEqual(9f, budgets.GpuMs, 0.001f);
            Assert.AreEqual(150, budgets.MaxDrawCalls);
            Assert.AreEqual(300000, budgets.MaxTriangles);
            Assert.AreEqual(0, budgets.MaxGcBytes);
            Assert.AreEqual(400L * 1024L * 1024L, budgets.MaxTextureBytes);
        }

        private static List<QuestPerfViolation> Evaluate(QuestPerfSample sample)
        {
            var found = new List<QuestPerfViolation>();
            QuestPerfBudget.Evaluate(sample, QuestPerfBudgets.Default, found);
            return found;
        }

        private static string Report(List<QuestPerfViolation> found)
        {
            var sb = new StringBuilder();
            QuestPerfBudget.AppendReport(sb, found);
            return sb.ToString();
        }
    }
}
