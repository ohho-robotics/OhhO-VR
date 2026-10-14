using System;
using System.IO;
using UnityEngine;
using OmniBot.VR.Core;

namespace OmniBot.VR.Metrics
{
    /// <summary>
    /// Logs time-to-first-drive for a beta session. Created on play without a
    /// scene edit so it does not depend on the UI builder. It only listens:
    /// connect state from <see cref="ConnectionManager"/> and messages
    /// <see cref="ROSBridgeClient"/> has already accepted for send.
    /// </summary>
    public sealed class FirstDriveMetric : MonoBehaviour
    {
        public const string RelativePath = "beta/first_drive.txt";

        private FirstDriveClock _clock;
        private ConnectionManager _connection;
        private bool _loggedReady;
        private bool _loggedDrive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying) return;
            if (FindObjectOfType<FirstDriveMetric>() != null) return;
            var go = new GameObject("FirstDriveMetric");
            DontDestroyOnLoad(go);
            go.AddComponent<FirstDriveMetric>();
        }

        private void Awake()
        {
            _clock = new FirstDriveClock(() => Time.realtimeSinceStartupAsDouble);
        }

        private void OnEnable()
        {
            ROSBridgeClient.MessagePublished += OnMessagePublished;
            AttachConnection();
        }

        private void Update()
        {
            if (_connection == null) AttachConnection();
        }

        private void OnDisable()
        {
            ROSBridgeClient.MessagePublished -= OnMessagePublished;
            if (_connection != null)
                _connection.OnConnectionStateChanged -= OnConnectionState;
        }

        private void AttachConnection()
        {
            if (_connection == null)
                _connection = FindObjectOfType<ConnectionManager>();
            if (_connection == null) return;
            _connection.OnConnectionStateChanged -= OnConnectionState;
            _connection.OnConnectionStateChanged += OnConnectionState;
            if (_connection.IsConnected) OnConnectionState(true);
        }

        private void OnConnectionState(bool connected)
        {
            if (!connected || _clock == null || _clock.HasConnected) return;
            _clock.MarkConnected();
            if (_loggedReady) return;
            _loggedReady = true;
            string line = _clock.FormatReadyLine();
            if (!string.IsNullOrEmpty(line)) Debug.Log(line);
        }

        private void OnMessagePublished(string topic, object message)
        {
            if (_loggedDrive || _clock == null) return;
            if (!_clock.ObserveMessage(topic, message)) return;
            _loggedDrive = true;
            string line = _clock.FormatLogLine();
            Debug.Log(line);
            TryWrite(line);
        }

        private static void TryWrite(string line)
        {
            try
            {
                WriteLog(Path.Combine(Application.persistentDataPath, RelativePath), line);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirstDrive] could not write {RelativePath}: {ex.Message}");
            }
        }

        /// <summary>Writes one log line. Used by the player and by edit-mode tests.</summary>
        public static void WriteLog(string path, string line)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, (line ?? "") + "\n");
        }
    }
}
