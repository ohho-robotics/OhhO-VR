using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OmniBot.VR.PlayModeTests
{
    /// <summary>
    /// Editor play-mode check of ROSBridgeClient against a disposable rosbridge.
    /// When OHHO_ROSBRIDGE_URL is unset the test returns without opening a socket.
    /// The url must be the CI broker or another broker with no robot subscribed.
    /// The harness publishes only /ohho/ci_smoke.
    /// </summary>
    [PrebuildSetup("OmniBot.VR.PlayModeTests.EmptySceneSetup")]
    [PostBuildCleanup("OmniBot.VR.PlayModeTests.EmptySceneSetup")]
    public class RosBridgeDockerPlayModeTests
    {
        [UnityTest]
        public IEnumerator RosBridgeClient_RoundTrip_WhenBrokerUrlSet()
        {
            string url = Environment.GetEnvironmentVariable("OHHO_ROSBRIDGE_URL");
            if (string.IsNullOrEmpty(url))
            {
                Debug.Log(
                    "OHHO_ROSBRIDGE_URL is unset, so ROSBridgeClient does not open a socket. " +
                    "TODO(OHH-230): bash Tests/PlayMode/stage-into-assets.sh && " +
                    "OHHO_ROSBRIDGE_URL=ws://127.0.0.1:9090 unity -batchmode -nographics " +
                    "-projectPath <clone> -runTests -testPlatform playmode " +
                    "-testFilter OmniBot.VR.PlayModeTests.RosBridgeDockerPlayModeTests " +
                    "-testResults <xml> -logFile <log>. " +
                    "Point the url only at Tests/PlayMode/docker/compose.yml.");
                yield break;
            }

            var type = OperatorFlowPlayModeTests.FindType("OmniBot.VR.PlayMode.RosBridgeDockerHarness");
            Assert.IsNotNull(type, "Stage Tests/PlayMode into Assets before running play-mode tests.");
            var method = type.GetMethod("RoundTrip", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "RoundTrip");
            var iter = (IEnumerator)method.Invoke(null, new object[] { url });
            try
            {
                while (iter.MoveNext())
                    yield return iter.Current;
            }
            finally
            {
                (iter as IDisposable)?.Dispose();
            }

            var result = type.GetProperty("Result", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            string detail = OperatorFlowPlayModeTests.Field<string>(result, "Detail");
            Assert.IsTrue(OperatorFlowPlayModeTests.Field<bool>(result, "Connected"), detail);
            Assert.IsTrue(OperatorFlowPlayModeTests.Field<bool>(result, "ReceivedPing"), detail);
        }
    }
}
