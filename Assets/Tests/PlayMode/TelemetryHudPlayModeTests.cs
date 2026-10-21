using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using OmniBot.VR.Telemetry;

namespace OmniBot.VR.Tests
{
    public sealed class HudFrameProbe : MonoBehaviour
    {
        public TelemetryHudComposer Composer;
        public int Frames;
        public int Refreshes;

        void Update()
        {
            Frames++;
            if (Composer != null && Composer.ConsumeFrame(Time.unscaledDeltaTime, Time.realtimeSinceStartupAsDouble))
                Refreshes++;
        }
    }

    public class TelemetryHudPlayModeTests
    {
        [Test]
        public void ExpandedHud_ShowsEveryTelemetryBlock()
        {
            TelemetryHudComposer hud = new TelemetryHudComposer();
            hud.Echo = new CommandEcho();
            Assert.IsTrue(hud.IngestTelemetry(TelemetrySamples.Full, 10));
            hud.Expanded = true;
            hud.Write(10);
            string text = hud.Text;
            Assert.That(text, Does.Contain("omnibot-sim"));
            Assert.That(text, Does.Contain("BASE"));
            Assert.That(text, Does.Contain("JOINTS"));
            Assert.That(text, Does.Contain("GRIPPER"));
            Assert.That(text, Does.Contain("IMU"));
            Assert.That(text, Does.Contain("BATT"));
            Assert.That(text, Does.Contain("ESTOP"));
            Assert.That(text, Does.Contain("DEADMAN"));
            Assert.That(text, Does.Contain("LINK"));
            Assert.That(text, Does.Contain("NAV"));
            Assert.That(text, Does.Contain("POLICY"));
            Assert.That(text, Does.Contain("REC"));
            Assert.That(text, Does.Contain("CPU"));
            Assert.That(text, Does.Contain("VIDEO"));
            Assert.That(text, Does.Contain("SENT"));
        }

        [UnityTest]
        public IEnumerator PlayModeFrames_RefreshNearTenHertz()
        {
            TelemetryHudComposer hud = new TelemetryHudComposer();
            hud.Echo = new CommandEcho();
            Assert.IsTrue(hud.IngestTelemetry(TelemetrySamples.Full, 1));
            GameObject go = new GameObject("hud-v2-probe");
            HudFrameProbe probe = go.AddComponent<HudFrameProbe>();
            probe.Composer = hud;

            float elapsed = 0f;
            while (elapsed < 0.55f)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            Assert.GreaterOrEqual(probe.Refreshes, 3);
            Assert.LessOrEqual(probe.Refreshes, 8);
            Assert.GreaterOrEqual(probe.Frames, 1);
            Assert.That(hud.Text, Does.Contain("omnibot-sim"));
            Object.Destroy(go);
        }
    }
}
