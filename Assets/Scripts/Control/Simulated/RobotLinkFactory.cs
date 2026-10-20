namespace OmniBot.VR.Control
{
    /// <summary>
    /// Where teleop sends commands. <see cref="InHeadset"/> needs no robot PC.
    /// </summary>
    public enum RobotLinkTarget
    {
        /// <summary>Live ROSBridge WebSocket (<see cref="RosBridgeLink"/>).</summary>
        RosBridge = 0,

        /// <summary>Offline kinematic sim inside the headset (<see cref="SimulatedRobotLink"/>).</summary>
        InHeadset = 1,
    }

    /// <summary>
    /// Selects the robot transport. The garage session picker (OHH-212) lands
    /// separately. Hook it up with one line when the operator picks
    /// Garage → OmniBot → Simulate → In-headset:
    /// <code>link = RobotLinkFactory.Select(RobotLinkTarget.InHeadset);</code>
    /// Or assign an <see cref="InHeadsetSimHost"/> to
    /// <c>TeleopController.linkProvider</c> (that field already accepts any
    /// <see cref="IRobotLink"/>).
    /// </summary>
    public static class RobotLinkFactory
    {
        public static RobotLinkTarget Selected { get; private set; } = RobotLinkTarget.RosBridge;

        public static IRobotLink Select(RobotLinkTarget target, RobotProfile profile = null)
        {
            Selected = target;
            return Create(target, profile);
        }

        public static IRobotLink Create(RobotLinkTarget target, RobotProfile profile = null)
        {
            if (target == RobotLinkTarget.InHeadset)
            {
                var link = new SimulatedRobotLink(profile);
                link.Connect();
                return link;
            }
            return RosBridgeLink.Instance;
        }
    }
}
