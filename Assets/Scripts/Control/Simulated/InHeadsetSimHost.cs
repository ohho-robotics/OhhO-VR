using UnityEngine;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Scene hook for <see cref="RobotLinkTarget.InHeadset"/>. Implements
    /// <see cref="IRobotLink"/> so it can be assigned to
    /// <c>TeleopController.linkProvider</c> without another code change.
    ///
    /// Deadman is the left index trigger, or Space in Play Mode (the browser
    /// sim uses Space / pointer-hold). Grips stay on the drive scheme: right
    /// grip is turbo, both grips publish <c>/emergency_stop</c>.
    /// Runs after the default teleop update so a twist published this frame
    /// is integrated this frame once the deadman is already held.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class InHeadsetSimHost : MonoBehaviour, IRobotLink
    {
        [SerializeField] bool spawnPlaceholderRoom = true;

        SimulatedRobotLink _link;
        SimulatedTwinPlaceholder _room;
        RobotProfile _profile;

        public SimulatedRobotLink Sim => Ensure();

        public bool IsConnected => Ensure().IsConnected;

        void Awake() => Ensure();

        void Update() => SimulateFrame(Time.deltaTime, ReadDeadman());

        /// <summary>EditMode / tests advance the sim without reading controllers.</summary>
        public void SimulateFrame(float dt, bool deadmanHeld)
        {
            var link = Ensure();
            link.SetDeadman(deadmanHeld);
            link.Tick(dt);
            if (_room != null) _room.ApplyPose(link.Pose);
        }

        /// <summary>Swap in a garage profile. Replaces the running sim link.</summary>
        public void Configure(RobotProfile profile)
        {
            _profile = profile;
            _link = (SimulatedRobotLink)RobotLinkFactory.Select(RobotLinkTarget.InHeadset, profile);
        }

        public void Advertise(string topic, string type) => Ensure().Advertise(topic, type);

        public void Publish<T>(string topic, T msg) => Ensure().Publish(topic, msg);

        public void Subscribe(string topic, string type, System.Action<string> callback)
            => Ensure().Subscribe(topic, type, callback);

        public void Unsubscribe(string topic) => Ensure().Unsubscribe(topic);

        SimulatedRobotLink Ensure()
        {
            if (_link == null)
                _link = (SimulatedRobotLink)RobotLinkFactory.Select(RobotLinkTarget.InHeadset, _profile);
            if (spawnPlaceholderRoom && _room == null && !SimulatedTwinPlaceholder.IsUnityTwinPresent())
                _room = SimulatedTwinPlaceholder.BuildIfNeeded(transform);
            return _link;
        }

        static bool ReadDeadman()
        {
            bool trigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                           || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) > 0.8f;
            return trigger || SpaceHeld();
        }

        static bool SpaceHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.spaceKey.isPressed;
#else
            return false;
#endif
        }
    }
}
