using System;
using System.Collections.Generic;
using System.Globalization;
using OmniBot.VR.Control;

namespace OmniBot.VR.Latency
{
    /// <summary>
    /// In-memory <see cref="IRobotLink"/> for the latency harness. It records
    /// publishes unchanged, then delivers scripted odometry and heartbeats when
    /// <see cref="Pump"/> is called. It does not open a socket.
    /// </summary>
    public sealed class FakeRobotLink : IRobotLink
    {
        public readonly List<PublishedMessage> Published = new List<PublishedMessage>();
        public long MotionDelayMs = 40;
        public long TelemetryLagMs = 15;
        public long HeartbeatLagMs = 10;
        public bool AutoHeartbeat = true;
        public string LastDeliveredJson { get; private set; }

        private readonly LatencySession _session;
        private readonly ManualLatencyClock _clock;
        private readonly Dictionary<string, List<Action<string>>> _subscribers
            = new Dictionary<string, List<Action<string>>>();
        private long? _motionDueMs;

        public FakeRobotLink(LatencySession session, ManualLatencyClock clock)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool IsConnected { get; private set; }

        public void Connect()
        {
            IsConnected = true;
        }

        public void Advertise(string topic, string type) { }

        public void Publish<T>(string topic, T msg)
        {
            if (!IsConnected) return;
            string json = msg as string;
            if (json == null)
                throw new ArgumentException("FakeRobotLink.Publish expects the ROS message JSON.");
            // Store the caller's JSON as-is. The harness must not rewrite it.
            Published.Add(new PublishedMessage(topic, json));
            _session.ObserveOutgoing(topic, json);
            if (LatencyJson.IsMotionCommandTopic(topic) && LatencyJson.CommandHasMotion(json))
                _motionDueMs = _clock.NowMs + MotionDelayMs;
        }

        public void Subscribe(string topic, string type, Action<string> callback)
        {
            List<Action<string>> list;
            if (!_subscribers.TryGetValue(topic, out list))
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

        public void Inject(string topic, string json)
        {
            Deliver(topic, json);
        }

        public int Pump()
        {
            int delivered = 0;
            if (_motionDueMs.HasValue && _clock.NowMs >= _motionDueMs.Value)
            {
                long stamp = _clock.NowMs - TelemetryLagMs;
                Deliver("/odom", OdomJson(stamp, 0.1));
                _motionDueMs = null;
                delivered++;
            }
            if (AutoHeartbeat)
            {
                long stamp = _clock.NowMs - HeartbeatLagMs;
                Deliver("/diagnostics", HeartbeatJson(stamp));
                delivered++;
            }
            return delivered;
        }

        public static string StampJson(long unixMs)
        {
            if (unixMs < 0) unixMs = 0;
            long secs = unixMs / 1000L;
            long nsecs = (unixMs % 1000L) * 1000000L;
            return "{\"secs\":" + secs.ToString(CultureInfo.InvariantCulture)
                + ",\"nsecs\":" + nsecs.ToString(CultureInfo.InvariantCulture) + "}";
        }

        public static string OdomJson(long stampUnixMs, double linearX)
        {
            string x = linearX.ToString(CultureInfo.InvariantCulture);
            return "{\"header\":{\"stamp\":" + StampJson(stampUnixMs)
                + ",\"frame_id\":\"odom\"},\"pose\":{\"pose\":{\"position\":{\"x\":2,\"y\":0,\"z\":0}},\"covariance\":[1,0]},\"twist\":{\"twist\":{\"linear\":{\"x\":"
                + x + ",\"y\":0,\"z\":0},\"angular\":{\"x\":0,\"y\":0,\"z\":0}},\"covariance\":[0.5]}}";
        }

        public static string HeartbeatJson(long stampUnixMs)
        {
            return "{\"header\":{\"stamp\":" + StampJson(stampUnixMs)
                + "},\"status\":[{\"name\":\"heartbeat\",\"message\":\"ok\"}]}";
        }

        private void Deliver(string topic, string json)
        {
            LastDeliveredJson = json;
            _session.ObserveIncoming(topic, json);
            List<Action<string>> list;
            if (!_subscribers.TryGetValue(topic, out list)) return;
            for (int i = 0; i < list.Count; i++)
                list[i](json);
        }
    }

    public sealed class PublishedMessage
    {
        public readonly string Topic;
        public readonly string Json;

        public PublishedMessage(string topic, string json)
        {
            Topic = topic;
            Json = json;
        }
    }
}
