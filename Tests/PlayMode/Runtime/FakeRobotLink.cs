#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.PlayMode
{
    /// <summary>
    /// In-memory <see cref="IRobotLink"/> for play-mode tests. OHH-207's
    /// SimulatedRobotLink is not in this tree; this stand-in stays under Tests/
    /// and is compiled into the editor only, so it is absent from Quest player
    /// builds. It never opens a socket.
    /// </summary>
    public sealed class FakeRobotLink : MonoBehaviour, IRobotLink
    {
        public bool Connected = true;

        public bool IsConnected => Connected;

        public readonly List<Publication> Publications = new List<Publication>();
        public readonly List<Advertisement> Advertisements = new List<Advertisement>();

        readonly Dictionary<string, List<Action<string>>> _subscribers =
            new Dictionary<string, List<Action<string>>>();

        public struct Publication
        {
            public string Topic;
            public object Message;
        }

        public struct Advertisement
        {
            public string Topic;
            public string Type;
        }

        public void Advertise(string topic, string type)
        {
            Advertisements.Add(new Advertisement { Topic = topic, Type = type });
        }

        public void Publish<T>(string topic, T msg)
        {
            Publications.Add(new Publication { Topic = topic, Message = msg });
        }

        public void Subscribe(string topic, string type, Action<string> callback)
        {
            if (callback == null) return;
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

        public bool HasAdvertised(string topic, string type)
        {
            for (int i = 0; i < Advertisements.Count; i++)
            {
                var ad = Advertisements[i];
                if (ad.Topic == topic && ad.Type == type) return true;
            }
            return false;
        }

        public int CountBool(string topic, bool value)
        {
            int n = 0;
            for (int i = 0; i < Publications.Count; i++)
            {
                var p = Publications[i];
                if (p.Topic == topic && p.Message is BoolMsg msg && msg.data == value) n++;
            }
            return n;
        }

        public int CountNonZeroTwist(string topic)
        {
            int n = 0;
            for (int i = 0; i < Publications.Count; i++)
            {
                var p = Publications[i];
                if (p.Topic == topic && p.Message is TwistMsg twist && !IsZero(twist)) n++;
            }
            return n;
        }

        public bool HasString(string topic, string value)
        {
            for (int i = 0; i < Publications.Count; i++)
            {
                var p = Publications[i];
                if (p.Topic == topic && p.Message is StringMsg msg && msg.data == value) return true;
            }
            return false;
        }

        public TwistMsg LastTwist(string topic)
        {
            TwistMsg last = null;
            for (int i = 0; i < Publications.Count; i++)
            {
                var p = Publications[i];
                if (p.Topic == topic && p.Message is TwistMsg twist) last = twist;
            }
            return last;
        }

        public static bool IsZero(TwistMsg twist)
        {
            if (twist == null || twist.linear == null || twist.angular == null) return false;
            const float eps = 0.0001f;
            return Mathf.Abs(twist.linear.x) < eps && Mathf.Abs(twist.linear.y) < eps &&
                   Mathf.Abs(twist.linear.z) < eps && Mathf.Abs(twist.angular.x) < eps &&
                   Mathf.Abs(twist.angular.y) < eps && Mathf.Abs(twist.angular.z) < eps;
        }
    }
}
#endif
