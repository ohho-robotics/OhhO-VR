using UnityEngine;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Session-origin anchor. The first lock places it at the tracking origin.
    /// Later motion of that anchor is a tracking correction, not robot motion.
    /// </summary>
    public interface ISessionOriginAnchor
    {
        bool IsEstablished { get; }
        bool TryEstablish(Pose originInWorld);
        bool TryGetPose(out Pose pose);
        void Release();
    }

    public sealed class MemorySessionOriginAnchor : ISessionOriginAnchor
    {
        public bool IsEstablished { get; private set; }
        public Pose Pose { get; private set; }

        public bool TryEstablish(Pose originInWorld)
        {
            Pose = originInWorld;
            IsEstablished = true;
            return true;
        }

        public bool TryGetPose(out Pose pose)
        {
            pose = Pose;
            return IsEstablished;
        }

        public void Release()
        {
        }
    }

    public static class SessionOriginAnchors
    {
        public static ISessionOriginAnchor CreateForThisRuntime()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new OvrSessionOriginAnchor();
#else
            return new MemorySessionOriginAnchor();
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Quest anchor. Compiled only into the Android player so editor tests never
    /// call <c>OVRPlugin.CreateSpatialAnchor</c>.
    /// </summary>
    public sealed class OvrSessionOriginAnchor : ISessionOriginAnchor
    {
        private GameObject _root;

        public bool IsEstablished { get; private set; }

        public bool TryEstablish(Pose originInWorld)
        {
            if (IsEstablished)
            {
                return true;
            }

            _root = new GameObject("OhhoSessionOriginAnchor");
            _root.transform.SetPositionAndRotation(originInWorld.position, originInWorld.rotation);
            _root.AddComponent<OVRSpatialAnchor>();
            IsEstablished = true;
            return true;
        }

        public bool TryGetPose(out Pose pose)
        {
            if (_root == null)
            {
                pose = default;
                return false;
            }

            pose = new Pose(_root.transform.position, _root.transform.rotation);
            return true;
        }

        public void Release()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            IsEstablished = false;
        }
    }
#endif
}
