using System;
using OmniBot.VR.Core;

namespace OmniBot.VR.Nav
{
    /// <summary>
    /// Outbound rosbridge calls the Nav2 session needs beyond topic publish:
    /// action goals and service calls. Schemes keep using <see cref="OmniBot.VR.Control.IRobotLink"/>.
    /// </summary>
    public interface INavTransport
    {
        bool IsConnected { get; }
        void SendRaw(string json);
        void Publish<T>(string topic, T message);
        void Advertise(string topic, string type);
        void Subscribe(string topic, string type, Action<string> callback);
    }

    public sealed class RosbridgeNavTransport : INavTransport
    {
        public static readonly RosbridgeNavTransport Instance = new RosbridgeNavTransport();

        private RosbridgeNavTransport() { }

        public bool IsConnected =>
            ROSBridgeClient.Instance != null && ROSBridgeClient.Instance.IsConnected;

        public void SendRaw(string json) => ROSBridgeClient.Instance.SendRaw(json);

        public void Publish<T>(string topic, T message) =>
            ROSBridgeClient.Instance.Publish(topic, message);

        public void Advertise(string topic, string type) =>
            ROSBridgeClient.Instance.Advertise(topic, type);

        public void Subscribe(string topic, string type, Action<string> callback) =>
            ROSBridgeClient.Instance.Subscribe(topic, type, callback);
    }
}
