using System;
using UnityEngine;
using OmniBot.VR.Descriptor;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Orchestrates the ROSBridge connection lifecycle: reads/writes PlayerPrefs,
    /// advertises all publish topics, and subscribes to all receive topics after
    /// a successful connection.
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────
        private static ConnectionManager _instance;
        public static ConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("ConnectionManager");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ConnectionManager>();
                }
                return _instance;
            }
        }

        // ── PlayerPrefs keys ─────────────────────────────────────────────────
        private const string PrefKeyIp = "robot_ip";
        private const string PrefKeyPort = "robot_port";
        private const string PrefKeyAutoConnect = "auto_connect";

        // ── Public state ─────────────────────────────────────────────────────
        public bool IsConnected => ROSBridgeClient.Instance.IsConnected;
        public string CurrentIp { get; private set; }
        public int CurrentPort { get; private set; }

        /// <summary>Fires on state changes: true = connected, false = disconnected.</summary>
        public event Action<bool> OnConnectionStateChanged;

        /// <summary>Fires when the robot IP changes (e.g. after a successful connect).</summary>
        public event Action<string> OnRobotIpChanged;

        /// <summary>Last descriptor that parsed. Null when disconnected or the document was rejected.</summary>
        public RobotDescriptor LatestDescriptor { get; private set; }

        /// <summary>Fires when a latched /ohho/descriptor message parses. Invalid documents are ignored.</summary>
        public event Action<RobotDescriptor> OnRobotDescriptor;

        private Action<string> _onDescriptorMessage;

        // ── Unity lifecycle ──────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentIp = PlayerPrefs.GetString(PrefKeyIp, "192.168.1.101");
            CurrentPort = PlayerPrefs.GetInt(PrefKeyPort, 9090);
        }

        private void Start()
        {
            // Wire ROSBridgeClient events
            ROSBridgeClient.Instance.OnConnected += HandleConnected;
            ROSBridgeClient.Instance.OnDisconnected += HandleDisconnected;
            ROSBridgeClient.Instance.OnError += HandleError;

            // Auto-connect if preference set
            if (PlayerPrefs.GetInt(PrefKeyAutoConnect, 0) == 1)
            {
                Debug.Log("[ConnectionManager] Auto-connect enabled, connecting...");
                Connect(CurrentIp, CurrentPort);
            }
        }

        private void OnDestroy()
        {
            if (ROSBridgeClient.Instance != null)
            {
                ROSBridgeClient.Instance.OnConnected -= HandleConnected;
                ROSBridgeClient.Instance.OnDisconnected -= HandleDisconnected;
                ROSBridgeClient.Instance.OnError -= HandleError;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Connects to the robot at the given IP and port.</summary>
        public void Connect(string ip, int port)
        {
            CurrentIp = ip;
            CurrentPort = port;
            string url = $"ws://{ip}:{port}";
            Debug.Log($"[ConnectionManager] Connecting to {url}");
            ROSBridgeClient.Instance.Connect(url);
        }

        /// <summary>Unsubscribes from all topics, then disconnects cleanly.</summary>
        public void Disconnect()
        {
            ROSBridgeClient.Instance.UnsubscribeAll();
            LatestDescriptor = null;
            ROSBridgeClient.Instance.Disconnect();
        }

        /// <summary>Saves auto-connect preference to PlayerPrefs.</summary>
        public void SetAutoConnect(bool enabled)
        {
            PlayerPrefs.SetInt(PrefKeyAutoConnect, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Returns the current auto-connect preference.</summary>
        public bool GetAutoConnect() => PlayerPrefs.GetInt(PrefKeyAutoConnect, 0) == 1;

        // ── Event handlers ───────────────────────────────────────────────────

        private void HandleConnected()
        {
            Debug.Log("[ConnectionManager] Connected — waiting for /ohho/descriptor.");

            // Save last-known connection details
            PlayerPrefs.SetString(PrefKeyIp, CurrentIp);
            PlayerPrefs.SetInt(PrefKeyPort, CurrentPort);
            PlayerPrefs.Save();

            // Command topics are advertised by TeleopController from the profile.
            // The only connection-time subscription is the latched descriptor.
            if (_onDescriptorMessage == null)
                _onDescriptorMessage = OnDescriptorMessage;
            ROSBridgeClient.Instance.Subscribe(
                RobotDescriptor.Topic, RobotDescriptor.MessageType, _onDescriptorMessage);

            OnConnectionStateChanged?.Invoke(true);
            OnRobotIpChanged?.Invoke(CurrentIp);
        }

        private void OnDescriptorMessage(string json)
        {
            if (!RobotDescriptor.TryParse(json, out var descriptor, out var error))
            {
                Debug.LogWarning("[ConnectionManager] Rejected /ohho/descriptor: " + error);
                return;
            }

            LatestDescriptor = descriptor;
            OnRobotDescriptor?.Invoke(descriptor);
        }

        private void HandleDisconnected()
        {
            Debug.Log("[ConnectionManager] Disconnected.");
            LatestDescriptor = null;
            OnConnectionStateChanged?.Invoke(false);
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[ConnectionManager] ROSBridge error: {error}");
        }
    }
}
