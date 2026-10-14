using UnityEngine;

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Persists the last-selected robot id + connection details so a returning
    /// user can quick-resume teleoperation without re-browsing the garage. The
    /// "account link" polish: the headset remembers which robot you drove last
    /// and offers a one-tap resume on the next session.
    ///
    /// Stored in <c>PlayerPrefs</c> as plain keys (no secrets). The robot id is
    /// the catalog <c>hardware_model_id</c>; the IP/port are the ROSBridge
    /// endpoint the user was connected to.
    /// </summary>
    public static class LastRobotStore
    {
        private const string KeyRobotId = "ohho.last_robot_id";
        private const string KeyRobotName = "ohho.last_robot_name";
        private const string KeyRobotIp = "ohho.last_robot_ip";
        private const string KeyRobotPort = "ohho.last_robot_port";
        private const string KeyHasLast = "ohho.has_last_robot";
        private const string KeyKind = "ohho.last_session_kind";
        private const string KeyAlias = "ohho.last_robot_alias";

        /// <summary>True if a previous teleop session was recorded.</summary>
        public static bool HasLastRobot => PlayerPrefs.GetInt(KeyHasLast, 0) == 1;

        /// <summary>The hardware model id of the last-driven robot, or null.</summary>
        public static string LastRobotId => PlayerPrefs.GetString(KeyRobotId, null);

        /// <summary>The display name of the last-driven robot, or null.</summary>
        public static string LastRobotName => PlayerPrefs.GetString(KeyRobotName, null);

        /// <summary>The robot IP from the last session.</summary>
        public static string LastRobotIp => PlayerPrefs.GetString(KeyRobotIp, "192.168.1.101");

        /// <summary>The ROSBridge port from the last session.</summary>
        public static int LastRobotPort => PlayerPrefs.GetInt(KeyRobotPort, 9090);

        /// <summary>True after a session choice (sim or real) was saved for the last robot.</summary>
        public static bool HasSessionKind => PlayerPrefs.HasKey(KeyKind);

        /// <summary>Sim unless the last confirmed session was a real robot.</summary>
        public static Session.SessionKind LastSessionKind =>
            PlayerPrefs.GetString(KeyKind, "sim") == "real"
                ? Session.SessionKind.Real
                : Session.SessionKind.Sim;

        /// <summary>
        /// Record the robot the user just started teleoperating, so the next
        /// session can offer a quick-resume. When <paramref name="endpoint"/> is
        /// set, the sim/real choice is stored too.
        /// </summary>
        public static void Save(string robotId, string robotName, string ip, int port,
            Session.RobotEndpoint endpoint = null)
        {
            PlayerPrefs.SetString(KeyRobotId, robotId ?? "");
            PlayerPrefs.SetString(KeyRobotName, robotName ?? "");
            PlayerPrefs.SetString(KeyRobotIp, string.IsNullOrEmpty(ip) ? "192.168.1.101" : ip);
            PlayerPrefs.SetInt(KeyRobotPort, port);
            PlayerPrefs.SetInt(KeyHasLast, 1);
            if (endpoint != null)
            {
                PlayerPrefs.SetString(KeyKind, endpoint.Kind == Session.SessionKind.Real ? "real" : "sim");
                SaveChoice(robotId, endpoint);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Remember a second id (the user-robot row) that shares this choice.</summary>
        public static void RememberAlias(string aliasRobotId)
        {
            if (string.IsNullOrEmpty(aliasRobotId))
                PlayerPrefs.DeleteKey(KeyAlias);
            else
                PlayerPrefs.SetString(KeyAlias, aliasRobotId);
            PlayerPrefs.Save();
        }

        /// <summary>Persist simulate / drive-real and the endpoint for one robot.</summary>
        public static void SaveChoice(string robotId, Session.RobotEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(robotId) || endpoint == null) return;
            string prefix = Prefix(robotId);
            PlayerPrefs.SetInt(prefix + "has", 1);
            PlayerPrefs.SetString(prefix + "kind", endpoint.Kind == Session.SessionKind.Real ? "real" : "sim");
            PlayerPrefs.SetString(prefix + "target", endpoint.Target.ToString());
            PlayerPrefs.SetString(prefix + "url", endpoint.RosbridgeUrl ?? "");
            PlayerPrefs.SetString(prefix + "host", endpoint.Host ?? "");
            PlayerPrefs.SetInt(prefix + "port", endpoint.Port);
            PlayerPrefs.SetString(prefix + "webrtc", endpoint.WebrtcSignalUrl ?? "");
            PlayerPrefs.SetString(prefix + "label", endpoint.Label ?? "");
            PlayerPrefs.SetInt(prefix + "offline", endpoint.Offline ? 1 : 0);
            PlayerPrefs.SetString(prefix + "sid", endpoint.SessionId ?? "");
            PlayerPrefs.SetString(prefix + "source", endpoint.Source ?? "");
            var videos = endpoint.VideoUrls;
            PlayerPrefs.SetString(prefix + "video", videos == null || videos.Count == 0
                ? ""
                : string.Join("\n", videos.ToArray()));
            PlayerPrefs.Save();
        }

        /// <summary>Load the choice saved for this robot, if there is one.</summary>
        public static bool TryLoadChoice(string robotId, out Session.RobotEndpoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrEmpty(robotId)) return false;
            string prefix = Prefix(robotId);
            if (PlayerPrefs.GetInt(prefix + "has", 0) != 1) return false;

            string targetText = PlayerPrefs.GetString(prefix + "target", "LocalDesktop");
            if (!System.Enum.TryParse(targetText, out Session.SessionTarget target))
                target = Session.SessionTarget.LocalDesktop;

            string videos = PlayerPrefs.GetString(prefix + "video", "");
            var videoList = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(videos))
            {
                foreach (var part in videos.Split('\n'))
                {
                    if (!string.IsNullOrEmpty(part)) videoList.Add(part);
                }
            }

            endpoint = new Session.RobotEndpoint
            {
                RobotId = robotId,
                Kind = PlayerPrefs.GetString(prefix + "kind", "sim") == "real"
                    ? Session.SessionKind.Real
                    : Session.SessionKind.Sim,
                Target = target,
                RosbridgeUrl = PlayerPrefs.GetString(prefix + "url", ""),
                Host = PlayerPrefs.GetString(prefix + "host", ""),
                Port = PlayerPrefs.GetInt(prefix + "port", 9090),
                WebrtcSignalUrl = PlayerPrefs.GetString(prefix + "webrtc", ""),
                Label = PlayerPrefs.GetString(prefix + "label", ""),
                Offline = PlayerPrefs.GetInt(prefix + "offline", 0) == 1,
                SessionId = PlayerPrefs.GetString(prefix + "sid", ""),
                Source = PlayerPrefs.GetString(prefix + "source", "saved"),
                VideoUrls = videoList,
            };
            return true;
        }

        /// <summary>Drop the saved choice for one robot.</summary>
        public static void ClearChoice(string robotId)
        {
            if (string.IsNullOrEmpty(robotId)) return;
            string prefix = Prefix(robotId);
            PlayerPrefs.DeleteKey(prefix + "has");
            PlayerPrefs.DeleteKey(prefix + "kind");
            PlayerPrefs.DeleteKey(prefix + "target");
            PlayerPrefs.DeleteKey(prefix + "url");
            PlayerPrefs.DeleteKey(prefix + "host");
            PlayerPrefs.DeleteKey(prefix + "port");
            PlayerPrefs.DeleteKey(prefix + "webrtc");
            PlayerPrefs.DeleteKey(prefix + "label");
            PlayerPrefs.DeleteKey(prefix + "offline");
            PlayerPrefs.DeleteKey(prefix + "sid");
            PlayerPrefs.DeleteKey(prefix + "source");
            PlayerPrefs.DeleteKey(prefix + "video");
            PlayerPrefs.Save();
        }

        /// <summary>Clear the saved last robot (e.g. on sign-out).</summary>
        public static void Clear()
        {
            string id = PlayerPrefs.GetString(KeyRobotId, "");
            string alias = PlayerPrefs.GetString(KeyAlias, "");
            if (!string.IsNullOrEmpty(id)) ClearChoice(id);
            if (!string.IsNullOrEmpty(alias) && alias != id) ClearChoice(alias);
            PlayerPrefs.DeleteKey(KeyRobotId);
            PlayerPrefs.DeleteKey(KeyRobotName);
            PlayerPrefs.DeleteKey(KeyRobotIp);
            PlayerPrefs.DeleteKey(KeyRobotPort);
            PlayerPrefs.DeleteKey(KeyHasLast);
            PlayerPrefs.DeleteKey(KeyKind);
            PlayerPrefs.DeleteKey(KeyAlias);
            PlayerPrefs.Save();
        }

        private static string Prefix(string robotId) => "ohho.choice." + robotId + ".";
    }
}
