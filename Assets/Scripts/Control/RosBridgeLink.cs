using System;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// <see cref="IRobotLink"/> backed by the existing
    /// <see cref="ROSBridgeClient"/> WebSocket singleton. The schemes never touch
    /// <c>ROSBridgeClient</c> directly — they go through this link so the robot
    /// transport is swappable and testable. Delegates every call straight through.
    /// </summary>
    public sealed class RosBridgeLink : IRobotLink
    {
        public static readonly RosBridgeLink Instance = new RosBridgeLink();

        public bool IsConnected => ROSBridgeClient.Instance != null && ROSBridgeClient.Instance.IsConnected;

        private RosBridgeLink() { }

        public void Advertise(string topic, string type)
            => ROSBridgeClient.Instance.Advertise(topic, type);

        public void Publish<T>(string topic, T msg)
            => ROSBridgeClient.Instance.Publish(topic, msg);

        public void Subscribe(string topic, string type, Action<string> callback)
            => ROSBridgeClient.Instance.Subscribe(topic, type, callback);

        public void Unsubscribe(string topic)
            => ROSBridgeClient.Instance.Unsubscribe(topic);
    }
}
