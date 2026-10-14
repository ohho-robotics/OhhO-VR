using System;
using System.Collections.Generic;
using OmniBot.VR.Control;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// In-memory <see cref="IRobotLink"/> for edit-mode tests. Records advertises,
    /// publishes, and service calls, and can push a fake subscription payload.
    /// </summary>
    public sealed class FakeRobotLink : IRobotLink
    {
        public bool IsConnected { get; set; } = true;

        public readonly List<AdvertisedTopic> Advertised = new List<AdvertisedTopic>();
        public readonly List<PublishedMessage> Published = new List<PublishedMessage>();
        public readonly List<ServiceInvocation> ServiceCalls = new List<ServiceInvocation>();

        private readonly Dictionary<string, List<Action<string>>> _subscribers
            = new Dictionary<string, List<Action<string>>>();

        public struct AdvertisedTopic
        {
            public string Topic;
            public string Type;
        }

        public struct PublishedMessage
        {
            public string Topic;
            public object Msg;
        }

        public struct ServiceInvocation
        {
            public string Service;
            public string Type;
            public Action<bool, string> Callback;
        }

        public void Advertise(string topic, string type)
        {
            Advertised.Add(new AdvertisedTopic { Topic = topic, Type = type });
        }

        public void Publish<T>(string topic, T msg)
        {
            Published.Add(new PublishedMessage { Topic = topic, Msg = msg });
        }

        public void Subscribe(string topic, string type, Action<string> callback)
        {
            if (!_subscribers.TryGetValue(topic, out var list))
            {
                list = new List<Action<string>>();
                _subscribers[topic] = list;
            }
            list.Add(callback);
        }

        public void Unsubscribe(string topic)
        {
            _subscribers.Remove(topic);
        }

        public void CallService(string service, string type, Action<bool, string> onResponse)
        {
            ServiceCalls.Add(new ServiceInvocation
            {
                Service = service,
                Type = type,
                Callback = onResponse,
            });
        }

        public void Push(string topic, string json)
        {
            if (!_subscribers.TryGetValue(topic, out var list)) return;
            var copy = list.ToArray();
            for (int i = 0; i < copy.Length; i++)
                copy[i]?.Invoke(json);
        }
    }
}
