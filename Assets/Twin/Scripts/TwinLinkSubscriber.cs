using System;
using Newtonsoft.Json;
using OmniBot.VR.Control;
using OmniBot.VR.Core;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Display-only subscription. Topic names come from <see cref="TwinProfile.Topics"/>.
    /// This type never calls Advertise, Publish, or Unsubscribe: Unsubscribe would
    /// drop every other listener on the shared odometry and joint topics.
    /// A later <see cref="Detach"/> only retires this subscriber's callbacks.
    /// </summary>
    public sealed class TwinLinkSubscriber
    {
        int _generation;
        TwinAnimator _animator;
        Func<double> _clock = DefaultClock;

        public bool IsAttached { get; private set; }

        public void Attach(IRobotLink link, TwinProfile profile, TwinAnimator animator, Func<double> clock)
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));
            if (animator == null)
                throw new ArgumentNullException(nameof(animator));
            Detach();
            int generation = _generation;
            _animator = animator;
            _clock = clock ?? DefaultClock;
            TwinTopics topics = profile != null && profile.Topics != null ? profile.Topics : new TwinTopics();

            if (!string.IsNullOrEmpty(topics.Odom))
                link.Subscribe(topics.Odom, "nav_msgs/Odometry", json => OnOdometry(generation, json));
            if (!string.IsNullOrEmpty(topics.Tf))
                link.Subscribe(topics.Tf, "tf2_msgs/TFMessage", json => OnTf(generation, json));
            SubscribeAll(link, topics.JointStates, "sensor_msgs/JointState", json => OnJointStates(generation, json));
            SubscribeAll(link, topics.JointCommands, "sensor_msgs/JointState", json => OnJointCommands(generation, json));
            IsAttached = true;
        }

        public void Detach()
        {
            _generation++;
            _animator = null;
            IsAttached = false;
        }

        static void SubscribeAll(IRobotLink link, string[] topics, string type, Action<string> callback)
        {
            if (topics == null)
                return;
            for (int i = 0; i < topics.Length; i++)
            {
                if (!string.IsNullOrEmpty(topics[i]))
                    link.Subscribe(topics[i], type, callback);
            }
        }

        void OnOdometry(int generation, string json)
        {
            if (generation != _generation || _animator == null)
                return;
            try
            {
                OdometryMsg message = JsonConvert.DeserializeObject<OdometryMsg>(json);
                _animator.ApplyOdometry(message, _clock());
            }
            catch (Exception)
            {
                // A bad packet must not take down teleop. The last good pose stays.
            }
        }

        void OnTf(int generation, string json)
        {
            if (generation != _generation || _animator == null)
                return;
            try
            {
                TfMessage message = JsonConvert.DeserializeObject<TfMessage>(json);
                _animator.ApplyTf(message, _clock());
            }
            catch (Exception)
            {
            }
        }

        void OnJointStates(int generation, string json)
        {
            if (generation != _generation || _animator == null)
                return;
            try
            {
                JointStateMsg message = JsonConvert.DeserializeObject<JointStateMsg>(json);
                _animator.ApplyJointStates(message, _clock());
            }
            catch (Exception)
            {
            }
        }

        void OnJointCommands(int generation, string json)
        {
            if (generation != _generation || _animator == null)
                return;
            try
            {
                JointStateMsg message = JsonConvert.DeserializeObject<JointStateMsg>(json);
                _animator.ApplyJointCommands(message, _clock());
            }
            catch (Exception)
            {
            }
        }

        static double DefaultClock()
        {
            return 0d;
        }
    }
}
