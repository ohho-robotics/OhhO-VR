using System;
using System.Collections.Generic;
using OmniBot.VR.Core;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Display pose of one URDF. Pure C#: no MonoBehaviour, no transport, no
    /// publishing. Odometry / tf set the root pose. Joint states rotate or
    /// translate joints by URDF name. A second set of angles is the commanded
    /// ghost. Stale samples are held, not zeroed.
    /// </summary>
    public sealed class TwinAnimator
    {
        readonly UrdfDocument _document;
        readonly TwinProfile _profile;
        readonly Dictionary<string, UrdfJoint> _jointByName = new Dictionary<string, UrdfJoint>();
        readonly Dictionary<string, UrdfJoint> _jointByChild = new Dictionary<string, UrdfJoint>();
        readonly Dictionary<string, float> _measured = new Dictionary<string, float>();
        readonly Dictionary<string, float> _commanded = new Dictionary<string, float>();

        TwinPose _baseUnity = TwinPose.Identity;
        bool _hasBase;
        bool _baseHadStamp;
        double _lastBaseStamp = double.NegativeInfinity;
        double _baseReceived;
        bool _hasJoints;
        double _jointsReceived;
        bool _hasCommand;
        double _commandReceived;
        double _now;

        public TwinAnimator(UrdfDocument document, TwinProfile profile)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            _document = document;
            _profile = profile ?? TwinProfiles.OmniBot();
            StaleTimeoutSeconds = _profile.StaleTimeoutSeconds > 0f ? _profile.StaleTimeoutSeconds : 1f;
            SetScale(_profile.Scale);
            RootLink = FindRoot(document, _profile.BaseFrame);
            for (int i = 0; i < document.Joints.Count; i++)
            {
                UrdfJoint joint = document.Joints[i];
                if (!string.IsNullOrEmpty(joint.Name) && !_jointByName.ContainsKey(joint.Name))
                    _jointByName.Add(joint.Name, joint);
                if (!string.IsNullOrEmpty(joint.Child) && !_jointByChild.ContainsKey(joint.Child))
                    _jointByChild.Add(joint.Child, joint);
            }
        }

        public UrdfDocument Document => _document;
        public TwinProfile Profile => _profile;
        public string RootLink { get; }
        public TwinScaleMode Scale { get; private set; }
        public float ScaleFactor { get; private set; }
        public float StaleTimeoutSeconds { get; }

        public bool HasBaseSample => _hasBase;
        public bool HasJointSample => _hasJoints;
        public bool HasCommandSample => _hasCommand;

        public bool IsBaseStale => !_hasBase || _now - _baseReceived > StaleTimeoutSeconds;
        public bool AreJointsStale => !_hasJoints || _now - _jointsReceived > StaleTimeoutSeconds;
        public bool IsGhostStale => !_hasCommand || _now - _commandReceived > StaleTimeoutSeconds;

        /// <summary>Ghost is drawn only while a command sample is still fresh.</summary>
        public bool GhostVisible => _hasCommand && !IsGhostStale;

        public void SetScale(TwinScaleMode mode)
        {
            Scale = mode;
            ScaleFactor = TwinScale.Factor(mode);
        }

        public void Tick(double timeSeconds)
        {
            _now = timeSeconds;
        }

        public void ApplyOdometry(OdometryMsg message, double timeSeconds)
        {
            if (message == null || message.pose == null || message.pose.pose == null)
            {
                _now = timeSeconds;
                return;
            }
            if (!ChildMatches(message.child_frame_id))
            {
                _now = timeSeconds;
                return;
            }
            string parent = message.header != null ? message.header.frame_id : null;
            if (!ParentMatches(parent))
            {
                _now = timeSeconds;
                return;
            }
            AcceptBase(PoseFrom(message.pose.pose.position, message.pose.pose.orientation), Stamp(message.header), timeSeconds);
        }

        public void ApplyTf(TfMessage message, double timeSeconds)
        {
            _now = timeSeconds;
            if (message == null || message.transforms == null)
                return;
            for (int i = 0; i < message.transforms.Length; i++)
            {
                TfStamped stamped = message.transforms[i];
                if (stamped == null || stamped.transform == null)
                    continue;
                if (!ChildMatches(stamped.child_frame_id))
                    continue;
                string parent = stamped.header != null ? stamped.header.frame_id : null;
                if (!ParentMatches(parent))
                    continue;
                AcceptBase(PoseFrom(stamped.transform.translation, stamped.transform.rotation), Stamp(stamped.header), timeSeconds);
            }
        }

        public void ApplyJointStates(JointStateMsg message, double timeSeconds)
        {
            ApplyAngles(message, timeSeconds, _measured, ref _hasJoints, ref _jointsReceived);
        }

        public void ApplyJointCommands(JointStateMsg message, double timeSeconds)
        {
            ApplyAngles(message, timeSeconds, _commanded, ref _hasCommand, ref _commandReceived);
        }

        public float GetJointPosition(string urdfJointName, bool commanded)
        {
            float commandedValue;
            if (commanded && _commanded.TryGetValue(urdfJointName, out commandedValue))
                return commandedValue;
            float measured;
            return _measured.TryGetValue(urdfJointName, out measured) ? measured : 0f;
        }

        /// <summary>
        /// Local pose of a link in its parent, Unity metres, not yet multiplied
        /// by <see cref="ScaleFactor"/>. The root link's local pose is the base
        /// pose in the anchor frame.
        /// </summary>
        public bool TryGetLinkLocalPose(string linkName, bool commanded, out TwinPose pose)
        {
            if (linkName == RootLink)
            {
                pose = _hasBase ? _baseUnity : TwinPose.Identity;
                return true;
            }
            UrdfJoint joint;
            if (string.IsNullOrEmpty(linkName) || !_jointByChild.TryGetValue(linkName, out joint))
            {
                pose = TwinPose.Identity;
                return false;
            }
            pose = JointLocalPose(joint, ValueFor(joint, commanded));
            return true;
        }

        public TwinPose GetScaledLinkLocalPose(string linkName, bool commanded)
        {
            TwinPose pose;
            if (!TryGetLinkLocalPose(linkName, commanded, out pose))
                return TwinPose.Identity;
            return pose.Scaled(ScaleFactor);
        }

        float ValueFor(UrdfJoint joint, bool commanded)
        {
            float commandedValue;
            if (commanded && _commanded.TryGetValue(joint.Name, out commandedValue))
                return commandedValue;
            float measured;
            return _measured.TryGetValue(joint.Name, out measured) ? measured : 0f;
        }

        void ApplyAngles(JointStateMsg message, double timeSeconds, Dictionary<string, float> target, ref bool hasSample, ref double received)
        {
            _now = timeSeconds;
            if (message == null || message.name == null || message.position == null)
                return;
            int count = Math.Min(message.name.Length, message.position.Length);
            bool applied = false;
            for (int i = 0; i < count; i++)
            {
                string resolved = _profile.ResolveJointName(message.name[i]);
                UrdfJoint joint;
                if (string.IsNullOrEmpty(resolved) || !_jointByName.TryGetValue(resolved, out joint))
                    continue;
                if (!IsDriven(joint.Type))
                    continue;
                target[joint.Name] = Clamp(joint, message.position[i]);
                applied = true;
            }
            if (!applied)
                return;
            hasSample = true;
            received = timeSeconds;
        }

        static bool IsDriven(UrdfJointType type)
        {
            return type == UrdfJointType.Revolute
                || type == UrdfJointType.Continuous
                || type == UrdfJointType.Prismatic;
        }

        static float Clamp(UrdfJoint joint, float value)
        {
            if (!joint.HasLimits)
                return value;
            if (joint.Type != UrdfJointType.Revolute && joint.Type != UrdfJointType.Prismatic)
                return value;
            if (value < joint.Lower)
                return joint.Lower;
            if (value > joint.Upper)
                return joint.Upper;
            return value;
        }

        public static TwinPose JointLocalPose(UrdfJoint joint, float value)
        {
            RosTransform origin = TwinMath.FromRpy(
                joint.OriginX, joint.OriginY, joint.OriginZ, joint.Roll, joint.Pitch, joint.Yaw);
            RosTransform motion = RosTransform.Identity;
            if (joint.Type == UrdfJointType.Revolute || joint.Type == UrdfJointType.Continuous)
                motion = TwinMath.FromAxisAngle(joint.AxisX, joint.AxisY, joint.AxisZ, value);
            else if (joint.Type == UrdfJointType.Prismatic)
                motion = TwinMath.Translation(joint.AxisX * value, joint.AxisY * value, joint.AxisZ * value);
            return TwinMath.ToUnity(TwinMath.Compose(origin, motion));
        }

        void AcceptBase(TwinPose pose, double stampSeconds, double timeSeconds)
        {
            _now = timeSeconds;
            bool hasStamp = stampSeconds > 0d;
            // Only drop a sample when both sides carry a ROS stamp and the new one is older.
            // A missing stamp follows socket order so a live pose is not stuck behind a clock.
            if (_hasBase && hasStamp && _baseHadStamp && stampSeconds + 1e-9 < _lastBaseStamp)
                return;
            if (hasStamp)
            {
                _lastBaseStamp = stampSeconds;
                _baseHadStamp = true;
            }
            _baseUnity = pose;
            _baseReceived = timeSeconds;
            _hasBase = true;
        }

        bool ChildMatches(string childFrame)
        {
            if (string.IsNullOrEmpty(childFrame) || string.IsNullOrEmpty(_profile.BaseFrame))
                return true;
            return childFrame == _profile.BaseFrame;
        }

        bool ParentMatches(string parentFrame)
        {
            if (string.IsNullOrEmpty(parentFrame) || string.IsNullOrEmpty(_profile.OdomFrame))
                return true;
            return parentFrame == _profile.OdomFrame;
        }

        static TwinPose PoseFrom(Vector3Msg position, QuaternionMsg orientation)
        {
            float x = position != null ? position.x : 0f;
            float y = position != null ? position.y : 0f;
            float z = position != null ? position.z : 0f;
            float qx = orientation != null ? orientation.x : 0f;
            float qy = orientation != null ? orientation.y : 0f;
            float qz = orientation != null ? orientation.z : 0f;
            float qw = orientation != null ? orientation.w : 1f;
            return TwinMath.ToUnity(new RosTransform(x, y, z, qx, qy, qz, qw));
        }

        static double Stamp(HeaderMsg header)
        {
            if (header == null || header.stamp == null)
                return 0d;
            return header.stamp.secs + header.stamp.nsecs * 1e-9;
        }

        static string FindRoot(UrdfDocument document, string preferred)
        {
            var children = new HashSet<string>();
            for (int i = 0; i < document.Joints.Count; i++)
            {
                if (!string.IsNullOrEmpty(document.Joints[i].Child))
                    children.Add(document.Joints[i].Child);
            }
            string first = null;
            for (int i = 0; i < document.Links.Count; i++)
            {
                string name = document.Links[i].Name;
                if (children.Contains(name))
                    continue;
                if (first == null)
                    first = name;
                if (!string.IsNullOrEmpty(preferred) && name == preferred)
                    return name;
            }
            return first ?? preferred ?? "";
        }
    }
}
