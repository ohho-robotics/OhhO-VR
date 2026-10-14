using System;
using System.Collections.Generic;

namespace OmniBot.VR.Control.Session
{
    /// <summary>
    /// Sessions the picker can offer. The shared catalog starts empty so manual IP
    /// works before the Supabase <c>robot_sessions</c> registry or a LAN announcer
    /// is running. Remote rows replace only the <c>registry</c> source.
    /// </summary>
    public sealed class SessionCatalog
    {
        public static readonly SessionCatalog Shared = new SessionCatalog();

        private readonly List<RobotEndpoint> _items = new List<RobotEndpoint>();

        /// <summary>
        /// Optional cloud orchestrator. Null means start-cloud is unavailable and
        /// the picker tells the operator to attach a URL or use Local desktop.
        /// </summary>
        public Func<string, RobotEndpoint> CloudStarter { get; set; }

        public void Clear()
        {
            _items.Clear();
            CloudStarter = null;
        }

        public void Add(RobotEndpoint endpoint)
        {
            if (endpoint != null) _items.Add(endpoint);
        }

        public void ReplaceRemote(IEnumerable<RobotEndpoint> rows)
        {
            _items.RemoveAll(item => item != null && item.Source == "registry");
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row == null) continue;
                row.Source = "registry";
                _items.Add(row);
            }
        }

        public static void ResetSharedForTests()
        {
            Shared.Clear();
        }

        public IReadOnlyList<RobotEndpoint> ListLocalSim(string robotId, string alias)
        {
            return List(robotId, alias, SessionKind.Sim, SessionTarget.LocalDesktop);
        }

        public IReadOnlyList<RobotEndpoint> ListCloud(string robotId, string alias)
        {
            return List(robotId, alias, SessionKind.Sim, SessionTarget.Cloud);
        }

        public IReadOnlyList<RobotEndpoint> ListReal(string robotId, string alias)
        {
            var list = new List<RobotEndpoint>();
            foreach (var item in _items)
            {
                if (item == null || item.Kind != SessionKind.Real || item.Offline) continue;
                if (!Matches(item, robotId, alias)) continue;
                list.Add(item);
            }
            return list;
        }

        public RobotEndpoint Find(string robotId, string alias, string sessionId, SessionKind kind)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            foreach (var item in _items)
            {
                if (item == null || item.SessionId != sessionId || item.Kind != kind) continue;
                if (!Matches(item, robotId, alias)) continue;
                return item;
            }
            return null;
        }

        public bool TryStartCloud(string robotId, out RobotEndpoint endpoint, out string error)
        {
            endpoint = null;
            if (CloudStarter == null)
            {
                error = "Cloud orchestrator is not configured. Attach with a rosbridge URL, or use Local desktop.";
                return false;
            }

            try { endpoint = CloudStarter(robotId); }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.RosbridgeUrl))
            {
                endpoint = null;
                error = "Cloud orchestrator did not return a rosbridge URL.";
                return false;
            }

            if (endpoint.Kind == SessionKind.Real)
            {
                endpoint = null;
                error = "That cloud target is a real robot. Use Drive real.";
                return false;
            }

            endpoint.Kind = SessionKind.Sim;
            endpoint.Target = SessionTarget.Cloud;
            endpoint.Offline = false;
            if (string.IsNullOrEmpty(endpoint.RobotId)) endpoint.RobotId = robotId;
            if (string.IsNullOrEmpty(endpoint.Source)) endpoint.Source = "cloud";
            error = null;
            return true;
        }

        private List<RobotEndpoint> List(string robotId, string alias, SessionKind kind, SessionTarget target)
        {
            var list = new List<RobotEndpoint>();
            foreach (var item in _items)
            {
                if (item == null || item.Kind != kind || item.Target != target) continue;
                if (!Matches(item, robotId, alias)) continue;
                list.Add(item);
            }
            return list;
        }

        public static bool Matches(RobotEndpoint endpoint, string robotId, string alias)
        {
            if (endpoint == null) return false;
            if (string.IsNullOrEmpty(endpoint.RobotId)) return true;
            if (!string.IsNullOrEmpty(robotId) && endpoint.RobotId == robotId) return true;
            if (!string.IsNullOrEmpty(alias) && endpoint.RobotId == alias) return true;
            return string.IsNullOrEmpty(robotId) && string.IsNullOrEmpty(alias);
        }
    }
}
