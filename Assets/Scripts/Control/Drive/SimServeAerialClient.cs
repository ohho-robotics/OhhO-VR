using System;
using System.Net;
using UnityEngine;
using NativeWebSocket;

namespace OmniBot.VR.Control.Drive
{
    /// <summary>
    /// Something that can accept one sim-serve text frame. Tests use a recorder.
    /// The play-mode implementation is <see cref="SimServeAerialClient"/>.
    /// </summary>
    public interface ISimServeSender
    {
        bool IsConnected { get; }
        void Send(string json);
    }

    /// <summary>
    /// WebSocket client for <c>ohho sim-serve</c> on loopback port 8765.
    /// Refuses any non-loopback URL so this headset build cannot address a
    /// flight controller, radio, or off-box robot.
    /// </summary>
    public sealed class SimServeAerialClient : MonoBehaviour, ISimServeSender
    {
        public const string DefaultUrl = "ws://127.0.0.1:8765";

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;
        public string Url { get; private set; }

        WebSocket _socket;
        bool _intentionalDisconnect;

        void Update()
        {
            if (_socket != null) _socket.DispatchMessageQueue();
        }

        void OnDestroy()
        {
            _intentionalDisconnect = true;
            Disconnect();
        }

        public void ConnectLoopback()
        {
            Connect(DefaultUrl);
        }

        public void Connect(string url)
        {
            if (!IsLoopbackUrl(url))
            {
                Debug.LogError("[SimServeAerial] Refusing a non-loopback sim-serve URL.");
                return;
            }
            if (IsConnected && string.Equals(Url, url, StringComparison.OrdinalIgnoreCase))
                return;
            Url = url;
            _intentionalDisconnect = false;
            _ = ConnectAsync(url);
        }

        public void Disconnect()
        {
            _intentionalDisconnect = true;
            if (_socket != null && _socket.State == WebSocketState.Open)
                _ = _socket.Close();
            _socket = null;
        }

        public void Send(string json)
        {
            if (!IsConnected || string.IsNullOrEmpty(json)) return;
            _ = SendAsync(json);
        }

        /// <summary>True for ws/wss to localhost, 127.0.0.1, or ::1 only.</summary>
        public static bool IsLoopbackUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri)) return false;
            if (!uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
                return false;
            string host = uri.Host.Trim();
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host.Substring(1, host.Length - 2);
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(host, out IPAddress address)) return false;
            return IPAddress.IsLoopback(address);
        }

        async System.Threading.Tasks.Task ConnectAsync(string url)
        {
            if (_socket != null)
            {
                _socket.OnOpen -= HandleOpen;
                _socket.OnError -= HandleError;
                _socket.OnClose -= HandleClose;
            }

            _socket = new WebSocket(url);
            _socket.OnOpen += HandleOpen;
            _socket.OnError += HandleError;
            _socket.OnClose += HandleClose;
            try
            {
                await _socket.Connect();
            }
            catch (Exception ex)
            {
                if (!_intentionalDisconnect)
                    Debug.LogWarning("[SimServeAerial] Connect failed: " + ex.Message);
            }
        }

        async System.Threading.Tasks.Task SendAsync(string json)
        {
            try
            {
                await _socket.SendText(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SimServeAerial] Send failed: " + ex.Message);
            }
        }

        void HandleOpen()
        {
            Debug.Log("[SimServeAerial] Connected to " + Url);
        }

        void HandleError(string message)
        {
            Debug.LogWarning("[SimServeAerial] " + message);
        }

        void HandleClose(WebSocketCloseCode code)
        {
            if (!_intentionalDisconnect)
                Debug.Log("[SimServeAerial] Closed (" + code + ").");
        }
    }
}
