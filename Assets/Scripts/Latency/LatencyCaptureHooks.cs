using System;
using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Latency
{
    /// <summary>
    /// Unity-side subscription for an optional command-accept echo.
    /// Called only while capture is already enabled. Subscribing does not
    /// publish a command and does not run unless logging was requested.
    /// </summary>
    public static class LatencyCaptureHooks
    {
        public static void EnsureCommandAckSubscription()
        {
            if (!LatencyCapture.IsEnabled) return;
            try
            {
                ROSBridgeClient ros = ROSBridgeClient.Instance;
                if (ros == null || !ros.IsConnected) return;
                ros.Subscribe(LatencyBudget.CommandAckTopic, "std_msgs/Header", Ignore);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Latency] command-ack subscribe failed: " + ex.Message);
            }
        }

        private static void Ignore(string json) { }
    }
}
