using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Thread-safe ROSBridge v2 WebSocket client.
    /// Dispatches incoming messages on the Unity main thread via ConcurrentQueue.
    /// Implements exponential back-off auto-reconnect (2s, 4s, 8s, max 3 attempts).
    /// </summary>
    public class ROSBridgeClient : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────
        private static ROSBridgeClient _instance;
        public static ROSBridgeClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("ROSBridgeClient");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ROSBridgeClient>();
                }
                return _instance;
            }
        }

        // ── Public state ─────────────────────────────────────────────────────
        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;
        public string Url { get; private set; }

        // ── Events ───────────────────────────────────────────────────────────
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        // ── Private fields ───────────────────────────────────────────────────
        private WebSocket _socket;
        private readonly ConcurrentQueue<(string topic, string json)> _incomingQueue
            = new ConcurrentQueue<(string, string)>();
        private readonly Dictionary<string, List<Action<string>>> _subscribers
            = new Dictionary<string, List<Action<string>>>();
        private readonly object _subscribersLock = new object();

        private bool _intentionalDisconnect = false;
        private readonly ReconnectBackoff _backoff = new ReconnectBackoff();
        private float _reconnectTimer = 0f;
        private bool _reconnecting = false;
        private float _reconnectDelay = 0f;
        private int _serviceId;
        private readonly Dictionary<string, Action<bool, string>> _pendingServices
            = new Dictionary<string, Action<bool, string>>();

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
        }

        private void Update()
        {
            // NativeWebSocket requires DispatchMessageQueue() on the main thread
            // (no-op on WebGL, required on all other platforms).
            if (_socket != null)
            {
                _socket.DispatchMessageQueue();
            }

            // Drain the incoming message queue on the main thread
            while (_incomingQueue.TryDequeue(out var item))
            {
                DispatchIncoming(item.topic, item.json);
            }

            // Auto-reconnect logic
            if (_reconnecting && !IsConnected)
            {
                _reconnectTimer -= Time.deltaTime;
                if (_reconnectTimer <= 0f)
                {
                    _reconnecting = false;
                    if (!string.IsNullOrEmpty(Url))
                    {
                        Debug.Log($"[ROSBridgeClient] Reconnect attempt {_backoff.Attempts}/{ReconnectBackoff.MaxAttempts} to {Url}");
                        _ = ConnectAsync(Url);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            _intentionalDisconnect = true;
            Disconnect();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Connects to the given ROSBridge WebSocket URL.</summary>
        public async void Connect(string url)
        {
            _intentionalDisconnect = false;
            _backoff.Reset();
            Url = url;
            await ConnectAsync(url);
        }

        /// <summary>Cleanly disconnects from ROSBridge.</summary>
        public async void Disconnect()
        {
            _intentionalDisconnect = true;
            _reconnecting = false;
            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                await _socket.Close();
            }
            _socket = null;
        }

        /// <summary>
        /// Advertises a publisher topic so ROSBridge creates the ROS publisher.
        /// Must be called before publishing to a new topic.
        /// </summary>
        public void Advertise(string topic, string type)
        {
            var msg = new ROSAdvertiseMessage(topic, type);
            SendJson(JsonConvert.SerializeObject(msg));
        }

        /// <summary>Serializes and publishes a message to a ROS topic.</summary>
        public void Publish<T>(string topic, T msg)
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"[ROSBridgeClient] Publish dropped — not connected. Topic: {topic}");
                return;
            }
            var wrapper = new ROSPublishMessage<T>(topic, msg);
            SendJson(JsonConvert.SerializeObject(wrapper));
        }

        /// <summary>
        /// Subscribes to a ROS topic. The callback is invoked on the main thread
        /// with the raw JSON string of the msg field.
        /// </summary>
        public void Subscribe(string topic, string type, Action<string> callback)
        {
            lock (_subscribersLock)
            {
                if (!_subscribers.ContainsKey(topic))
                {
                    _subscribers[topic] = new List<Action<string>>();
                    // Send subscribe op to ROSBridge
                    var subMsg = new ROSSubscribeMessage(topic, type);
                    SendJson(JsonConvert.SerializeObject(subMsg));
                }
                _subscribers[topic].Add(callback);
            }
        }

        /// <summary>Unsubscribes all callbacks for a topic.</summary>
        public void Unsubscribe(string topic)
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(topic);
            }
            var unsubMsg = new ROSUnsubscribeMessage(topic);
            SendJson(JsonConvert.SerializeObject(unsubMsg));
        }

        /// <summary>
        /// rosbridge <c>call_service</c>. The callback is invoked on the main
        /// thread when the matching <c>service_response</c> arrives.
        /// </summary>
        public void CallService(string service, string type, Action<bool, string> onResponse)
        {
            string id = "ohho_svc_" + (++_serviceId);
            if (onResponse != null)
                _pendingServices[id] = onResponse;
            SendJson(RosBridgeServiceCall.FormatRequest(id, service, type));
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private async System.Threading.Tasks.Task ConnectAsync(string url)
        {
            // Clean up any existing socket
            if (_socket != null)
            {
                _socket.OnOpen -= HandleOpen;
                _socket.OnClose -= HandleClose;
                _socket.OnError -= HandleError;
                _socket.OnMessage -= HandleMessage;
            }

            _socket = new WebSocket(url);
            _socket.OnOpen += HandleOpen;
            _socket.OnClose += HandleClose;
            _socket.OnError += HandleError;
            _socket.OnMessage += HandleMessage;

            try
            {
                await _socket.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ROSBridgeClient] Connect exception: {ex.Message}");
                ScheduleReconnect();
            }
        }

        private void HandleOpen()
        {
            Debug.Log($"[ROSBridgeClient] Connected to {Url}");
            _backoff.Reset();
            _reconnecting = false;
            // Dispatch on main thread via queue trick — enqueue a sentinel
            _incomingQueue.Enqueue(("__connected__", ""));
        }

        private void HandleClose(WebSocketCloseCode code)
        {
            Debug.Log($"[ROSBridgeClient] Disconnected. Code: {code}");
            _incomingQueue.Enqueue(("__disconnected__", ""));
            if (!_intentionalDisconnect)
            {
                ScheduleReconnect();
            }
        }

        private void HandleError(string errorMsg)
        {
            Debug.LogError($"[ROSBridgeClient] WebSocket error: {errorMsg}");
            _incomingQueue.Enqueue(("__error__", errorMsg));
        }

        private void HandleMessage(byte[] data)
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            try
            {
                // Quick op check without full deserialize
                JObject root = JObject.Parse(json);
                string op = root["op"]?.ToString();
                if (op == "service_response")
                {
                    _incomingQueue.Enqueue(("__service__", json));
                    return;
                }
                if (op == "publish")
                {
                    string topic = root["topic"]?.ToString();
                    if (!string.IsNullOrEmpty(topic))
                    {
                        string msgJson = root["msg"]?.ToString(Formatting.None) ?? "{}";
                        _incomingQueue.Enqueue((topic, msgJson));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ROSBridgeClient] Failed to parse incoming message: {ex.Message}");
            }
        }

        private void DispatchIncoming(string topic, string json)
        {
            // Handle sentinel events on main thread
            if (topic == "__connected__")
            {
                // Re-subscribe to all known topics after reconnect
                ResubscribeAll();
                OnConnected?.Invoke();
                return;
            }
            if (topic == "__disconnected__")
            {
                OnDisconnected?.Invoke();
                return;
            }
            if (topic == "__error__")
            {
                OnError?.Invoke(json);
                return;
            }
            if (topic == "__service__")
            {
                DispatchService(json);
                return;
            }

            List<Action<string>> callbacks = null;
            lock (_subscribersLock)
            {
                if (_subscribers.TryGetValue(topic, out var list))
                {
                    callbacks = new List<Action<string>>(list);
                }
            }

            if (callbacks != null)
            {
                foreach (var cb in callbacks)
                {
                    try { cb(json); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ROSBridgeClient] Subscriber callback exception on {topic}: {ex.Message}");
                    }
                }
            }
        }

        private void ResubscribeAll()
        {
            lock (_subscribersLock)
            {
                foreach (var kvp in _subscribers)
                {
                    // We don't track types here so we use empty string; ROSBridge accepts it
                    var subMsg = new ROSSubscribeMessage(kvp.Key, "");
                    SendJson(JsonConvert.SerializeObject(subMsg));
                }
            }
        }

        private void DispatchService(string json)
        {
            if (!RosBridgeServiceCall.TryParseResponse(json, out string id, out bool ok, out string values))
            {
                Debug.LogWarning("[ROSBridgeClient] Unreadable service_response.");
                return;
            }

            if (string.IsNullOrEmpty(id) || !_pendingServices.TryGetValue(id, out var callback))
                return;

            _pendingServices.Remove(id);
            try { callback(ok, values); }
            catch (Exception ex)
            {
                Debug.LogError($"[ROSBridgeClient] Service callback exception: {ex.Message}");
            }
        }

        private void ScheduleReconnect()
        {
            if (_intentionalDisconnect) return;
            if (!_backoff.TrySchedule(out float delay))
            {
                Debug.LogWarning("[ROSBridgeClient] Max reconnect attempts reached.");
                OnError?.Invoke("Max reconnect attempts reached.");
                return;
            }
            // Exponential back-off: 2s, 4s, 8s
            _reconnectDelay = delay;
            _reconnectTimer = _reconnectDelay;
            _reconnecting = true;
            Debug.Log($"[ROSBridgeClient] Scheduling reconnect in {_reconnectDelay}s (attempt {_backoff.Attempts})");
        }

        private async void SendJson(string json)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                Debug.LogWarning($"[ROSBridgeClient] SendJson dropped — socket not open.");
                return;
            }
            try
            {
                await _socket.SendText(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ROSBridgeClient] SendJson exception: {ex.Message}");
            }
        }
    }
}
