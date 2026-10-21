#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.PlayMode
{
    /// <summary>
    /// Editor play-mode round-trip of <see cref="ROSBridgeClient"/> against a
    /// rosbridge WebSocket. Publishes only on <c>/ohho/ci_smoke</c>. It does not
    /// publish <c>/cmd_vel/teleop</c> or <c>/emergency_stop</c>.
    /// </summary>
    public static class RosBridgeDockerHarness
    {
        public const string Topic = "/ohho/ci_smoke";

        public static BridgeRoundTripResult Result { get; private set; }

        public static IEnumerator RoundTrip(string url)
        {
            Result = new BridgeRoundTripResult();
            var log = new StringBuilder();
            ROSBridgeClient client = null;
            bool sawPing = false;
            try
            {
                client = ROSBridgeClient.Instance;
                client.Connect(url);
                float start = Time.realtimeSinceStartup;
                while (!client.IsConnected && Time.realtimeSinceStartup - start < 8f)
                    yield return null;

                Result.Connected = client.IsConnected;
                log.Append("connected=").Append(Result.Connected).Append("; ");
                if (!client.IsConnected)
                {
                    Result.Detail = log.Append("timed out waiting for ").Append(url).ToString();
                    yield break;
                }

                client.Subscribe(Topic, "std_msgs/String", json =>
                {
                    if (!string.IsNullOrEmpty(json) && json.Contains("ping")) sawPing = true;
                });
                client.Advertise(Topic, "std_msgs/String");
                client.Publish(Topic, new StringMsg("ping"));

                start = Time.realtimeSinceStartup;
                while (!sawPing && Time.realtimeSinceStartup - start < 8f)
                    yield return null;

                Result.ReceivedPing = sawPing;
                log.Append("ping=").Append(sawPing);
                Result.Detail = log.ToString();
            }
            finally
            {
                Result.Detail = log.ToString();
                if (client != null)
                {
                    client.Disconnect();
                    UnityEngine.Object.DestroyImmediate(client.gameObject);
                }
            }
        }
    }

    public sealed class BridgeRoundTripResult
    {
        public bool Connected;
        public bool ReceivedPing;
        public string Detail = "";
    }
}
#endif
