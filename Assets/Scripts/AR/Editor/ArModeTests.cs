using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using OmniBot.VR.AprilTagSpike;
using OmniBot.VR.AR;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Detection;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class ArModeTests
    {
        private static readonly int[] AllTags = { 0, 1, 2 };

        [Test]
        public void TrackingSnapshot_PlacesTheTwinOnTheDetectedPose()
        {
            var session = new ArModeSession();
            var rotation = Quaternion.Euler(0f, 25f, 0f);
            session.ApplyDetection(LockedAt(new Vector3(0.2f, 0f, 1.4f), rotation, 0.91f, 3));

            Assert.AreEqual(ArPlacementMode.Tracking, session.Mode);
            Assert.IsTrue(session.TwinVisible);
            Assert.AreEqual(0.2f, session.TwinPose.position.x, 1e-5f);
            Assert.AreEqual(1.4f, session.TwinPose.position.z, 1e-5f);
            Assert.Less(Quaternion.Angle(rotation, session.TwinPose.rotation), 0.05f);
        }

        [Test]
        public void ManualPlacement_WinsOnlyWhenDetectionIsNotLocked()
        {
            var session = new ArModeSession();
            var manual = new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 10f, 0f));
            session.PlaceManually(manual);
            Assert.AreEqual(ArPlacementMode.Manual, session.Mode);
            Assert.AreEqual(manual.position, session.TwinPose.position);

            var tracked = LockedAt(new Vector3(0.4f, 0f, 0.8f), Quaternion.identity, 0.8f, 3);
            session.ApplyDetection(tracked);
            Assert.AreEqual(ArPlacementMode.Tracking, session.Mode);
            Assert.AreEqual(0.4f, session.TwinPose.position.x, 1e-5f);

            session.ApplyDetection(Lost());
            Assert.AreEqual(ArPlacementMode.Manual, session.Mode);
            Assert.AreEqual(manual.position, session.TwinPose.position);
            Assert.IsTrue(session.TwinVisible);

            session.ClearManualPlacement();
            Assert.AreEqual(ArPlacementMode.Unplaced, session.Mode);
            Assert.IsFalse(session.TwinVisible);
        }

        [Test]
        public void InFrontOf_FlattensPitchAndDropsFromTheHead()
        {
            var head = new Pose(
                new Vector3(0.1f, 1.6f, 0.2f),
                Quaternion.Euler(-40f, 35f, 12f));
            var placed = ArManualPlacement.InFrontOf(head, 1.2f, 1.2f);

            var flat = head.rotation * Vector3.forward;
            flat.y = 0f;
            Assert.Greater(flat.sqrMagnitude, 1e-4f);
            flat.Normalize();
            var expected = head.position + flat * 1.2f - Vector3.up * 1.2f;
            Assert.AreEqual(expected.x, placed.position.x, 1e-4f);
            Assert.AreEqual(0.4f, placed.position.y, 1e-4f);
            Assert.AreEqual(expected.z, placed.position.z, 1e-4f);

            var forward = placed.rotation * Vector3.forward;
            Assert.AreEqual(0f, forward.y, 1e-4f);
            Assert.Greater(Vector3.Dot(forward, flat), 0.999f);
        }

        [Test]
        public void HudOffset_StaysInTheRobotFrameWhenTheBaseYaws()
        {
            var rotation = Quaternion.Euler(0f, 90f, 0f);
            var robot = new Pose(new Vector3(1f, 0f, 2f), rotation);
            var hud = ArAnchoring.HudWorldPosition(robot);
            var local = Quaternion.Inverse(rotation) * (hud - robot.position);
            Assert.AreEqual(ArAnchoring.HudLocalOffset.x, local.x, 1e-4f);
            Assert.AreEqual(ArAnchoring.HudLocalOffset.y, local.y, 1e-4f);
            Assert.AreEqual(ArAnchoring.HudLocalOffset.z, local.z, 1e-4f);
            Assert.AreEqual(1f + ArAnchoring.HudLocalOffset.z, hud.x, 1e-4f);
            Assert.AreEqual(2f - ArAnchoring.HudLocalOffset.x, hud.z, 1e-4f);
        }

        [Test]
        public void PositiveShoulderLift_RaisesTheElbow()
        {
            var geometry = ArTwinKinematics.ArmGeometry.OmniBotNominal;
            var flat = ArTwinKinematics.Chain(null, geometry);
            var elbow = IndexOf(flat, RobotConfig.ARM_JOINT_NAMES[2]);
            var flatPoint = ArTwinKinematics.PointInBase(flat, elbow);
            Assert.AreEqual(geometry.BaseHeight, flatPoint.y, 1e-4f);
            Assert.AreEqual(geometry.UpperArm, flatPoint.z, 1e-4f);

            var raised = ArTwinKinematics.Chain(
                new[] { new ArJointReadout(RobotConfig.ARM_JOINT_NAMES[1], 0.6f) },
                geometry);
            var raisedPoint = ArTwinKinematics.PointInBase(raised, elbow);
            Assert.Greater(raisedPoint.y, flatPoint.y + 0.02f);
            Assert.Less(raisedPoint.z, flatPoint.z);
        }

        [Test]
        public void Geometry_UsesProfileLinkLengths()
        {
            var profile = new RobotProfile
            {
                ArmBaseHeight = 0.42f,
                ArmLinkLengths = new[] { 0.2f, 0.3f, 0.05f },
            };
            var geometry = ArTwinKinematics.ArmGeometry.FromProfile(profile);
            Assert.AreEqual(0.42f, geometry.BaseHeight, 1e-6f);
            Assert.AreEqual(0.2f, geometry.UpperArm, 1e-6f);
            Assert.AreEqual(0.3f, geometry.Forearm, 1e-6f);
            Assert.AreEqual(0.05f, geometry.Wrist, 1e-6f);

            var fallback = ArTwinKinematics.ArmGeometry.FromProfile(new RobotProfile
            {
                ArmLinkLengths = new[] { 0f, -1f },
            });
            Assert.AreEqual(RobotConfig.ARM_LINK1_LENGTH, fallback.UpperArm, 1e-6f);
            Assert.AreEqual(RobotConfig.ARM_BASE_HEIGHT, fallback.BaseHeight, 1e-6f);
        }

        [Test]
        public void FakeLink_FillsJointAnglesAndSpeeds_WithoutPublishing()
        {
            var link = new FakeRobotLink();
            var profile = Profile("/arm/joint_states", "/odom");
            var source = new ArTelemetrySource();
            source.Attach(link, profile);

            Assert.AreEqual("/arm/joint_states", source.JointTopic);
            Assert.AreEqual("/odom", source.OdomTopic);
            Assert.AreEqual("sensor_msgs/JointState", link.Subscriptions["/arm/joint_states"].Type);
            Assert.AreEqual("nav_msgs/Odometry", link.Subscriptions["/odom"].Type);
            Assert.AreEqual(0, link.AdvertiseCalls);
            Assert.AreEqual(0, link.PublishCalls);
            Assert.AreEqual(0, link.UnsubscribeCalls);

            link.Subscriptions["/arm/joint_states"].Callback(JointJson("arm_shoulder_pan", 0.5f));
            link.Subscriptions["/odom"].Callback(OdomJson(0.125f, 0f, 0.25f));

            Assert.AreEqual(1, source.Model.Joints.Length);
            Assert.AreEqual("arm_shoulder_pan", source.Model.Joints[0].Name);
            Assert.AreEqual(0.5f, source.Model.Joints[0].Radians, 1e-5f);
            Assert.AreEqual(0.125f, source.Model.LinearSpeedMetersPerSecond, 1e-5f);
            Assert.AreEqual(0.25f, source.Model.AngularSpeedRadiansPerSecond, 1e-5f);

            var session = new ArModeSession();
            session.ApplyDetection(LockedAt(Vector3.zero, Quaternion.identity, 0.86f, 3));
            source.SetTrack(session);
            var text = ArHudText.Format(source.Model);
            StringAssert.Contains("TRACKING", text);
            StringAssert.Contains("conf 0.86", text);
            StringAssert.Contains("tags 3", text);
            StringAssert.Contains("lin 0.125 m/s   ang 0.250 rad/s", text);
            StringAssert.Contains("arm_shoulder_pan  28.6°", text);

            link.Subscriptions["/arm/joint_states"].Callback("{");
            Assert.AreEqual(0.5f, source.Model.Joints[0].Radians, 1e-5f);

            source.Detach();
            link.Subscriptions["/arm/joint_states"].Callback(JointJson("arm_elbow_flex", 1f));
            Assert.AreEqual("arm_shoulder_pan", source.Model.Joints[0].Name);
            Assert.AreEqual(0, link.UnsubscribeCalls);
            Assert.AreEqual(0, link.PublishCalls);
            Assert.IsFalse(source.IsAttached);
        }

        [Test]
        public void Telemetry_UsesTheProfileTopics()
        {
            var link = new FakeRobotLink();
            var source = new ArTelemetrySource();
            source.Attach(link, Profile("/custom/joints", "/custom/odom"));
            Assert.IsTrue(link.Subscriptions.ContainsKey("/custom/joints"));
            Assert.IsTrue(link.Subscriptions.ContainsKey("/custom/odom"));
            Assert.IsFalse(link.Subscriptions.ContainsKey(RobotConfig.TOPIC_ARM_STATES));
            Assert.AreEqual(0, link.AdvertiseCalls);
            Assert.AreEqual(0, link.PublishCalls);
        }

        [Test]
        public void PlanarSpeed_IncludesStrafe()
        {
            Assert.IsTrue(ArTelemetrySource.TryParseMotion(OdomJson(0.3f, 0.4f, -0.2f), out var linear, out var angular));
            Assert.AreEqual(0.5f, linear, 1e-4f);
            Assert.AreEqual(-0.2f, angular, 1e-5f);
        }

        [Test]
        public void FakeTags_LockTheOverlayOnTheRobotWithinThreeCentimetres()
        {
            var truth = RigidTransform.RotationY(0.2, 0.4, 0.0, 1.1);
            var camera = CameraPose();
            var detector = new MutableDetector
            {
                Next = DetectTags(truth, camera, 0, 0, 0, AllTags, -1, 0),
            };

            using (var fixture = new Fixture())
            {
                fixture.Service.SetSources(
                    new FakePassthroughFrameSource(),
                    detector,
                    new MemorySessionOriginAnchor());
                fixture.Service.ApplyPermission(headsetBuild: true, granted: true);
                var snap = fixture.Service.ProcessSample(FrameAt(camera, 0.2), 0.2);
                Assert.AreEqual(RobotTrackState.Locked, snap.State);
                Assert.Less(snap.PositionErrorMeters(truth.X, truth.Y, truth.Z), RobotDetectionLimits.OverlayErrorMeters);

                var link = new FakeRobotLink();
                fixture.Controller.Bind(fixture.Service, link, Profile("/arm/joint_states", "/odom"));
                var head = new Pose(new Vector3(0f, 1.6f, -0.5f), Quaternion.identity);
                fixture.Controller.Tick(head);

                Assert.AreEqual(ArPlacementMode.Tracking, fixture.Controller.Session.Mode);
                Assert.IsTrue(fixture.Controller.Twin.gameObject.activeSelf);
                Assert.AreEqual(Vector3.one, fixture.Controller.Twin.transform.localScale);
                Assert.AreEqual(ArModeController.OneToOneScale, 1f);
                Assert.Less(
                    Vector3.Distance(fixture.Controller.Twin.transform.position, snap.Pose.position),
                    0.0001f);
                Assert.Less(
                    Vector3.Distance(
                        fixture.Controller.Twin.transform.position,
                        new Vector3((float)truth.X, (float)truth.Y, (float)truth.Z)),
                    (float)RobotDetectionLimits.OverlayErrorMeters);
                Assert.Less(Quaternion.Angle(fixture.Controller.Twin.transform.rotation, snap.Pose.rotation), 0.5f);

                var chassis = fixture.Controller.Twin.transform.Find("chassis");
                Assert.IsNotNull(chassis);
                Assert.AreEqual(ArTwinKinematics.ChassisSizeMeters, chassis.localScale);

                var elbow = fixture.Controller.Twin.transform.Find(
                    "arm_shoulder_pan/arm_shoulder_lift/arm_elbow_flex");
                Assert.IsNotNull(elbow);
                link.Subscriptions["/arm/joint_states"].Callback(JointJson("arm_shoulder_lift", 0.7f));
                fixture.Controller.Tick(head);
                var chain = ArTwinKinematics.Chain(
                    fixture.Controller.Telemetry.Model.Joints,
                    ArTwinKinematics.ArmGeometry.OmniBotNominal);
                var expected = fixture.Controller.Twin.transform.TransformPoint(
                    ArTwinKinematics.PointInBase(chain, IndexOf(chain, "arm_elbow_flex")));
                Assert.Less(Vector3.Distance(elbow.position, expected), 0.0001f);
                Assert.Greater(elbow.position.y, snap.Pose.position.y + RobotConfig.ARM_BASE_HEIGHT);

                Assert.AreEqual(0, link.PublishCalls);
                Assert.AreEqual(0, link.AdvertiseCalls);
                Assert.AreEqual(0, link.UnsubscribeCalls);
            }
        }

        [Test]
        public void Relock_MovesTheTwinAndKeepsTheHudOnTheRobot()
        {
            var camera = CameraPose();
            var detector = new MutableDetector
            {
                Next = DetectTags(RigidTransform.Identity, camera, 0, 0, 0, AllTags, -1, 0),
            };

            using (var fixture = new Fixture())
            {
                fixture.Service.SetSources(new FakePassthroughFrameSource(), detector, new MemorySessionOriginAnchor());
                fixture.Service.ApplyPermission(true, true);
                fixture.Service.ProcessSample(FrameAt(camera, 0.0), 0.0);
                var link = new FakeRobotLink();
                fixture.Controller.Bind(fixture.Service, link, Profile("/arm/joint_states", "/odom"));
                var head = new Pose(new Vector3(-0.2f, 1.7f, -1.2f), Quaternion.identity);
                fixture.Controller.Tick(head);

                var manual = new Pose(new Vector3(-0.5f, 0f, 0.3f), Quaternion.Euler(0f, 15f, 0f));
                fixture.Controller.PlaceManually(manual);
                fixture.Controller.Tick(head);
                Assert.AreEqual(ArPlacementMode.Tracking, fixture.Controller.Session.Mode);
                Assert.Less(fixture.Controller.Twin.transform.position.magnitude, 0.03f);

                detector.Next = Array.Empty<AprilTagDetection>();
                var lost = fixture.Service.ProcessSample(FrameAt(camera, 1.05), 1.05);
                Assert.AreEqual(RobotTrackState.Lost, lost.State);
                fixture.Controller.Tick(head);
                Assert.AreEqual(ArPlacementMode.Manual, fixture.Controller.Session.Mode);
                Assert.Less(Vector3.Distance(fixture.Controller.Twin.transform.position, manual.position), 0.0001f);
                StringAssert.Contains("MANUAL", fixture.Controller.Hud.Text);
                StringAssert.Contains("detection lost", fixture.Controller.Hud.Text);

                var moved = RigidTransform.Translation(0, 0, 0.8);
                detector.Next = DetectTags(moved, camera, 0, 0, 0, AllTags, -1, 0);
                var relock = fixture.Service.ProcessSample(FrameAt(camera, 1.1), 1.1);
                Assert.AreEqual(RobotTrackState.Locked, relock.State);
                Assert.Less(relock.PositionErrorMeters(0, 0, 0.8), RobotDetectionLimits.OverlayErrorMeters);
                fixture.Controller.Tick(head);

                Assert.AreEqual(ArPlacementMode.Tracking, fixture.Controller.Session.Mode);
                Assert.Less(
                    Vector3.Distance(fixture.Controller.Twin.transform.position, relock.Pose.position),
                    0.0001f);
                Assert.Less(
                    Vector3.Distance(
                        fixture.Controller.Twin.transform.position,
                        new Vector3(0f, 0f, 0.8f)),
                    (float)RobotDetectionLimits.OverlayErrorMeters);

                var local = fixture.Controller.Hud.transform.localPosition;
                Assert.AreEqual(ArAnchoring.HudLocalOffset.x, local.x, 1e-4f);
                Assert.AreEqual(ArAnchoring.HudLocalOffset.y, local.y, 1e-4f);
                Assert.AreEqual(ArAnchoring.HudLocalOffset.z, local.z, 1e-4f);
                var anchored = fixture.Controller.Twin.transform.TransformPoint(ArAnchoring.HudLocalOffset);
                Assert.Less(Vector3.Distance(fixture.Controller.Hud.transform.position, anchored), 0.0001f);

                var towardHead = head.position - fixture.Controller.Hud.transform.position;
                towardHead.y = 0f;
                var facing = fixture.Controller.Hud.transform.forward;
                facing.y = 0f;
                Assert.Greater(Vector3.Dot(facing.normalized, towardHead.normalized), 0.9f);
                Assert.AreEqual(0, link.PublishCalls);
                Assert.AreEqual(0, link.UnsubscribeCalls);
            }
        }

        [Test]
        public void ManualPlacement_WorksWhenDetectionIsOffline()
        {
            using (var fixture = new Fixture())
            {
                var inactive = fixture.Service.ProcessSample(FrameAt(RigidTransform.Identity, 0.0), 0.0);
                Assert.IsFalse(inactive.HasPose);
                Assert.IsFalse(fixture.Service.FeatureEnabled);

                var link = new FakeRobotLink();
                fixture.Controller.Bind(fixture.Service, link, Profile("/arm/joint_states", "/odom"));
                fixture.Controller.Tick(new Pose(Vector3.zero, Quaternion.identity));
                Assert.AreEqual(ArPlacementMode.Unplaced, fixture.Controller.Session.Mode);
                Assert.IsFalse(fixture.Controller.Twin.gameObject.activeSelf);

                var head = new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
                fixture.Controller.PlaceInFrontOf(head);
                var expected = ArManualPlacement.InFrontOf(
                    head,
                    ArManualPlacement.DefaultDistanceMeters,
                    ArManualPlacement.DefaultDropMeters);
                Assert.AreEqual(ArPlacementMode.Manual, fixture.Controller.Session.Mode);
                Assert.IsTrue(fixture.Controller.Twin.gameObject.activeSelf);
                Assert.Less(Vector3.Distance(fixture.Controller.Twin.transform.position, expected.position), 0.0001f);
                Assert.Less(Quaternion.Angle(fixture.Controller.Twin.transform.rotation, expected.rotation), 0.05f);

                link.Subscriptions["/arm/joint_states"].Callback(JointJson("arm_gripper", 0.4f));
                link.Subscriptions["/odom"].Callback(OdomJson(0.125f, 0f, 0f));
                fixture.Controller.Tick(head);
                StringAssert.Contains("MANUAL", fixture.Controller.Hud.Text);
                StringAssert.Contains("arm_gripper  22.9°", fixture.Controller.Hud.Text);
                StringAssert.Contains("lin 0.125 m/s", fixture.Controller.Hud.Text);
                Assert.AreEqual(0, link.PublishCalls);
                Assert.AreEqual(0, link.AdvertiseCalls);
                Assert.AreEqual(0, link.UnsubscribeCalls);

                var external = new GameObject("external-twin");
                try
                {
                    fixture.Controller.SetExternalTwin(external.transform);
                    fixture.Controller.Tick(head);
                    Assert.Less(Vector3.Distance(external.transform.position, expected.position), 0.0001f);
                    Assert.AreEqual(Vector3.one, external.transform.localScale);
                    Assert.IsTrue(external.activeSelf);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(external);
                }
            }
        }

        [Test]
        public void Scene1_DoesNotReferenceArMode()
        {
            var assets = Application.dataPath;
            var metaPath = Path.Combine(assets, "Scripts", "AR", "ArModeController.cs.meta");
            var guid = ReadGuid(File.ReadAllText(metaPath));
            Assert.AreEqual(32, guid.Length);
            var scene = File.ReadAllText(Path.Combine(assets, "scene1.unity"));
            StringAssert.DoesNotContain(guid, scene);
        }

        private static RobotDetectionSnapshot LockedAt(Vector3 position, Quaternion rotation, float confidence, int inliers)
        {
            return new RobotDetectionSnapshot(
                RobotTrackState.Locked,
                true,
                position.x,
                position.y,
                position.z,
                rotation.x,
                rotation.y,
                rotation.z,
                rotation.w,
                confidence,
                inliers,
                1.0,
                0.0);
        }

        private static RobotDetectionSnapshot Lost()
        {
            return new RobotDetectionSnapshot(
                RobotTrackState.Lost,
                false,
                0, 0, 0,
                0, 0, 0, 1,
                0f,
                0,
                2.0,
                0.0);
        }

        private static RobotProfile Profile(string joints, string odom)
        {
            return new RobotProfile
            {
                Topics = new RobotTopics
                {
                    JointStates = joints,
                    Odom = odom,
                },
            };
        }

        private static int IndexOf(ArTwinKinematics.LinkPose[] chain, string name)
        {
            for (var i = 0; i < chain.Length; i++)
            {
                if (chain[i].Name == name)
                {
                    return i;
                }
            }

            Assert.Fail("missing link " + name);
            return -1;
        }

        private static string JointJson(string name, float radians)
        {
            return "{\"name\":[\"" + name + "\"],\"position\":["
                + radians.ToString("0.0######", CultureInfo.InvariantCulture) + "]}";
        }

        private static string OdomJson(float vx, float vy, float wz)
        {
            var culture = CultureInfo.InvariantCulture;
            return "{\"twist\":{\"twist\":{\"linear\":{\"x\":"
                + vx.ToString("0.0######", culture)
                + ",\"y\":" + vy.ToString("0.0######", culture)
                + ",\"z\":0},\"angular\":{\"x\":0,\"y\":0,\"z\":"
                + wz.ToString("0.0######", culture)
                + "}}},\"pose\":{\"pose\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}}";
        }

        private static RigidTransform CameraPose()
        {
            return RigidTransform.RotationY(0.35, 0.2, 1.25, -0.55);
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
            var calibration = OmniBotTagCalibration.Nominal;
            var list = new List<AprilTagDetection>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                Assert.IsTrue(calibration.TryGet(ids[i], out var mount));
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
                    100f));
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

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                ServiceObject = new GameObject("ar-detection-test");
                Service = ServiceObject.AddComponent<RobotDetectionService>();
                ControllerObject = new GameObject("ar-mode-test");
                Controller = ControllerObject.AddComponent<ArModeController>();
            }

            public GameObject ServiceObject { get; }
            public RobotDetectionService Service { get; }
            public GameObject ControllerObject { get; }
            public ArModeController Controller { get; }

            public void Dispose()
            {
                if (Service != null)
                {
                    Service.StopWorker();
                }

                if (ControllerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(ControllerObject);
                }

                if (ServiceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(ServiceObject);
                }
            }
        }

        private sealed class MutableDetector : IAprilTagDetector
        {
            public IReadOnlyList<AprilTagDetection> Next = Array.Empty<AprilTagDetection>();
            public string Name => "fake-tags";
            public bool IsReady => true;

            public IReadOnlyList<AprilTagDetection> Detect(PassthroughFrame frame)
            {
                return Next ?? Array.Empty<AprilTagDetection>();
            }
        }

        private sealed class FakeRobotLink : IRobotLink
        {
            public readonly Dictionary<string, Subscription> Subscriptions = new Dictionary<string, Subscription>();
            public int AdvertiseCalls;
            public int PublishCalls;
            public int UnsubscribeCalls;
            public bool IsConnected => true;

            public void Advertise(string topic, string type)
            {
                AdvertiseCalls++;
            }

            public void Publish<T>(string topic, T msg)
            {
                PublishCalls++;
            }

            public void Subscribe(string topic, string type, Action<string> callback)
            {
                Subscriptions[topic] = new Subscription
                {
                    Type = type,
                    Callback = callback,
                };
            }

            public void Unsubscribe(string topic)
            {
                UnsubscribeCalls++;
            }

            public sealed class Subscription
            {
                public string Type;
                public Action<string> Callback;
            }
        }
    }
}
