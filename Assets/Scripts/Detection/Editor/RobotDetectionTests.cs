using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using OmniBot.VR.AprilTagSpike;
using OmniBot.VR.Control;
using OmniBot.VR.Detection;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class RobotDetectionTests
    {
        private static readonly int[] AllTags = { 0, 1, 2 };

        [Test]
        public void Calibration_MatchesNominalOhh223Mounts()
        {
            var cal = OmniBotTagCalibration.Nominal;
            Assert.AreEqual("tag36h11", OmniBotTagCalibration.Family);
            Assert.AreEqual(AprilTagSpikeConfig.Family, OmniBotTagCalibration.Family);
            StringAssert.Contains("OHH-223", OmniBotTagCalibration.NominalNote);
            StringAssert.Contains("nominal", OmniBotTagCalibration.NominalNote);
            Assert.AreEqual(3, cal.Mounts.Count);

            AssertMount(cal, 0, "apriltag_front_link", 0.137855339, 0.0, 0.208355339, 0.0, 0.208355339, 0.137855339);
            AssertMount(cal, 1, "apriltag_rear_link", -0.092144661, -0.07, 0.208355339, 0.07, 0.208355339, -0.092144661);
            AssertMount(cal, 2, "apriltag_arm_base_link", -0.078, 0.079855339, 0.208355339, -0.079855339, 0.208355339, -0.078);
            Assert.IsFalse(cal.TryGet(9, out _));
        }

        [Test]
        public void Limits_MatchTheIssueRatesAndErrorBound()
        {
            Assert.AreEqual(10f, RobotDetectionLimits.MinRateHz);
            Assert.AreEqual(15f, RobotDetectionLimits.MaxRateHz);
            Assert.GreaterOrEqual(RobotDetectionLimits.DefaultRateHz, RobotDetectionLimits.MinRateHz);
            Assert.LessOrEqual(RobotDetectionLimits.DefaultRateHz, RobotDetectionLimits.MaxRateHz);
            Assert.AreEqual(2.0, RobotDetectionLimits.LockWithinSeconds);
            Assert.AreEqual(0.03, RobotDetectionLimits.OverlayErrorMeters);
            Assert.AreEqual(0.2, RobotDetectionLimits.ReferenceDriveSpeedMetersPerSecond);
            Assert.AreEqual(83, RobotDetectionLimits.PeriodMilliseconds(12f));
            Assert.AreEqual(100, RobotDetectionLimits.PeriodMilliseconds(10f));
            Assert.AreEqual(67, RobotDetectionLimits.PeriodMilliseconds(15f));
            Assert.AreEqual(100, RobotDetectionLimits.PeriodMilliseconds(1f));
            Assert.AreEqual(67, RobotDetectionLimits.PeriodMilliseconds(100f));
        }

        [Test]
        public void OdomConversion_RosForwardAndYaw_MatchUnityAxes()
        {
            var forward = DetectionFrames.OdomPoseToUnity(
                RigidTransform.FromQuaternion(1, 0, 0, 0, 0, 0, 1));
            Assert.AreEqual(0, forward.X, 1e-9);
            Assert.AreEqual(0, forward.Y, 1e-9);
            Assert.AreEqual(1, forward.Z, 1e-9);

            var half = Math.Sqrt(0.5);
            var yaw = DetectionFrames.OdomPoseToUnity(
                RigidTransform.FromQuaternion(0, 0, 0, 0, 0, half, half));
            yaw.TransformDirection(0, 0, 1, out var x, out var y, out var z);
            Assert.AreEqual(-1, x, 1e-6);
            Assert.AreEqual(0, y, 1e-6);
            Assert.AreEqual(0, z, 1e-6);
        }

        [Test]
        public void Quaternion_MatchesUnityVectorRotation()
        {
            var rotation = RigidTransform.RotationY(0.7, 0.1, -0.2, 0.3);
            Assert.IsTrue(rotation.TryGetQuaternion(out var qx, out var qy, out var qz, out var qw));
            var unity = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
            var vector = new Vector3(0.2f, -0.4f, 0.8f);
            var rotated = unity * vector;
            rotation.TransformDirection(vector.x, vector.y, vector.z, out var x, out var y, out var z);
            Assert.AreEqual(x, rotated.x, 1e-5);
            Assert.AreEqual(y, rotated.y, 1e-5);
            Assert.AreEqual(z, rotated.z, 1e-5);
        }

        [Test]
        public void TagToBase_RoundTripsThroughTheCameraFrame()
        {
            var truth = RigidTransform.RotationY(0.4, 0.3, 0.0, 1.2);
            var camera = RigidTransform.RotationY(0.35, 0.2, 1.25, -0.55);
            var cal = OmniBotTagCalibration.Nominal;
            for (var i = 0; i < AllTags.Length; i++)
            {
                Assert.IsTrue(cal.TryGet(AllTags[i], out var mount));
                var recovered = RecoverBase(truth, camera, mount, 0, 0, 0);
                Assert.AreEqual(0, RigidTransform.PositionDistance(truth, recovered), 1e-6);
                // The published CAD matrices are truncated, so the round trip is a few hundredths of a milliradian.
                Assert.Less(RigidTransform.AngleRadians(truth, recovered), 1e-4);
            }

            var pipeline = new RobotDetectionPipeline();
            var snap = pipeline.Step(0.1, camera, DetectTags(truth, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, snap.State);
            Assert.Less(snap.PositionErrorMeters(truth.X, truth.Y, truth.Z), 1e-3);
            Assert.LessOrEqual(snap.SecondsToLock, RobotDetectionLimits.LockWithinSeconds);
            Assert.Greater(snap.Confidence, 0.8f);
            Assert.AreEqual(3, snap.InlierCount);
        }

        [Test]
        public void MultiTag_LocksWithinTwoSecondsOfTheFirstLook()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var snap = pipeline.Step(0.05, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, snap.State);
            Assert.LessOrEqual(snap.SecondsToLock, RobotDetectionLimits.LockWithinSeconds);
            Assert.GreaterOrEqual(snap.SecondsToLock, 0);
            Assert.AreEqual(0, snap.SecondsToLock, 1e-9);
        }

        [Test]
        public void SingleTag_LocksOnTheSecondSample_StillInsideTwoSeconds()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var first = pipeline.Step(
                0.0,
                camera,
                DetectTags(RigidTransform.Identity, camera, 0, 0, 0, new[] { 0 }, -1, 0),
                false,
                RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Searching, first.State);
            Assert.IsFalse(first.HasPose);

            var second = pipeline.Step(
                1.0 / 12.0,
                camera,
                DetectTags(RigidTransform.Identity, camera, 0, 0, 0, new[] { 0 }, -1, 0),
                false,
                RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, second.State);
            Assert.LessOrEqual(second.SecondsToLock, RobotDetectionLimits.LockWithinSeconds);
            Assert.AreEqual(1.0 / 12.0, second.SecondsToLock, 1e-9);
            Assert.Less(second.PositionErrorMeters(0, 0, 0), 1e-3);
        }

        [Test]
        public void OutlierTag_IsRejectedByTheCluster()
        {
            var truth = RigidTransform.RotationY(0.25, -0.15, 0.0, 0.35);
            var camera = CameraPose();
            var pipeline = new RobotDetectionPipeline();
            var snap = pipeline.Step(
                0.2,
                camera,
                DetectTags(truth, camera, 0, 0, 0, AllTags, outlierId: 2, outlierX: 0.5),
                false,
                RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, snap.State);
            Assert.AreEqual(2, snap.InlierCount);
            Assert.Less(snap.PositionErrorMeters(truth.X, truth.Y, truth.Z), 0.01);
        }

        [Test]
        public void TemporalOutlier_DoesNotPullALockedPose()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var locked = pipeline.Step(0.0, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, locked.State);

            var jumped = pipeline.Step(
                0.1,
                camera,
                DetectTags(RigidTransform.Translation(0.5, 0, 0), camera, 0, 0, 0, AllTags, -1, 0),
                false,
                RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, jumped.State);
            Assert.Less(jumped.PositionErrorMeters(0, 0, 0), RobotDetectionLimits.OverlayErrorMeters);
            Assert.Greater(jumped.PositionErrorMeters(0.5, 0, 0), 0.2);
        }

        [Test]
        public void Lost_AfterTimeoutWithoutADetection()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            pipeline.Step(0.0, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            var still = pipeline.Step(RobotDetectionLimits.LostTimeoutSeconds, camera, Array.Empty<AprilTagDetection>(), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, still.State);

            var lost = pipeline.Step(
                RobotDetectionLimits.LostTimeoutSeconds + 0.05,
                camera,
                Array.Empty<AprilTagDetection>(),
                false,
                RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Lost, lost.State);
            Assert.IsFalse(lost.HasPose);
            Assert.AreEqual(0f, lost.Confidence);
        }

        [Test]
        public void Relock_OnTheNextDetectionAfterLost()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            pipeline.Step(0.0, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            pipeline.Step(1.05, camera, Array.Empty<AprilTagDetection>(), false, RigidTransform.Identity);
            var moved = RigidTransform.Translation(0, 0, 0.8);
            var relock = pipeline.Step(1.1, camera, DetectTags(moved, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Locked, relock.State);
            Assert.IsTrue(relock.HasPose);
            Assert.Less(relock.PositionErrorMeters(0, 0, 0.8), 1e-3);
            Assert.Greater(relock.Confidence, 0.5f);
        }

        [Test]
        public void Confidence_DropsAsTheDetectionAges_AndIsZeroWhenLost()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var fresh = pipeline.Step(0.0, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            var aged = pipeline.Step(0.5, camera, Array.Empty<AprilTagDetection>(), false, RigidTransform.Identity);
            Assert.Greater(fresh.Confidence, aged.Confidence);
            Assert.Greater(aged.Confidence, 0f);
            var lost = pipeline.Step(1.1, camera, Array.Empty<AprilTagDetection>(), false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Lost, lost.State);
            Assert.AreEqual(0f, lost.Confidence);
        }

        [Test]
        public void HammingAndUnknownIds_DoNotLock()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var real = DetectTags(RigidTransform.Identity, camera, 0, 0, 0, new[] { 0 }, -1, 0)[0];
            var hamming = new AprilTagDetection(real.Id, real.Family, real.PositionMeters, real.Rotation, 2, real.DecisionMargin);
            var unknown = new AprilTagDetection(9, real.Family, real.PositionMeters, real.Rotation, 0, real.DecisionMargin);
            var wrongFamily = new AprilTagDetection(real.Id, "tagStandard41h12", real.PositionMeters, real.Rotation, 0, real.DecisionMargin);
            var snap = pipeline.Step(0.0, camera, new[] { hamming, unknown, wrongFamily }, false, RigidTransform.Identity);
            Assert.AreEqual(RobotTrackState.Searching, snap.State);
            Assert.IsFalse(snap.HasPose);
        }

        [Test]
        public void DrivingAtPointTwo_StaysInsideThreeCentimetersBetweenDetections()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var noise = new[] { 0.012, -0.009, 0.004, -0.011, 0.007, -0.003 };
            var noiseIndex = 0;
            var maxError = 0.0;
            const double speed = RobotDetectionLimits.ReferenceDriveSpeedMetersPerSecond;
            for (var i = 0; i < 48; i++)
            {
                var time = i / 12.0;
                var truth = RigidTransform.Translation(0, 0, speed * time);
                var odom = OdomUnity(speed * time);
                AprilTagDetection[] detections = Array.Empty<AprilTagDetection>();
                if (i % 6 == 0)
                {
                    var lateral = noise[noiseIndex % noise.Length];
                    noiseIndex++;
                    detections = DetectTags(truth, camera, lateral, 0, 0, AllTags, -1, 0);
                }

                var snap = pipeline.Step(time, camera, detections, true, odom);
                if (i == 0)
                {
                    Assert.AreEqual(RobotTrackState.Locked, snap.State);
                    Assert.LessOrEqual(snap.SecondsToLock, RobotDetectionLimits.LockWithinSeconds);
                }

                Assert.AreEqual(RobotTrackState.Locked, snap.State);
                var error = snap.PositionErrorMeters(truth.X, truth.Y, truth.Z);
                if (error > maxError)
                {
                    maxError = error;
                }

                if (i == 5)
                {
                    Assert.AreEqual(speed * time, snap.Z, RobotDetectionLimits.OverlayErrorMeters);
                    Assert.Greater(Math.Abs(snap.Z), RobotDetectionLimits.OverlayErrorMeters);
                }
            }

            Assert.Less(maxError, RobotDetectionLimits.OverlayErrorMeters);
        }

        [Test]
        public void AnchorJump_MovesTheTrackedPoseWithTheSessionOrigin()
        {
            var pipeline = new RobotDetectionPipeline();
            var camera = CameraPose();
            var locked = pipeline.Step(0.0, camera, DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0), false, RigidTransform.Identity);
            Assert.Less(locked.PositionErrorMeters(0, 0, 0), 1e-3);
            pipeline.ApplyWorldCorrection(RigidTransform.Translation(0, 0, 1));
            var corrected = pipeline.Capture(0.0);
            Assert.AreEqual(RobotTrackState.Locked, corrected.State);
            Assert.AreEqual(1.0, corrected.Z, 1e-3);
            Assert.AreEqual(0.0, corrected.X, 1e-3);
        }

        [Test]
        public void OdomParser_ReadsRosPose_AndAWrappedMessage()
        {
            Assert.IsTrue(OdomObservationParser.TryParse(OdomJson(1, 0, 0, 2), out var parsed));
            Assert.IsTrue(parsed.HasStamp);
            Assert.AreEqual(2, parsed.StampSeconds, 1e-9);
            Assert.AreEqual(0, parsed.BaseInOdomUnity.X, 1e-9);
            Assert.AreEqual(1, parsed.BaseInOdomUnity.Z, 1e-9);

            var wrapped = "{\"op\":\"publish\",\"topic\":\"/odom\",\"msg\":" + OdomJson(0.2, 0, 0, 1) + "}";
            Assert.IsTrue(OdomObservationParser.TryParse(wrapped, out var inner));
            Assert.AreEqual(0.2, inner.BaseInOdomUnity.Z, 1e-9);
            Assert.IsFalse(OdomObservationParser.TryParse("not-json", out _));
        }

        [Test]
        public void Service_SubscribesToTheProfileOdomTopic_AndPropagates()
        {
            var link = new FakeRobotLink();
            var profile = new RobotProfile
            {
                RobotId = "omnibot",
                Topics = new RobotTopics { Odom = "/ohh224/odom_delta" },
            };
            var detector = new ScriptedDetector();
            var camera = CameraPose();
            using (var host = new ServiceHost())
            {
                host.Service.SetSources(new HeadsetPassthroughCapture(), detector, new MemorySessionOriginAnchor());
                host.Service.ApplyPermission(headsetBuild: true, granted: true);
                host.Service.UseProfile(profile, link);
                Assert.AreEqual("/ohh224/odom_delta", link.SubscribedTopic);
                Assert.AreEqual("nav_msgs/Odometry", link.SubscribedType);
                Assert.AreEqual(1, link.SubscribeCalls);

                host.Service.PushOdomJsonForTests(OdomJson(0, 0, 0, 1));
                detector.Enqueue(DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0));
                var locked = host.Service.ProcessSample(FrameAt(camera, 0), 0);
                Assert.AreEqual(RobotTrackState.Locked, locked.State);

                host.Service.PushOdomJsonForTests(OdomJson(0.2, 0, 0, 3));
                host.Service.PushOdomJsonForTests(OdomJson(5, 0, 0, 2));
                detector.Enqueue(Array.Empty<AprilTagDetection>());
                var carried = host.Service.ProcessSample(FrameAt(camera, 0.1), 0.1);
                Assert.AreEqual(RobotTrackState.Locked, carried.State);
                Assert.AreEqual(0.2, carried.Z, 0.01);
                Assert.Greater(Math.Abs(carried.Z - 5.0), 1.0);
            }
        }

        [Test]
        public void PermissionDenied_DisablesDetection_AndDoesNotSubscribeOrAnchor()
        {
            var link = new FakeRobotLink();
            var anchor = new MemorySessionOriginAnchor();
            var profile = new RobotProfile
            {
                Topics = new RobotTopics { Odom = "/ohh224/odom_delta" },
            };
            using (var host = new ServiceHost())
            {
                host.Service.SetSources(null, new ScriptedDetector(), anchor);
                host.Service.ApplyPermission(headsetBuild: true, granted: false);
                host.Service.UseProfile(profile, link);
                Assert.IsFalse(host.Service.FeatureEnabled);
                Assert.IsTrue(host.Service.PermissionDenied);
                StringAssert.Contains("denied", host.Service.DisabledReason.ToLowerInvariant());
                StringAssert.Contains("camera", host.Service.PermissionRationale.ToLowerInvariant());
                StringAssert.Contains("robot", host.Service.PermissionRationale.ToLowerInvariant());
                Assert.AreEqual(0, link.SubscribeCalls);
                Assert.IsFalse(host.Service.StartWorker());
                var snap = host.Service.ProcessSample(FrameAt(CameraPose(), 0), 0);
                Assert.AreEqual(RobotTrackState.Searching, snap.State);
                Assert.IsFalse(anchor.IsEstablished);
            }
        }

        [Test]
        public void PermissionDenied_UsesManualPlacement_AndDoesNotReadCameraFrames()
        {
            var link = new FakeRobotLink();
            var anchor = new MemorySessionOriginAnchor();
            var frames = new CountingFrameSource();
            var profile = new RobotProfile
            {
                Topics = new RobotTopics { Odom = "/ohh224/odom_delta" },
            };
            using (var host = new ServiceHost())
            {
                host.Service.SetSources(frames, new ScriptedDetector(), anchor);
                host.Service.NotifyPermissionDenied();
                host.Service.UseProfile(profile, link);

                Assert.IsFalse(host.Service.FeatureEnabled);
                Assert.IsTrue(host.Service.PermissionDenied);
                Assert.IsTrue(host.Service.UsingManualPlacement);
                StringAssert.Contains("denied", host.Service.StatusMessage.ToLowerInvariant());
                StringAssert.Contains("manual", host.Service.StatusMessage.ToLowerInvariant());
                StringAssert.Contains("not used", host.Service.StatusMessage.ToLowerInvariant());
                Assert.IsFalse(host.Service.TryGetManualPlacement(out _));

                var placed = new Pose(new Vector3(1.25f, 0.1f, 0.4f), Quaternion.Euler(0f, 30f, 0f));
                host.Service.SetManualPlacement(placed);
                Assert.IsTrue(host.Service.ManualPlacement.HasPlacement);
                Assert.IsTrue(host.Service.TryGetManualPlacement(out var got));
                Assert.AreEqual(1.25f, got.position.x, 1e-4f);
                Assert.AreEqual(0.4f, got.position.z, 1e-4f);

                host.Service.SubmitCapturedFrame(FrameAt(CameraPose(), 1));
                InvokeUpdate(host.Service);
                var snap = host.Service.ProcessSample(FrameAt(CameraPose(), 1), 1);
                Assert.AreEqual(RobotTrackState.Searching, snap.State);
                Assert.IsFalse(snap.HasPose);
                Assert.IsFalse(anchor.IsEstablished);
                Assert.AreEqual(0, link.SubscribeCalls);
                Assert.IsFalse(host.Service.StartWorker());
                Assert.AreEqual(0, frames.Reads);
                Assert.IsTrue(host.Service.TryGetManualPlacement(out got));
                Assert.AreEqual(1.25f, got.position.x, 1e-4f);
            }
        }

        [Test]
        public void EditorSdkGrant_IsNotTreatedAsHeadsetPermission()
        {
            Assert.IsTrue(OVRPermissionsRequester.IsPermissionGranted(
                OVRPermissionsRequester.Permission.PassthroughCameraAccess));
            Assert.IsFalse(HeadsetCameraGate.IsHeadsetPlayer);
            Assert.IsFalse(HeadsetCameraGate.IsGrantedOnHeadset());
            HeadsetCameraGate.RequestAccess();

            using (var host = new ServiceHost())
            {
                host.Service.ApplyPermission(headsetBuild: false, granted: true);
                Assert.IsFalse(host.Service.FeatureEnabled);
                Assert.IsFalse(host.Service.PermissionDenied);
                StringAssert.Contains("editor", host.Service.DisabledReason.ToLowerInvariant());
                Assert.IsFalse(host.Service.CameraAvailable);
                StringAssert.Contains("mrutilitykit", host.Service.CameraUnavailableReason);
                StringAssert.Contains("203.0.0", host.Service.CameraUnavailableReason);
            }
        }

        [Test]
        public void Lock_EstablishesTheSessionOriginAnchor()
        {
            var anchor = new MemorySessionOriginAnchor();
            var detector = new ScriptedDetector();
            var camera = CameraPose();
            detector.Enqueue(DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0));
            using (var host = new ServiceHost())
            {
                host.Service.SetSources(new HeadsetPassthroughCapture(), detector, anchor);
                host.Service.ApplyPermission(headsetBuild: true, granted: true);
                Assert.IsFalse(anchor.IsEstablished);
                var snap = host.Service.ProcessSample(FrameAt(camera, 0.2), 0.2);
                Assert.AreEqual(RobotTrackState.Locked, snap.State);
                Assert.IsTrue(anchor.IsEstablished);
                Assert.IsTrue(anchor.TryGetPose(out var pose));
                Assert.AreEqual(0f, pose.position.x, 1e-5f);
                Assert.AreEqual(0f, pose.position.y, 1e-5f);
                Assert.AreEqual(0f, pose.position.z, 1e-5f);
                Assert.IsInstanceOf<MemorySessionOriginAnchor>(SessionOriginAnchors.CreateForThisRuntime());
            }
        }

        [Test]
        public void Rate_IsClampedToTheDetectorBand()
        {
            using (var host = new ServiceHost())
            {
                host.Service.RateHz = 100f;
                Assert.AreEqual(RobotDetectionLimits.MaxRateHz, host.Service.RateHz);
                Assert.AreEqual(RobotDetectionLimits.PeriodMilliseconds(15f), host.Service.PeriodMilliseconds);
                host.Service.RateHz = 1f;
                Assert.AreEqual(RobotDetectionLimits.MinRateHz, host.Service.RateHz);
            }
        }

        [Test]
        [Timeout(10000)]
        public void Worker_RunsTheDetectorOffTheMainThread()
        {
            var detector = new ThreadProbeDetector();
            var frame = FrameAt(RigidTransform.Identity, 1.25);
            using (var host = new ServiceHost())
            {
                host.Service.SetSources(new FakePassthroughFrameSource { Frame = frame }, detector, new MemorySessionOriginAnchor());
                host.Service.ApplyPermission(headsetBuild: true, granted: true);
                host.Service.RateHz = 15f;
                host.Service.SubmitCapturedFrame(frame);
                Assert.IsTrue(host.Service.StartWorker());
                Assert.IsTrue(
                    host.Service.WaitUntilProcessed(3000),
                    "worker did not finish: " + host.Service.LastWorkerError);
                Assert.IsNull(host.Service.LastWorkerError);
                Assert.Greater(detector.LastThreadId, 0);
                Assert.AreEqual(detector.LastThreadId, host.Service.LastDetectorThreadId);
                Assert.AreNotEqual(host.Service.MainThreadId, detector.LastThreadId);
            }
        }

        [Test]
        public void Scene1_DoesNotReferenceTheDetectionService()
        {
            var assets = Application.dataPath;
            var metaPath = Path.Combine(assets, "Scripts", "Detection", "RobotDetectionService.cs.meta");
            var guid = ReadGuid(File.ReadAllText(metaPath));
            Assert.AreEqual(32, guid.Length);
            var scene = File.ReadAllText(Path.Combine(assets, "scene1.unity"));
            StringAssert.DoesNotContain(guid, scene);
            var project = Directory.GetParent(assets).FullName;
            var buildSettings = File.ReadAllText(Path.Combine(project, "ProjectSettings", "EditorBuildSettings.asset"));
            StringAssert.DoesNotContain("RobotDetection", buildSettings);
            StringAssert.Contains("Assets/scene1.unity", buildSettings);
        }

        private static void AssertMount(
            OmniBotTagCalibration cal,
            int id,
            string frame,
            double rosX,
            double rosY,
            double rosZ,
            double unityX,
            double unityY,
            double unityZ)
        {
            Assert.IsTrue(cal.TryGet(id, out var mount));
            Assert.AreEqual(frame, mount.Frame);
            Assert.AreEqual(0.08, mount.SizeMeters, 0);
            Assert.AreEqual(rosX, mount.TagToBaseRos.X, 1e-12);
            Assert.AreEqual(rosY, mount.TagToBaseRos.Y, 1e-12);
            Assert.AreEqual(rosZ, mount.TagToBaseRos.Z, 1e-12);
            Assert.AreEqual(unityX, mount.TagToBaseUnity.X, 1e-9);
            Assert.AreEqual(unityY, mount.TagToBaseUnity.Y, 1e-9);
            Assert.AreEqual(unityZ, mount.TagToBaseUnity.Z, 1e-9);
            Assert.IsTrue(mount.TagToBaseUnity.TryGetQuaternion(out _, out _, out _, out _));
        }

        private static RigidTransform CameraPose()
        {
            return RigidTransform.RotationY(0.35, 0.2, 1.25, -0.55);
        }

        private static RigidTransform OdomUnity(double rosX)
        {
            return DetectionFrames.OdomPoseToUnity(RigidTransform.FromQuaternion(rosX, 0, 0, 0, 0, 0, 1));
        }

        private static RigidTransform RecoverBase(
            RigidTransform worldBase,
            RigidTransform camera,
            TagMount mount,
            double dx,
            double dy,
            double dz)
        {
            var worldTag = worldBase.Mul(mount.TagToBaseUnity).Translated(dx, dy, dz);
            var cameraUnity = camera.Inverse().Mul(worldTag);
            var openCv = DetectionFrames.UnityCameraToOpenCv(cameraUnity);
            var backCamera = DetectionFrames.OpenCvCameraToUnityCamera(openCv);
            return camera.Mul(backCamera).Mul(mount.TagToBaseUnity.Inverse());
        }

        private static AprilTagDetection[] DetectTags(
            RigidTransform worldBase,
            RigidTransform camera,
            double noiseX,
            double noiseY,
            double noiseZ,
            int[] ids,
            int outlierId,
            double outlierX)
        {
            var cal = OmniBotTagCalibration.Nominal;
            var list = new List<AprilTagDetection>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                Assert.IsTrue(cal.TryGet(ids[i], out var mount));
                var dx = ids[i] == outlierId ? outlierX : noiseX;
                var dy = ids[i] == outlierId ? 0 : noiseY;
                var dz = ids[i] == outlierId ? 0 : noiseZ;
                var worldTag = worldBase.Mul(mount.TagToBaseUnity).Translated(dx, dy, dz);
                var cameraUnity = camera.Inverse().Mul(worldTag);
                var openCv = DetectionFrames.UnityCameraToOpenCv(cameraUnity);
                Assert.IsTrue(openCv.TryGetQuaternion(out var qx, out var qy, out var qz, out var qw));
                list.Add(new AprilTagDetection(
                    ids[i],
                    OmniBotTagCalibration.Family,
                    new Vector3((float)openCv.X, (float)openCv.Y, (float)openCv.Z),
                    new Quaternion((float)qx, (float)qy, (float)qz, (float)qw),
                    0,
                    ids[i] == outlierId ? 5f : 100f));
            }

            return list.ToArray();
        }

        private static PassthroughFrame FrameAt(RigidTransform camera, double time)
        {
            return new PassthroughFrame(
                AprilTagSpikeConfig.CaptureWidth,
                AprilTagSpikeConfig.CaptureHeight,
                new PinholeIntrinsics(600f, 600f, 640f, 360f, 1280, 720),
                camera.ToUnityPose(),
                time);
        }

        private static string OdomJson(double x, double y, double z, int secs)
        {
            return "{\"header\":{\"stamp\":{\"secs\":" + secs
                + ",\"nsecs\":0},\"frame_id\":\"odom\"},\"child_frame_id\":\"base_link\",\"pose\":{\"pose\":{\"position\":{\"x\":"
                + x.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"y\":" + y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"z\":" + z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}},\"twist\":{\"twist\":{\"linear\":{\"x\":0,\"y\":0,\"z\":0},\"angular\":{\"x\":0,\"y\":0,\"z\":0}}}}";
        }

        private static string ReadGuid(string meta)
        {
            var lines = meta.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("guid:"))
                {
                    return trimmed.Substring("guid:".Length).Trim();
                }
            }

            return "";
        }

        private static void InvokeUpdate(RobotDetectionService service)
        {
            var update = typeof(RobotDetectionService).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(update);
            update.Invoke(service, null);
        }

        private sealed class CountingFrameSource : IPassthroughFrameSource
        {
            public int Reads;
            public bool IsAvailable => true;
            public string UnavailableReason => "";

            public bool TryGetLatest(out PassthroughFrame frame)
            {
                Reads++;
                frame = default;
                return false;
            }
        }

        private sealed class ServiceHost : IDisposable
        {
            public ServiceHost()
            {
                GameObject = new GameObject("robot-detection-test");
                Service = GameObject.AddComponent<RobotDetectionService>();
            }

            public GameObject GameObject { get; }
            public RobotDetectionService Service { get; }

            public void Dispose()
            {
                if (Service != null)
                {
                    Service.StopWorker();
                }

                if (GameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(GameObject);
                }
            }
        }

        private sealed class FakeRobotLink : IRobotLink
        {
            public int SubscribeCalls;
            public string SubscribedTopic;
            public string SubscribedType;
            public Action<string> Callback;
            public bool IsConnected => true;

            public void Advertise(string topic, string type)
            {
            }

            public void Publish<T>(string topic, T msg)
            {
            }

            public void Subscribe(string topic, string type, Action<string> callback)
            {
                SubscribeCalls++;
                SubscribedTopic = topic;
                SubscribedType = type;
                Callback = callback;
            }

            public void Unsubscribe(string topic)
            {
            }
        }

        private sealed class ScriptedDetector : IAprilTagDetector
        {
            private readonly Queue<IReadOnlyList<AprilTagDetection>> _frames = new Queue<IReadOnlyList<AprilTagDetection>>();

            public string Name => "scripted-tags";
            public bool IsReady => true;

            public void Enqueue(IReadOnlyList<AprilTagDetection> detections)
            {
                _frames.Enqueue(detections ?? Array.Empty<AprilTagDetection>());
            }

            public IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
            {
                if (_frames.Count == 0)
                {
                    return Array.Empty<AprilTagDetection>();
                }

                return _frames.Dequeue();
            }
        }

        private sealed class ThreadProbeDetector : IAprilTagDetector
        {
            public int LastThreadId;
            public string Name => "thread-probe";
            public bool IsReady => true;

            public IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
            {
                LastThreadId = Thread.CurrentThread.ManagedThreadId;
                return Array.Empty<AprilTagDetection>();
            }
        }
    }
}
