using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using OmniBot.VR.Core;
using OmniBot.VR.Video;

namespace OmniBot.VR.UI
{
    /// <summary>
    /// Legacy MJPEG panel. Decoding and texture upload go through
    /// <see cref="MjpegVideoSource"/>, the same reused-buffer path as the sim
    /// screen, instead of parsing headers into strings and calling
    /// <c>ImageConversion.LoadImage</c> on a fresh byte array every frame.
    /// The camera name is written when the stream starts, not every frame.
    /// </summary>
    public class CameraFeedViewer : MonoBehaviour
    {
        [Header("Camera URLs (topic path only, e.g. /camera/front/image_raw)")]
        [SerializeField] private string[] cameraTopics = {
            "/camera/front/image_raw",
            "/camera/wrist/image_raw",
            "/camera/base/bev/image_raw",
        };

        [Header("Display")]
        [SerializeField] private RawImage displayImage;
        [SerializeField] private TMP_Text cameraNameOverlay;
        [SerializeField] private Texture2D noSignalTexture;

        [Header("web_video_server")]
        [SerializeField] private int webVideoServerPort = 8080;

        private string[] _fullUrls;
        private int _currentCameraIndex;
        private string _robotIp = "192.168.1.101";
        private MjpegVideoSource _mjpeg;
        private Texture2D _grey;

        private void Start()
        {
            _robotIp = PlayerPrefs.GetString("robot_ip", "192.168.1.101");
            RebuildUrls(_robotIp);

            ConnectionManager.Instance.OnRobotIpChanged += OnRobotIpChanged;
            ConnectionManager.Instance.OnConnectionStateChanged += OnConnectionStateChanged;

            StartStreaming();
        }

        private void OnDestroy()
        {
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnRobotIpChanged -= OnRobotIpChanged;
                ConnectionManager.Instance.OnConnectionStateChanged -= OnConnectionStateChanged;
            }
            StopStreaming();
            if (_grey != null) Destroy(_grey);
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick, OVRInput.Controller.RTouch))
                CycleCamera();
        }

        public void CycleCamera()
        {
            if (_fullUrls == null || _fullUrls.Length == 0) return;
            _currentCameraIndex = (_currentCameraIndex + 1) % _fullUrls.Length;
            StartStreaming();
        }

        public void SetCamera(int index)
        {
            if (_fullUrls == null || _fullUrls.Length == 0) return;
            _currentCameraIndex = Mathf.Clamp(index, 0, _fullUrls.Length - 1);
            StartStreaming();
        }

        private void OnRobotIpChanged(string newIp)
        {
            _robotIp = newIp;
            RebuildUrls(newIp);
            StartStreaming();
        }

        private void OnConnectionStateChanged(bool connected)
        {
            if (!connected)
            {
                StopStreaming();
                ShowNoSignal();
            }
            else
            {
                StartStreaming();
            }
        }

        private void RebuildUrls(string ip)
        {
            if (cameraTopics == null)
            {
                _fullUrls = new string[0];
                return;
            }
            _fullUrls = new string[cameraTopics.Length];
            for (int i = 0; i < cameraTopics.Length; i++)
            {
                _fullUrls[i] = MjpegUrl.Build(
                    ip,
                    webVideoServerPort,
                    cameraTopics[i],
                    SimScreenCameras.MainQuality,
                    SimScreenCameras.MainWidth);
            }
        }

        private void StartStreaming()
        {
            StopStreaming();
            if (_fullUrls == null || _fullUrls.Length == 0)
            {
                ShowNoSignal();
                return;
            }

            int idx = Mathf.Clamp(_currentCameraIndex, 0, _fullUrls.Length - 1);
            string url = _fullUrls[idx];
            string topicName = cameraTopics[idx];
            if (cameraNameOverlay != null && cameraNameOverlay.text != topicName)
                cameraNameOverlay.text = topicName;

            _mjpeg = new MjpegVideoSource(this);
            _mjpeg.OnFrame += OnFrame;
            _mjpeg.OnError += OnStreamError;
            _mjpeg.StartUrl(url);
        }

        private void StopStreaming()
        {
            if (_mjpeg == null) return;
            _mjpeg.OnFrame -= OnFrame;
            _mjpeg.OnError -= OnStreamError;
            ShowNoSignal();
            _mjpeg.Dispose();
            _mjpeg = null;
        }

        private void OnFrame(Texture2D frame)
        {
            if (displayImage == null || frame == null) return;
            if (displayImage.texture != frame)
            {
                displayImage.texture = frame;
                displayImage.color = Color.white;
            }
        }

        private void OnStreamError(string message)
        {
            ShowNoSignal();
        }

        private void ShowNoSignal()
        {
            if (displayImage == null) return;
            if (noSignalTexture != null)
            {
                displayImage.texture = noSignalTexture;
                return;
            }
            if (_grey == null)
            {
                _grey = new Texture2D(4, 4, TextureFormat.RGB24, false, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.2f, 0.2f, 0.2f);
                _grey.SetPixels(pixels);
                _grey.Apply(false, true);
            }
            displayImage.texture = _grey;
        }
    }
}
