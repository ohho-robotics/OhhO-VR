using System;
using System.Collections.Generic;
using OmniBot.VR.Core;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// In-headset offline demo transport. It reports connected so teleop can tick,
    /// and it never opens a socket. Published twists stay on the headset.
    /// </summary>
    public sealed class OfflineKinematicLink : IRobotLink
    {
        public bool IsConnected { get; set; } = true;

        public TwistMsg LastTwist { get; private set; }

        private readonly Dictionary<string, List<Action<string>>> _subscribers =
            new Dictionary<string, List<Action<string>>>();

        public void Advertise(string topic, string type) { }

        public void Publish<T>(string topic, T msg)
        {
            if (msg is TwistMsg twist) LastTwist = twist;
        }

        public void Subscribe(string topic, string type, Action<string> callback)
        {
            if (string.IsNullOrEmpty(topic) || callback == null) return;
            if (!_subscribers.TryGetValue(topic, out var list))
            {
                list = new List<Action<string>>();
                _subscribers[topic] = list;
            }
            list.Add(callback);
        }

        public void Unsubscribe(string topic)
        {
            if (!string.IsNullOrEmpty(topic)) _subscribers.Remove(topic);
        }
    }
}
