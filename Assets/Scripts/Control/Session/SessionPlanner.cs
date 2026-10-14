namespace OmniBot.VR.Control.Session
{
    /// <summary>What the picker is allowed to start after the operator's clicks.</summary>
    public sealed class SessionLaunch
    {
        public RobotEndpoint Endpoint;
        /// <summary>Always on. The picker has no way to clear the deadman.</summary>
        public bool DeadmanRequired => true;
        public bool ReadyToDrive;
        public bool ShowDeadmanTutorial;
        public string BlockReason;
    }

    /// <summary>
    /// The session currently being driven. Banners read <see cref="Current"/>.
    /// </summary>
    public static class ActiveSession
    {
        public static RobotEndpoint Current { get; private set; }

        public static void Set(RobotEndpoint endpoint)
        {
            Current = endpoint;
        }

        public static void Clear()
        {
            Current = null;
        }
    }

    /// <summary>
    /// Resolves a click on the session picker to an endpoint. Local desktop with an
    /// empty registry is one click: the prefilled manual IP (default 192.168.1.101:9090).
    /// Drive real never comes back ready until the safety notice is acknowledged,
    /// and the deadman flag on that launch cannot be false.
    /// </summary>
    public static class SessionPlanner
    {
        public static SessionLaunch LocalDesktop(string robotId, string alias, string ip, int port,
            SessionCatalog catalog, string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                var found = catalog != null ? catalog.Find(robotId, alias, sessionId, SessionKind.Sim) : null;
                if (found == null || found.Target != SessionTarget.LocalDesktop)
                    return Block("That local session is no longer listed.");
                return Ready(found);
            }

            if (!EndpointAddresses.TryManual(robotId, ip, port, SessionKind.Sim, SessionTarget.LocalDesktop,
                    out var endpoint, out var error))
                return Block(error);
            return Ready(endpoint);
        }

        public static SessionLaunch InHeadset(string robotId)
        {
            return Ready(EndpointAddresses.InHeadset(robotId));
        }

        public static SessionLaunch CloudStart(string robotId, SessionCatalog catalog)
        {
            if (catalog == null)
                return Block("Cloud orchestrator is not configured. Attach with a rosbridge URL, or use Local desktop.");
            if (!catalog.TryStartCloud(robotId, out var endpoint, out var error))
                return Block(string.IsNullOrEmpty(error)
                    ? "Cloud orchestrator is not configured. Attach with a rosbridge URL, or use Local desktop."
                    : error);
            return Ready(endpoint);
        }

        /// <summary>Launch a sim row already in the catalog, keeping its video and signal URLs.</summary>
        public static SessionLaunch LaunchListedSim(RobotEndpoint listed)
        {
            if (listed == null || listed.Kind != SessionKind.Sim)
                return Block("That session is no longer listed.");
            return Ready(listed);
        }

        public static SessionLaunch CloudAttach(string robotId, string rosbridgeUrl)
        {
            if (!EndpointAddresses.TryRosbridge(robotId, rosbridgeUrl, SessionKind.Sim, SessionTarget.Cloud,
                    out var endpoint, out var error))
                return Block(error);
            endpoint.Source = "manual";
            return Ready(endpoint);
        }

        public static SessionLaunch DriveReal(string robotId, string alias, string ip, int port,
            SessionCatalog catalog, string sessionId, bool safetyAcknowledged, bool tutorialSeen)
        {
            RobotEndpoint endpoint;
            if (!string.IsNullOrEmpty(sessionId))
            {
                endpoint = catalog != null ? catalog.Find(robotId, alias, sessionId, SessionKind.Real) : null;
                if (endpoint == null) return Block("That robot session is no longer listed.");
            }
            else if (!EndpointAddresses.TryManual(robotId, ip, port, SessionKind.Real, SessionTarget.ManualIp,
                         out endpoint, out var error))
            {
                return Block(error);
            }

            return FinishReal(endpoint, safetyAcknowledged, tutorialSeen);
        }

        /// <summary>
        /// A saved sim choice can launch immediately. A saved real choice still
        /// needs the safety notice; this method will not skip it.
        /// </summary>
        public static SessionLaunch FromSaved(RobotEndpoint saved, bool safetyAcknowledged, bool tutorialSeen)
        {
            if (saved == null) return Block("No saved session.");
            if (saved.Kind == SessionKind.Real)
                return FinishReal(saved, safetyAcknowledged, tutorialSeen);
            return Ready(saved);
        }

        private static SessionLaunch Ready(RobotEndpoint endpoint)
        {
            var copy = endpoint.Clone();
            if (copy.Target == SessionTarget.InHeadset)
            {
                copy.Kind = SessionKind.Sim;
                copy.Offline = true;
            }
            return new SessionLaunch
            {
                Endpoint = copy,
                ReadyToDrive = true,
            };
        }

        private static SessionLaunch FinishReal(RobotEndpoint endpoint, bool safetyAcknowledged, bool tutorialSeen)
        {
            var copy = endpoint.Clone();
            copy.Kind = SessionKind.Real;
            copy.Offline = false;
            return new SessionLaunch
            {
                Endpoint = copy,
                ShowDeadmanTutorial = !tutorialSeen,
                ReadyToDrive = safetyAcknowledged,
                BlockReason = safetyAcknowledged ? null : RealRobotSafety.SafetyBlock,
            };
        }

        private static SessionLaunch Block(string reason)
        {
            return new SessionLaunch
            {
                ReadyToDrive = false,
                BlockReason = string.IsNullOrEmpty(reason) ? "Can't start that session." : reason,
            };
        }
    }
}
