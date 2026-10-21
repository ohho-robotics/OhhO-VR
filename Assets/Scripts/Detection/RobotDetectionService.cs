using System;
using System.Collections.Generic;
using OmniBot.VR.AprilTagSpike;
using OmniBot.VR.Control;
using UnityEngine;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Passthrough camera seam. MRUK <c>PassthroughCameraAccess</c> is not in this
    /// project (see docs/adr/213-apriltag-passthrough.md), so capture reports
    /// unavailable and detection does not invent tags. A fake
    /// <see cref="IPassthroughFrameSource"/> supplies frames in tests.
    /// </summary>
    public sealed class HeadsetPassthroughCapture : IPassthroughFrameSource
    {
        public const string NotInstalledReason =
            "Passthrough camera capture needs com.meta.xr.mrutilitykit 203.0.0 (PassthroughCameraAccess). It is not installed, so robot detection cannot see tags.";

        public bool IsAvailable => false;
        public string UnavailableReason => NotInstalledReason;

        public bool TryGetLatest(out PassthroughFrame frame)
        {
            frame = default;
            return false;
        }
    }

    /// <summary>
    /// Runs tag fusion on a worker at 10-15 Hz. The main thread captures the
    /// camera world pose with the frame. Odom is subscribed from the active
    /// <see cref="RobotProfile"/> topic. The feature stays off when the headset
    /// camera permission is denied, and it stays off in the editor.
    /// Not added to <c>Assets/scene1.unity</c>.
    /// </summary>
    public sealed class RobotDetectionService : MonoBehaviour
    {
        [SerializeField] private float rateHz = RobotDetectionLimits.DefaultRateHz;

        private readonly object _sync = new object();
        private readonly object _pipelineGate = new object();
        private readonly RobotDetectionPipeline _pipeline = new RobotDetectionPipeline();
        private IPassthroughFrameSource _frames;
        private IAprilTagDetector _detector;
        private ISessionOriginAnchor _anchor;
        private IRobotLink _link;
        private RobotProfile _profile;
        private Action<string> _odomCallback;
        private bool _acceptOdom;
        private bool _profileBound;
        private bool _waitingForPermission;
        private bool _hasFrame;
        private PassthroughFrame _latestFrame;
        private RigidTransform _latestCamera;
        private bool _hasOdom;
        private bool _odomHasStamp;
        private double _odomStamp;
        private RigidTransform _odom = RigidTransform.Identity;
        private RigidTransform _anchorPose = RigidTransform.Identity;
        private bool _hasAnchorPose;
        private RigidTransform _pendingCorrection = RigidTransform.Identity;
        private bool _hasPendingCorrection;
        private System.Threading.Thread _worker;
        private volatile bool _run;
        private readonly System.Threading.ManualResetEventSlim _processed = new System.Threading.ManualResetEventSlim(false);

        public bool FeatureEnabled { get; private set; }
        public bool PermissionDenied { get; private set; }
        public string DisabledReason { get; private set; } = HeadsetCameraGate.EditorReason;
        public string PermissionRationale => HeadsetCameraGate.Rationale;
        public RobotDetectionSnapshot Snapshot { get; private set; } = RobotDetectionSnapshot.Inactive(0);
        public ISessionOriginAnchor SessionOrigin => _anchor;
        public int LastDetectorThreadId { get; private set; }
        public int MainThreadId { get; private set; }
        public string LastWorkerError { get; private set; }
        public bool WorkerRunning => _worker != null && _worker.IsAlive;
        public string OdomTopic { get; private set; }
        public bool CameraAvailable
        {
            get
            {
                EnsureDefaults();
                return _frames.IsAvailable;
            }
        }

        public string CameraUnavailableReason
        {
            get
            {
                EnsureDefaults();
                return _frames.UnavailableReason ?? "";
            }
        }

        public float RateHz
        {
            get => rateHz;
            set => rateHz = Mathf.Clamp(value, RobotDetectionLimits.MinRateHz, RobotDetectionLimits.MaxRateHz);
        }

        public int PeriodMilliseconds => RobotDetectionLimits.PeriodMilliseconds(RateHz);

        private void Awake()
        {
            EnsureDefaults();
            RateHz = rateHz;
        }

        private void Start()
        {
            EnsureDefaults();
            var headset = HeadsetCameraGate.IsHeadsetPlayer;
            var granted = HeadsetCameraGate.IsGrantedOnHeadset();
            ApplyPermission(headset, granted);
            if (!headset)
            {
                return;
            }

            if (!granted)
            {
                Debug.Log("[RobotDetection] " + HeadsetCameraGate.Rationale);
                _waitingForPermission = true;
                HeadsetCameraGate.RequestAccess();
                return;
            }

            BeginRunning();
        }

        private void Update()
        {
            if (_waitingForPermission && HeadsetCameraGate.IsGrantedOnHeadset())
            {
                _waitingForPermission = false;
                ApplyPermission(true, true);
                BeginRunning();
            }

            if (!FeatureEnabled)
            {
                return;
            }

            TryBindActiveProfile();
            if (_frames != null && _frames.IsAvailable && _frames.TryGetLatest(out var frame))
            {
                SubmitCapturedFrame(frame);
            }

            ApplyAnchorCorrection();
        }

        private void OnDestroy()
        {
            StopWorker();
            DetachOdom();
            _anchor?.Release();
            if (!WorkerRunning)
            {
                _processed.Dispose();
            }
        }

        public void SetSources(
            IPassthroughFrameSource frames,
            IAprilTagDetector detector,
            ISessionOriginAnchor anchor)
        {
            EnsureDefaults();
            if (frames != null) _frames = frames;
            if (detector != null) _detector = detector;
            if (anchor != null) _anchor = anchor;
        }

        /// <summary>
        /// Subscribe to <paramref name="profile"/>'s odom topic. The topic string
        /// comes from the profile. This does not read <c>RobotConfig</c> topic constants.
        /// </summary>
        public void UseProfile(RobotProfile profile, IRobotLink link)
        {
            _profile = profile;
            _link = link;
            _profileBound = true;
            if (FeatureEnabled)
            {
                BindOdom();
            }
        }

        public void ApplyPermission(bool headsetBuild, bool granted)
        {
            if (!headsetBuild || !granted)
            {
                FeatureEnabled = false;
                PermissionDenied = headsetBuild && !granted;
                DisabledReason = PermissionDenied
                    ? HeadsetCameraGate.DeniedReason
                    : HeadsetCameraGate.EditorReason;
                StopWorker();
                DetachOdom();
                Snapshot = RobotDetectionSnapshot.Inactive(Snapshot.TimestampSeconds);
                return;
            }

            FeatureEnabled = true;
            PermissionDenied = false;
            DisabledReason = "";
            if (_profileBound)
            {
                BindOdom();
            }
        }

        public void NotifyPermissionDenied()
        {
            _waitingForPermission = false;
            ApplyPermission(headsetBuild: true, granted: false);
        }

        public void SubmitCapturedFrame(PassthroughFrame frame)
        {
            var camera = RigidTransform.FromUnityPose(frame.CameraPose);
            lock (_sync)
            {
                _latestFrame = frame;
                _latestCamera = camera;
                _hasFrame = true;
            }

            _processed.Reset();
        }

        public bool StartWorker()
        {
            if (!FeatureEnabled)
            {
                return false;
            }

            if (_worker != null && _worker.IsAlive)
            {
                return true;
            }

            EnsureDefaults();
            MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _run = true;
            _worker = new System.Threading.Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "OhhoRobotDetection",
            };
            _worker.Start();
            return true;
        }

        public void StopWorker()
        {
            _run = false;
            var worker = _worker;
            if (worker == null)
            {
                return;
            }

            if (worker.IsAlive && worker != System.Threading.Thread.CurrentThread)
            {
                if (!worker.Join(2000))
                {
                    return;
                }
            }

            _worker = null;
        }

        public bool WaitUntilProcessed(int timeoutMs)
        {
            return _processed.Wait(timeoutMs);
        }

        /// <summary>
        /// Synchronous path for tests. The worker calls the same pipeline.
        /// </summary>
        public RobotDetectionSnapshot ProcessSample(PassthroughFrame frame, double timeSeconds)
        {
            if (!FeatureEnabled)
            {
                Snapshot = RobotDetectionSnapshot.Inactive(timeSeconds);
                return Snapshot;
            }

            EnsureDefaults();
            var camera = RigidTransform.FromUnityPose(frame.CameraPose);
            bool hasOdom;
            RigidTransform odom;
            lock (_sync)
            {
                hasOdom = _hasOdom;
                odom = _odom;
            }

            var detections = Detect(frame);
            RobotDetectionSnapshot snap;
            lock (_pipelineGate)
            {
                snap = _pipeline.Step(timeSeconds, camera, detections, hasOdom, odom);
            }

            lock (_sync)
            {
                Snapshot = snap;
            }

            ApplyAnchorCorrection();
            return snap;
        }

        public void PushOdomJsonForTests(string json)
        {
            if (_odomCallback != null)
            {
                _odomCallback(json);
            }
        }

        private void BeginRunning()
        {
            if (!FeatureEnabled)
            {
                return;
            }

            TryBindActiveProfile();
            StartWorker();
        }

        private void EnsureDefaults()
        {
            if (_frames == null) _frames = new HeadsetPassthroughCapture();
            if (_detector == null) _detector = new StubAprilTagDetector();
            if (_anchor == null) _anchor = SessionOriginAnchors.CreateForThisRuntime();
        }

        private void TryBindActiveProfile()
        {
            if (_profileBound)
            {
                return;
            }

            var teleop = FindAnyObjectByType<TeleopController>();
            if (teleop == null || teleop.Profile == null)
            {
                return;
            }

            UseProfile(teleop.Profile, RosBridgeLink.Instance);
        }

        private void BindOdom()
        {
            DetachOdom();
            var topic = _profile?.Topics?.Odom;
            if (string.IsNullOrEmpty(topic) || _link == null)
            {
                OdomTopic = "";
                return;
            }

            OdomTopic = topic;
            _acceptOdom = true;
            _odomCallback = OnOdomJson;
            _link.Subscribe(topic, "nav_msgs/Odometry", _odomCallback);
        }

        private void DetachOdom()
        {
            _acceptOdom = false;
            _odomCallback = null;
            OdomTopic = "";
        }

        private void OnOdomJson(string json)
        {
            if (!_acceptOdom)
            {
                return;
            }

            if (!OdomObservationParser.TryParse(json, out var observation))
            {
                return;
            }

            lock (_sync)
            {
                if (observation.HasStamp && _hasOdom && _odomHasStamp
                    && observation.StampSeconds + 1e-6 < _odomStamp)
                {
                    return;
                }

                _odom = observation.BaseInOdomUnity;
                _hasOdom = true;
                _odomHasStamp = observation.HasStamp;
                _odomStamp = observation.StampSeconds;
            }
        }

        private void WorkerLoop()
        {
            while (_run)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                if (TryTakeFrame(out var frame, out var camera, out var hasOdom, out var odom))
                {
                    try
                    {
                        var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                        var time = frame.TimestampSeconds;
                        var detections = Detect(frame);
                        RigidTransform pending = RigidTransform.Identity;
                        var hasPending = false;
                        lock (_sync)
                        {
                            if (_hasPendingCorrection)
                            {
                                pending = _pendingCorrection;
                                hasPending = true;
                                _hasPendingCorrection = false;
                            }
                        }

                        RobotDetectionSnapshot snap;
                        lock (_pipelineGate)
                        {
                            if (hasPending)
                            {
                                _pipeline.ApplyWorldCorrection(pending);
                            }

                            snap = _pipeline.Step(time, camera, detections, hasOdom, odom);
                        }

                        lock (_sync)
                        {
                            LastDetectorThreadId = threadId;
                            Snapshot = snap;
                            LastWorkerError = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (_sync)
                        {
                            LastWorkerError = ex.GetType().Name + ": " + ex.Message;
                        }
                    }

                    _processed.Set();
                }

                var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                var remain = PeriodMilliseconds - (int)elapsedMs;
                if (remain > 0 && _run)
                {
                    System.Threading.Thread.Sleep(remain);
                }
            }
        }

        private bool TryTakeFrame(
            out PassthroughFrame frame,
            out RigidTransform camera,
            out bool hasOdom,
            out RigidTransform odom)
        {
            lock (_sync)
            {
                hasOdom = _hasOdom;
                odom = _odom;
                if (!_hasFrame)
                {
                    frame = default;
                    camera = RigidTransform.Identity;
                    return false;
                }

                frame = _latestFrame;
                camera = _latestCamera;
                _hasFrame = false;
                return true;
            }
        }

        private IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
        {
            if (_detector == null)
            {
                return Array.Empty<AprilTagDetection>();
            }

            var detections = _detector.Detect(frame);
            return detections ?? Array.Empty<AprilTagDetection>();
        }

        private void ApplyAnchorCorrection()
        {
            if (!FeatureEnabled || _anchor == null)
            {
                return;
            }

            RobotDetectionSnapshot snap;
            lock (_sync)
            {
                snap = Snapshot;
            }

            if (snap.State != RobotTrackState.Locked)
            {
                return;
            }

            if (!_anchor.IsEstablished)
            {
                if (_anchor.TryEstablish(Pose.identity) && _anchor.TryGetPose(out var established))
                {
                    _anchorPose = RigidTransform.FromUnityPose(established);
                    _hasAnchorPose = true;
                }

                return;
            }

            if (!_anchor.TryGetPose(out var pose))
            {
                return;
            }

            var current = RigidTransform.FromUnityPose(pose);
            if (!_hasAnchorPose)
            {
                _anchorPose = current;
                _hasAnchorPose = true;
                return;
            }

            var correction = current.Mul(_anchorPose.Inverse());
            _anchorPose = current;
            if (correction.IsApproximatelyIdentity(1e-4, 1e-3))
            {
                return;
            }

            if (WorkerRunning)
            {
                lock (_sync)
                {
                    _pendingCorrection = _hasPendingCorrection
                        ? correction.Mul(_pendingCorrection)
                        : correction;
                    _hasPendingCorrection = true;
                }

                return;
            }

            lock (_pipelineGate)
            {
                _pipeline.ApplyWorldCorrection(correction);
                snap = _pipeline.Capture(snap.TimestampSeconds);
            }

            lock (_sync)
            {
                Snapshot = snap;
            }
        }
    }
}
