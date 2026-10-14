using System.IO;
using NUnit.Framework;
using OmniBot.VR.AprilTagSpike;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class AprilTagSpikeTests
    {
        [Test]
        public void PermissionId_MatchesVendoredSdkConstant()
        {
            Assert.AreEqual("horizonos.permission.HEADSET_CAMERA", HeadsetCameraPermission.PermissionId);
            Assert.AreEqual(74, HeadsetCameraPermission.MinimumHorizonOsVersion);
            Assert.AreEqual(
                OVRPermissionsRequester.PassthroughCameraAccessPermission,
                HeadsetCameraPermission.PermissionId);
            Assert.AreEqual(
                HeadsetCameraPermission.PermissionId,
                OVRPermissionsRequester.GetPermissionId(
                    OVRPermissionsRequester.Permission.PassthroughCameraAccess));
        }

        [Test]
        public void Editor_ReportsPermissionGranted_WhichIsNotAHeadsetResult()
        {
            Assert.IsTrue(OVRPermissionsRequester.IsPermissionGranted(
                OVRPermissionsRequester.Permission.PassthroughCameraAccess));
        }

        [Test]
        public void Config_TargetsTag36h11At720p()
        {
            Assert.AreEqual("tag36h11", AprilTagSpikeConfig.Family);
            Assert.AreEqual(1280, AprilTagSpikeConfig.CaptureWidth);
            Assert.AreEqual(720, AprilTagSpikeConfig.CaptureHeight);
            Assert.AreEqual(0.5f, AprilTagSpikeConfig.MinRangeMeters);
            Assert.AreEqual(3f, AprilTagSpikeConfig.MaxRangeMeters);
            Assert.AreEqual(5, AprilTagSpikeConfig.ProtocolRangeMeters.Length);
            Assert.AreEqual(0.5f, AprilTagSpikeConfig.ProtocolRangeMeters[0]);
            Assert.AreEqual(3f, AprilTagSpikeConfig.ProtocolRangeMeters[4]);
            Assert.Contains(
                AprilTagPoseError.AcceptanceRangeMeters,
                AprilTagSpikeConfig.ProtocolRangeMeters);
        }

        [Test]
        public void PositionError_KnownTriangle_IsFiveMeters()
        {
            var error = AprilTagPoseError.PositionErrorMeters(
                new Vector3(3f, 0f, 0f),
                new Vector3(0f, 4f, 0f));
            Assert.AreEqual(5f, error, 1e-5f);
            Assert.AreEqual(0f, AprilTagPoseError.PositionErrorMeters(Vector3.one, Vector3.one), 1e-6f);
        }

        [Test]
        public void PositionAcceptance_IsStrictlyUnderTwoCentimeters()
        {
            Assert.IsTrue(AprilTagPoseError.MeetsPositionAcceptance(0.015f));
            Assert.IsTrue(AprilTagPoseError.MeetsPositionAcceptance(0.0199f));
            Assert.IsFalse(AprilTagPoseError.MeetsPositionAcceptance(0.02f));
            Assert.IsFalse(AprilTagPoseError.MeetsPositionAcceptance(0.021f));
            Assert.AreEqual(0.02f, AprilTagPoseError.AcceptanceMaxPositionErrorMeters);
        }

        [Test]
        public void AngularError_NinetyDegrees_AndDoubleCover()
        {
            var quarter = AprilTagPoseError.AngularErrorDegrees(
                Quaternion.identity,
                Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual(90f, quarter, 0.05f);

            var q = Quaternion.Euler(10f, 20f, 30f);
            var neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            Assert.AreEqual(0f, AprilTagPoseError.AngularErrorDegrees(q, neg), 0.05f);
            Assert.AreEqual(0f, AprilTagPoseError.AngularErrorDegrees(q, q), 0.05f);
        }

        [Test]
        public void Hertz_AndRateGate()
        {
            Assert.AreEqual(10f, AprilTagPoseError.Hertz(100, 10f), 1e-5f);
            Assert.AreEqual(15f, AprilTagPoseError.Hertz(30, 2f), 1e-5f);
            Assert.AreEqual(0f, AprilTagPoseError.Hertz(5, 0f));
            Assert.AreEqual(0f, AprilTagPoseError.Hertz(-1, 10f));
            Assert.IsTrue(AprilTagPoseError.MeetsRateAcceptance(10f));
            Assert.IsFalse(AprilTagPoseError.MeetsRateAcceptance(9.99f));
            Assert.AreEqual(10f, AprilTagPoseError.AcceptanceMinHertz);
            Assert.AreEqual(1.5f, AprilTagPoseError.AcceptanceRangeMeters);
        }

        [Test]
        public void AcceptanceRange_AllowsOneCentimeterOfTapeError()
        {
            Assert.IsTrue(AprilTagPoseError.IsAcceptanceRange(1.5f));
            Assert.IsTrue(AprilTagPoseError.IsAcceptanceRange(1.49f));
            Assert.IsTrue(AprilTagPoseError.IsAcceptanceRange(1.51f));
            Assert.IsFalse(AprilTagPoseError.IsAcceptanceRange(1.48f));
            Assert.IsFalse(AprilTagPoseError.IsAcceptanceRange(0.5f));
            Assert.IsFalse(AprilTagPoseError.IsAcceptanceRange(3f));
        }

        [Test]
        public void OpenCvPoint_FlipsCameraY_AndComposeWorldUsesParentPose()
        {
            var flipped = AprilTagFrames.OpenCvCameraPointToUnityCameraPoint(new Vector3(0.1f, 0.2f, 1.5f));
            Assert.AreEqual(new Vector3(0.1f, -0.2f, 1.5f), flipped);

            var camera = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);
            var tagInCamera = new Pose(new Vector3(0f, 0f, 0.5f), Quaternion.identity);
            var world = AprilTagFrames.ComposeWorld(camera, tagInCamera);
            Assert.AreEqual(new Vector3(0f, 0f, 1.5f), world.position);
            Assert.AreEqual(Quaternion.identity, world.rotation);

            var yaw = Quaternion.Euler(0f, 90f, 0f);
            var turned = AprilTagFrames.ComposeWorld(
                new Pose(Vector3.zero, yaw),
                new Pose(Vector3.forward, Quaternion.identity));
            Assert.AreEqual(yaw * Vector3.forward, turned.position);
            Assert.AreEqual(yaw, turned.rotation);
        }

        [Test]
        public void StubDetector_IsNotReady_AndReturnsNothing()
        {
            IAprilTagDetector detector = new StubAprilTagDetector();
            Assert.IsFalse(detector.IsReady);
            Assert.AreEqual(AprilTagSpikeConfig.StubDetectorName, detector.Name);
            var frame = Frame720p();
            Assert.AreEqual(0, detector.Detect(frame).Count);
        }

        [Test]
        public void ManualBaseline_ReturnsPlacedPose_AndIgnoresTheFrame()
        {
            var placed = Detection(0, new Vector3(0f, 0f, 1.5f), Quaternion.identity);
            var baseline = new ManualPlacementBaseline();
            Assert.IsFalse(baseline.IsReady);
            Assert.AreEqual(0, baseline.Detect(Frame720p()).Count);

            baseline.SetPlaced(placed);
            Assert.IsTrue(baseline.IsReady);
            Assert.AreEqual(AprilTagSpikeConfig.ManualBaselineName, baseline.Name);

            var first = baseline.Detect(Frame720p());
            var second = baseline.Detect(default);
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(placed.PositionMeters, first[0].PositionMeters);
            Assert.AreEqual(placed.PositionMeters, second[0].PositionMeters);
            Assert.AreEqual(0, first[0].Hamming);
            Assert.IsTrue(first[0].CountsTowardHertz);
        }

        [Test]
        public void Pipeline_ComparesDetectorToBaseline_ByTagId()
        {
            var frame = Frame720p();
            var detected = new ManualPlacementBaseline(
                Detection(0, new Vector3(0f, 0f, 1.5f), Quaternion.identity));
            var baseline = new ManualPlacementBaseline(
                Detection(0, new Vector3(0.01f, 0f, 1.5f), Quaternion.identity));

            Assert.IsTrue(AprilTagSpikePipeline.TryPositionError(detected, baseline, frame, 0, out var error));
            Assert.AreEqual(0.01f, error, 1e-4f);
            Assert.IsTrue(AprilTagPoseError.MeetsPositionAcceptance(error));

            var far = new ManualPlacementBaseline(
                Detection(0, new Vector3(0.05f, 0f, 1.5f), Quaternion.identity));
            Assert.IsTrue(AprilTagSpikePipeline.TryPositionError(far, baseline, frame, 0, out var farError));
            Assert.AreEqual(0.04f, farError, 1e-4f);
            Assert.IsFalse(AprilTagPoseError.MeetsPositionAcceptance(farError));

            Assert.IsFalse(AprilTagSpikePipeline.TryPositionError(detected, baseline, frame, 4, out _));
            Assert.IsFalse(AprilTagSpikePipeline.TryPositionError(
                new StubAprilTagDetector(), baseline, frame, 0, out _));
        }

        [Test]
        public void HammingReject_DoesNotCountTowardHertzOrThePipeline()
        {
            var noisy = new AprilTagDetection(0, AprilTagSpikeConfig.Family, Vector3.zero, Quaternion.identity, 1, 50f);
            Assert.IsFalse(noisy.CountsTowardHertz);

            var detector = new ManualPlacementBaseline(noisy);
            var baseline = new ManualPlacementBaseline(
                Detection(0, Vector3.zero, Quaternion.identity));
            Assert.IsFalse(AprilTagSpikePipeline.TryPositionError(detector, baseline, Frame720p(), 0, out _));
        }

        [Test]
        public void FrameSource_FakeRoundTrips720p_UnavailableDoesNot()
        {
            var intrinsics = new PinholeIntrinsics(600f, 600f, 640f, 360f, 1280, 720);
            Assert.IsTrue(intrinsics.IsValid);
            var frame = new PassthroughFrame(1280, 720, intrinsics, new Pose(Vector3.zero, Quaternion.identity), 12.5);
            Assert.IsTrue(frame.HasMatchingResolution);

            var fake = new FakePassthroughFrameSource { Frame = frame };
            Assert.IsTrue(fake.IsAvailable);
            Assert.IsTrue(fake.TryGetLatest(out var got));
            Assert.AreEqual(1280, got.Width);
            Assert.AreEqual(720, got.Height);
            Assert.AreEqual(600f, got.Intrinsics.Fx);
            Assert.AreEqual(12.5, got.TimestampSeconds, 1e-6);

            var missing = new UnavailablePassthroughFrameSource();
            Assert.IsFalse(missing.IsAvailable);
            Assert.IsFalse(missing.TryGetLatest(out _));
            StringAssert.Contains("com.meta.xr.mrutilitykit", missing.UnavailableReason);
            StringAssert.Contains("203.0.0", missing.UnavailableReason);
        }

        [Test]
        public void MeasurementSample_GatesOnlyTheOnePointFiveMeterRow()
        {
            var pass = new AprilTagMeasurementSample(
                1.5f, 12f, 40f, 8f, 35f, 0.015f, 1.2f, "native-tag36h11");
            Assert.IsTrue(pass.MeetsSpikeAcceptance);
            Assert.AreEqual("yes", pass.GateLabel);

            var slow = new AprilTagMeasurementSample(
                1.5f, 9f, 40f, 8f, 35f, 0.015f, 1.2f, "native-tag36h11");
            Assert.IsFalse(slow.MeetsSpikeAcceptance);
            Assert.AreEqual("no", slow.GateLabel);

            var exactTwoCm = new AprilTagMeasurementSample(
                1.5f, 12f, 40f, 8f, 90f, 0.02f, 0f, "native-tag36h11");
            Assert.IsFalse(exactTwoCm.MeetsSpikeAcceptance);

            var farButAccurate = new AprilTagMeasurementSample(
                3f, 12f, 10f, 4f, 20f, 0.01f, 0.5f, "native-tag36h11");
            Assert.IsFalse(farButAccurate.MeetsSpikeAcceptance);
            Assert.AreEqual("record", farButAccurate.GateLabel);

            var row = AprilTagMeasurementLog.FormatRow(pass);
            StringAssert.Contains("1.50", row);
            StringAssert.Contains("12.00", row);
            StringAssert.Contains("0.015", row);
            StringAssert.Contains("native-tag36h11", row);
            StringAssert.Contains("yes", row);
        }

        [Test]
        public void ProtocolTemplate_ListsEveryRange_AndMarksOnlyTheGate()
        {
            var table = AprilTagMeasurementLog.FormatProtocolTemplate();
            StringAssert.Contains(AprilTagMeasurementLog.Header, table);
            StringAssert.Contains("| 0.50 |", table);
            StringAssert.Contains("| 1.00 |", table);
            StringAssert.Contains("| 1.50 |", table);
            StringAssert.Contains("| 2.00 |", table);
            StringAssert.Contains("| 3.00 |", table);
            StringAssert.Contains("| gate |", table);
            Assert.AreEqual(1, CountOccurrences(table, "| gate |"));
            Assert.AreEqual(4, CountOccurrences(table, "| record |"));
        }

        [Test]
        public void Host_StaysInert_AndDoesNotRequestTheCamera()
        {
            var go = new GameObject("apriltag-spike-test");
            try
            {
                var host = go.AddComponent<AprilTagSpikeHost>();
                Assert.IsFalse(host.RequestsHeadsetCameraPermission);
                Assert.IsFalse(host.RunsOnStart);
                Assert.IsFalse(host.Detector.IsReady);
                Assert.AreEqual(AprilTagSpikeConfig.StubDetectorName, host.Detector.Name);
                Assert.IsFalse(host.Frames.IsAvailable);
                Assert.IsFalse(host.Baseline.HasPlacement);
                Assert.AreEqual(0, host.Detector.Detect(default).Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Scene1_AndBuildSettings_DoNotReferenceTheSpikeHost()
        {
            var assets = Application.dataPath;
            var metaPath = Path.Combine(assets, "Ohho", "AR", "AprilTagSpike", "AprilTagSpikeHost.cs.meta");
            var guid = ReadGuid(File.ReadAllText(metaPath));
            Assert.AreEqual(32, guid.Length);

            var scene = File.ReadAllText(Path.Combine(assets, "scene1.unity"));
            StringAssert.DoesNotContain(guid, scene);

            var project = Directory.GetParent(assets).FullName;
            var buildSettings = File.ReadAllText(Path.Combine(project, "ProjectSettings", "EditorBuildSettings.asset"));
            StringAssert.DoesNotContain("AprilTag", buildSettings);
            StringAssert.Contains("Assets/scene1.unity", buildSettings);
        }

        private static PassthroughFrame Frame720p()
        {
            var intrinsics = new PinholeIntrinsics(600f, 600f, 640f, 360f, 1280, 720);
            return new PassthroughFrame(
                AprilTagSpikeConfig.CaptureWidth,
                AprilTagSpikeConfig.CaptureHeight,
                intrinsics,
                new Pose(Vector3.zero, Quaternion.identity),
                1d);
        }

        private static AprilTagDetection Detection(int id, Vector3 position, Quaternion rotation)
        {
            return new AprilTagDetection(id, AprilTagSpikeConfig.Family, position, rotation, 0, 1f);
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

        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var start = 0;
            while (start <= text.Length - token.Length)
            {
                var found = text.IndexOf(token, start, System.StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                count++;
                start = found + token.Length;
            }

            return count;
        }
    }
}
