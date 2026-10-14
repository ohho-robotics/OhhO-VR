using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// MJPEG from <c>web_video_server</c>. Bytes are parsed on the main thread;
    /// baseline JPEG decode runs on a worker. The main thread only uploads the
    /// latest RGB buffer (<see cref="Texture2D.LoadRawTextureData"/>). Frames
    /// are capped at <see cref="FramePacer.MaxFps"/>. <see cref="JpegQuality"/>
    /// and <see cref="JpegWidth"/> are sent as <c>quality</c> and <c>width</c>.
    /// </summary>
    public sealed class MjpegVideoSource : IVideoSource
    {
        private const float NoSignalTimeout = 5f;

        public string TransportName => "MJPEG";
        public bool IsStreaming => _coroutine != null;
        public event Action<Texture2D> OnFrame;
        public event Action<string> OnError;

        public int JpegQuality = SimScreenCameras.MainQuality;
        public int JpegWidth = SimScreenCameras.MainWidth;

        /// <summary>Realtime clock when the JPEG now on screen was pulled from the socket.</summary>
        public double LastFrameReceivedAt { get; private set; }

        public bool HasPresentedFrame { get; private set; }

        private readonly MonoBehaviour _host;
        private readonly BaselineJpegDecoder _decoder = new BaselineJpegDecoder();
        private readonly object _gate = new object();
        private Coroutine _coroutine;
        private Texture2D _texture;
        private byte[] _workerRgb;
        private byte[] _pending;
        private byte[] _upload;
        private int _pendingW;
        private int _pendingH;
        private double _pendingAt;
        private bool _hasPending;
        private bool _decodeBusy;
        private bool _disposed;
        private volatile int _generation;
        private double _lastSubmit = -1;

        public MjpegVideoSource(MonoBehaviour host)
        {
            _host = host;
        }

        public void Start(string robotIp, string topic)
        {
            StartUrl(MjpegUrl.Build(robotIp, MjpegUrl.Port, topic, JpegQuality, JpegWidth));
        }

        public void StartUrl(string url)
        {
            Stop();
            if (string.IsNullOrEmpty(url) || _host == null) return;
            url = MjpegUrl.WithParams(url, JpegQuality, JpegWidth);
            _lastSubmit = -1;
            HasPresentedFrame = false;
            _coroutine = _host.StartCoroutine(Stream(url, _generation));
        }

        public void Stop()
        {
            lock (_gate)
            {
                _generation++;
                _decodeBusy = false;
                _hasPending = false;
                _pending = null;
            }
            _lastSubmit = -1;
            if (_coroutine != null && _host != null)
            {
                _host.StopCoroutine(_coroutine);
                _coroutine = null;
            }
        }

        public void Dispose()
        {
            Stop();
            _disposed = true;
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
        }

        private IEnumerator Stream(string url, int generation)
        {
            while (!_disposed && generation == _generation)
            {
                var buffer = new MjpegChunkBuffer();
                var handler = new MjpegDownloadHandler();
                float silent = 0f;
                bool signaled = false;
                handler.OnBytes = (bytes, n) =>
                {
                    if (generation != _generation) return;
                    buffer.Append(bytes, n, (frame, offset, length) => Submit(frame, offset, length, generation));
                };

                using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    req.downloadHandler = handler;
                    req.SetRequestHeader("Accept", "multipart/x-mixed-replace");
                    req.timeout = 0;
                    req.SendWebRequest();

                    while (!req.isDone && !_disposed && generation == _generation)
                    {
                        PumpUpload();
                        if (!HasPresentedFrame)
                        {
                            silent += Time.unscaledDeltaTime;
                            if (silent > NoSignalTimeout && !signaled)
                            {
                                signaled = true;
                                OnError?.Invoke("No signal");
                            }
                        }
                        yield return null;
                    }
                    PumpUpload();

                    if (!_disposed && generation == _generation && req.result != UnityWebRequest.Result.Success)
                        OnError?.Invoke("Stream error: " + req.error);
                }

                handler.OnBytes = null;
                if (_disposed || generation != _generation) break;

                float wait = 0f;
                while (wait < 2f && !_disposed && generation == _generation)
                {
                    PumpUpload();
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        private void Submit(byte[] src, int offset, int length, int generation)
        {
            if (_disposed || generation != _generation || length <= 0) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!FramePacer.ShouldTake(_lastSubmit, now)) return;
            lock (_gate)
            {
                if (_decodeBusy || generation != _generation) return;
                _decodeBusy = true;
                _lastSubmit = now;
            }

            var copy = new byte[length];
            Buffer.BlockCopy(src, offset, copy, 0, length);
            ThreadPool.QueueUserWorkItem(_ => DecodeWorker(copy, generation, now));
        }

        private void DecodeWorker(byte[] jpeg, int generation, double receivedAt)
        {
            try
            {
                if (generation != _generation || _disposed) return;
                byte[] rgb = _workerRgb;
                if (!_decoder.TryDecode(jpeg, jpeg.Length, ref rgb, out int w, out int h, out _))
                    return;
                _workerRgb = rgb;
                if (generation != _generation || _disposed) return;
                int bytes = w * h * 3;
                lock (_gate)
                {
                    if (generation != _generation || _disposed) return;
                    if (_pending == null || _pending.Length < bytes) _pending = new byte[bytes];
                    Buffer.BlockCopy(rgb, 0, _pending, 0, bytes);
                    _pendingW = w;
                    _pendingH = h;
                    _pendingAt = receivedAt;
                    _hasPending = true;
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (generation == _generation) _decodeBusy = false;
                }
            }
        }

        private void PumpUpload()
        {
            if (_disposed) return;
            byte[] pending;
            int w;
            int h;
            double at;
            lock (_gate)
            {
                if (!_hasPending || _pending == null) return;
                w = _pendingW;
                h = _pendingH;
                at = _pendingAt;
                int bytes = w * h * 3;
                if (_upload == null || _upload.Length != bytes) _upload = new byte[bytes];
                BaselineJpegDecoder.FlipVertical(_pending, _upload, w, h);
                pending = _upload;
                _hasPending = false;
            }

            if (_texture == null || _texture.width != w || _texture.height != h)
            {
                if (_texture != null) UnityEngine.Object.Destroy(_texture);
                // linear:false marks the JPEG as sRGB so the linear Quest framebuffer converts it.
                _texture = new Texture2D(w, h, TextureFormat.RGB24, false, false);
                _texture.wrapMode = TextureWrapMode.Clamp;
                _texture.filterMode = FilterMode.Bilinear;
            }
            _texture.LoadRawTextureData(pending);
            _texture.Apply(false, false);
            LastFrameReceivedAt = at;
            HasPresentedFrame = true;
            OnFrame?.Invoke(_texture);
        }

        private sealed class MjpegDownloadHandler : DownloadHandlerScript
        {
            public Action<byte[], int> OnBytes;

            public MjpegDownloadHandler() : base(new byte[16 * 1024]) { }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data != null && dataLength > 0 && OnBytes != null)
                    OnBytes(data, dataLength);
                return true;
            }
        }
    }
}
