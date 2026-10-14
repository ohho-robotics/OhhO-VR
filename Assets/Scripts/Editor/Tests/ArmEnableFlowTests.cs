using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Manip;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Edit-mode coverage for arm enable, REAL confirmation, clutch hold, and
    /// the joint step cap. The scripted pick does not touch hardware.
    /// TODO(OHH-221): a real-arm video is still required. TODO(OHH-94): servo
    /// homes are not calibrated here.
    /// </summary>
    public class ArmEnableFlowTests
    {
        private const float Dt = 0.1f;

        private GameObject _origin;
        private HandIK6DofScheme _scheme;
        private FakeRobotLink _link;

        [SetUp]
        public void SetUp()
        {
            _origin = new GameObject("arm-enable-test-origin");
            _origin.transform.position = new Vector3(0f, RobotDescriptor.So101.BaseHeight, 0f);
            Use(ArmDeployment.Sim);
        }

        [TearDown]
        public void TearDown()
        {
            if (_origin != null) Object.DestroyImmediate(_origin);
        }

        [Test]
        public void Sim_RequestEnable_ArmsAndPublishes()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Sim);
            var signal = flow.RequestEnable();
            Assert.IsTrue(signal.Publish);
            Assert.IsTrue(signal.Value);
            Assert.AreEqual(ArmEnablePhase.Armed, flow.Phase);
            Assert.IsFalse(flow.MotionAllowed);
        }

        [Test]
        public void Real_Request_DoesNotPublishUntilConfirm()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Real);
            var request = flow.RequestEnable();
            Assert.IsFalse(request.Publish);
            Assert.AreEqual(ArmEnablePhase.AwaitingRealConfirmation, flow.Phase);

            var again = flow.RequestEnable();
            Assert.IsFalse(again.Publish);
            Assert.AreEqual(ArmEnablePhase.AwaitingRealConfirmation, flow.Phase);

            var confirm = flow.ConfirmReal();
            Assert.IsTrue(confirm.Publish);
            Assert.IsTrue(confirm.Value);
            Assert.AreEqual(ArmEnablePhase.Armed, flow.Phase);
            Assert.IsFalse(flow.ClutchEngaged);
        }

        [Test]
        public void Real_ConfirmWithoutPendingRequest_DoesNothing()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Real);
            var confirm = flow.ConfirmReal();
            Assert.IsFalse(confirm.Publish);
            Assert.AreEqual(ArmEnablePhase.Off, flow.Phase);
        }

        [Test]
        public void SameStep_EnableAndConfirm_DoesNotSkipRealGate()
        {
            var gate = new ArmSessionGate();
            gate.Bind(new RobotProfile { Deployment = ArmDeployment.Real });
            var signal = gate.Consume(new ArmOperatorInput
            {
                EnableRequested = true,
                ConfirmRequested = true,
            });
            Assert.IsFalse(signal.Publish);
            Assert.AreEqual(ArmEnablePhase.AwaitingRealConfirmation, gate.Flow.Phase);
        }

        [Test]
        public void Disarm_IsImmediate()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Sim);
            flow.RequestEnable();
            var off = flow.Disarm();
            Assert.IsTrue(off.Publish);
            Assert.IsFalse(off.Value);
            Assert.AreEqual(ArmEnablePhase.Off, flow.Phase);
        }

        [Test]
        public void EmergencyStop_DisarmsAndBlocksEnableUntilCleared()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Sim);
            flow.RequestEnable();
            var stop = flow.LatchEmergencyStop();
            Assert.IsTrue(stop.Publish);
            Assert.IsFalse(stop.Value);
            Assert.IsTrue(flow.EmergencyStopLatched);
            Assert.IsFalse(flow.RequestEnable().Publish);
            Assert.AreEqual(ArmEnablePhase.Off, flow.Phase);

            flow.ReleaseEmergencyStop();
            Assert.IsFalse(flow.EmergencyStopLatched);
            Assert.AreEqual(ArmEnablePhase.Off, flow.Phase);
            Assert.IsTrue(flow.RequestEnable().Value);
        }

        [Test]
        public void Clutch_IgnoredWhileOff_EngagesOnlyWhenArmed()
        {
            var flow = new ArmEnableFlow();
            flow.Reset(ArmDeployment.Sim);
            flow.SetClutch(true);
            Assert.IsFalse(flow.ClutchEngaged);
            flow.RequestEnable();
            flow.SetClutch(true);
            Assert.IsTrue(flow.MotionAllowed);
            flow.SetClutch(false);
            Assert.IsTrue(flow.TorqueEnabled);
            Assert.IsFalse(flow.MotionAllowed);
        }

        [Test]
        public void JointDeltaCap_CannotBeRaised()
        {
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, RobotDescriptor.ClampJointDelta(1f));
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, RobotDescriptor.ClampJointDelta(0f));
            Assert.AreEqual(0.05f, RobotDescriptor.ClampJointDelta(0.05f), 1e-6f);

            var current = new[] { 0f };
            JointRateLimiter.StepTowards(current, new[] { 2f }, 1f, new[] { -3f }, new[] { 3f });
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, current[0], 1e-4f);
        }

        [Test]
        public void Scheme_Sim_PublishesNoJointsUntilClutch()
        {
            var far = FarHand();
            Tick(Enable(trackingGood: true), far);
            Assert.IsTrue(_scheme.ArmEnabled);
            Assert.AreEqual(ArmEnablePhase.Armed, _scheme.EnablePhase);
            Assert.AreEqual(1, _link.Count<BoolMsg>());
            Assert.IsTrue(_link.Last<BoolMsg>().data);
            Assert.AreEqual(0, _link.Count<JointStateMsg>());

            Tick(new ArmOperatorInput { ClutchHeld = false, TrackingGood = true, DeltaTime = Dt }, far);
            Assert.AreEqual(0, _link.Count<JointStateMsg>());
        }

        [Test]
        public void Scheme_FirstClutchCommand_MatchesHeldPose_WhenHandIsFar()
        {
            Tick(Enable(true), FarHand());
            Tick(Clutch(trackingGood: true, pinch: 0f), FarHand());
            var msg = _link.Last<JointStateMsg>();
            Assert.IsNotNull(msg);
            AssertMatchesHome(msg);
            Assert.AreEqual(1, _link.Count<JointStateMsg>());
        }

        [Test]
        public void Scheme_StillHand_DoesNotChaseAbsolutePose()
        {
            var far = FarHand(HoldPinch());
            Tick(Enable(true), far);
            Tick(Clutch(true, HoldPinch()), far);
            Tick(Clutch(true, HoldPinch()), far);
            Tick(Clutch(true, HoldPinch()), far);
            foreach (var msg in _link.All<JointStateMsg>())
                AssertMatchesHome(msg);
        }

        [Test]
        public void Scheme_HandMotion_IsRateLimited()
        {
            var anchor = FarHand();
            Tick(Enable(true), anchor);
            Tick(Clutch(true, 0f), anchor);
            var moved = anchor;
            moved.Position += new Vector3(0.25f, 0.1f, -0.2f);
            Tick(Clutch(true, 0f), moved);
            Tick(Clutch(true, 0f), moved);
            AssertRateLimited(_link.All<JointStateMsg>());
        }

        [Test]
        public void Scheme_PoorTracking_JogsOneJoint_AndFirstSampleDoesNotJump()
        {
            Tick(Enable(true), FarHand());
            Tick(new ArmOperatorInput
            {
                ClutchHeld = true,
                TrackingGood = false,
                JogAxis = 1f,
                DeltaTime = 1f,
            }, FarHand());
            AssertMatchesHome(_link.Last<JointStateMsg>());

            Tick(new ArmOperatorInput
            {
                ClutchHeld = true,
                TrackingGood = false,
                JogAxis = 1f,
                DeltaTime = 1f,
            }, FarHand());
            var jogged = _link.Last<JointStateMsg>();
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, jogged.position[0], 1e-4f);
            for (int i = 1; i < jogged.position.Length; i++)
                Assert.AreEqual(0f, jogged.position[i], 1e-4f);
        }

        [Test]
        public void Scheme_GoodTracking_IgnoresJogStick()
        {
            var hand = FarHand(HoldPinch());
            Tick(Enable(true), hand);
            var clutched = Clutch(true, HoldPinch());
            clutched.JogAxis = 1f;
            Tick(clutched, hand);
            Tick(clutched, hand);
            foreach (var msg in _link.All<JointStateMsg>())
                AssertMatchesHome(msg);
        }

        [Test]
        public void Scheme_RaisedProfileCap_IsClampedToSafetyLimit()
        {
            var profile = MakeProfile(ArmDeployment.Sim);
            profile.MaxJointDeltaRad = 1f;
            _scheme.Configure(profile, _origin.transform);
            Tick(Enable(true), FarHand());
            Tick(new ArmOperatorInput { ClutchHeld = true, TrackingGood = false, JogAxis = 1f, DeltaTime = 1f }, FarHand());
            Tick(new ArmOperatorInput { ClutchHeld = true, TrackingGood = false, JogAxis = 1f, DeltaTime = 1f }, FarHand());
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, _link.Last<JointStateMsg>().position[0], 1e-4f);
        }

        [Test]
        public void Scheme_Real_ConfirmGatesEnable_AndClutchDoesNotMoveBeforeIt()
        {
            Use(ArmDeployment.Real);
            Tick(Enable(true), FarHand());
            Assert.AreEqual(ArmEnablePhase.AwaitingRealConfirmation, _scheme.EnablePhase);
            Assert.IsFalse(_scheme.ArmEnabled);
            Assert.AreEqual(0, _link.Count<BoolMsg>());
            Assert.That(_scheme.StatusText, Does.Contain("REAL"));

            Tick(Clutch(true, 1f), FarHand());
            Assert.AreEqual(0, _link.Count<JointStateMsg>());
            Assert.AreEqual(0, _link.Count<BoolMsg>());

            Tick(new ArmOperatorInput { ConfirmRequested = true, DeltaTime = Dt }, FarHand());
            Assert.IsTrue(_scheme.ArmEnabled);
            Assert.AreEqual(1, _link.Count<BoolMsg>());
            Assert.IsTrue(_link.Last<BoolMsg>().data);
            Assert.AreEqual(0, _link.Count<JointStateMsg>());

            Tick(Clutch(true, 0f), FarHand());
            AssertMatchesHome(_link.Last<JointStateMsg>());
        }

        [Test]
        public void Scheme_SetArmEnabledTrue_DoesNotConfirmReal()
        {
            Use(ArmDeployment.Real);
            _scheme.SetArmEnabled(true, _link);
            Assert.AreEqual(ArmEnablePhase.AwaitingRealConfirmation, _scheme.EnablePhase);
            Assert.AreEqual(0, _link.Count<BoolMsg>());
            _scheme.SetArmEnabled(false, _link);
            Assert.AreEqual(ArmEnablePhase.Off, _scheme.EnablePhase);
        }

        [Test]
        public void Scheme_EmergencyStop_PublishesEnableFalse_AndBlocksMotion()
        {
            Tick(Enable(true), FarHand());
            Tick(Clutch(true, 0f), FarHand());
            int joints = _link.Count<JointStateMsg>();
            Assert.Greater(joints, 0);

            Tick(new ArmOperatorInput
            {
                EmergencyStop = true,
                ClutchHeld = true,
                EnableRequested = true,
                TrackingGood = true,
                DeltaTime = Dt,
            }, FarHand());
            Assert.IsFalse(_scheme.ArmEnabled);
            Assert.IsFalse(_link.Last<BoolMsg>().data);
            Assert.AreEqual(joints, _link.Count<JointStateMsg>());

            Tick(new ArmOperatorInput
            {
                EmergencyStop = false,
                EnableRequested = true,
                TrackingGood = true,
                DeltaTime = Dt,
            }, FarHand());
            Assert.IsTrue(_scheme.ArmEnabled);
            Assert.IsTrue(_link.Last<BoolMsg>().data);
        }

        [Test]
        public void SimPick_ScriptedInsideWorkspace_ClosesGripperWithoutJump()
        {
            // Scripted stand-in for a sim cube pick. There is no physics cube
            // and this does not contact a robot. A real pick video waits on
            // the physical SO-101 (OHH-94 / OHH-221).
            var cube = _origin.transform.position + new Vector3(0.05f, 0.02f, 0.04f);
            Assert.Less(Vector3.Distance(cube, _origin.transform.position), RobotDescriptor.So101.HandWorkspaceRadius);

            Tick(new ArmOperatorInput { ClutchHeld = true, TrackingGood = true, DeltaTime = Dt }, Hand(cube, 1f));
            Assert.AreEqual(0, _link.Count<JointStateMsg>(), "Clutch before enable must not move the arm.");

            Tick(Enable(true), Hand(cube, 0f));
            Assert.IsTrue(_scheme.ArmEnabled);
            Assert.AreEqual(0, _link.Count<JointStateMsg>());

            Tick(Clutch(true, 0f), Hand(cube, 0f));
            var held = _link.Last<JointStateMsg>();
            AssertMatchesHome(held);

            var reach = cube + new Vector3(0.02f, 0f, 0f);
            Tick(Clutch(true, 1f), Hand(reach, 1f));
            var closed = _link.Last<JointStateMsg>();
            AssertRateLimited(_link.All<JointStateMsg>());
            Assert.Less(closed.position[closed.position.Length - 1], held.position[held.position.Length - 1]);
            Assert.Less(Vector3.Distance(reach, _origin.transform.position), RobotDescriptor.So101.HandWorkspaceRadius);

            bool accepted = _scheme.EnablePhase == ArmEnablePhase.Armed
                && _link.Count<JointStateMsg>() >= 2
                && closed.position[closed.position.Length - 1] < 0f;
            Assert.IsTrue(accepted);
        }

        [Test]
        public void WorkspaceRings_LieOnTheSphere()
        {
            float radius = RobotDescriptor.So101.HandWorkspaceRadius;
            var ring = WorkspaceSphereRings.Ring(radius, Vector3.right, Vector3.up, 16);
            Assert.AreEqual(17, ring.Length);
            Assert.Less((ring[0] - ring[ring.Length - 1]).magnitude, 1e-4f);
            for (int i = 0; i < ring.Length; i++)
                Assert.AreEqual(radius, ring[i].magnitude, 1e-4f);
        }

        [Test]
        public void WorkspaceVisual_UsesDescriptorRadius()
        {
            var go = new GameObject("sphere-test");
            try
            {
                var visual = go.AddComponent<WorkspaceSphereVisual>();
                visual.Configure(RobotDescriptor.So101.HandWorkspaceRadius);
                Assert.AreEqual(RobotDescriptor.So101.HandWorkspaceRadius, visual.Radius, 1e-4f);
                visual.SetAppearance(ArmEnablePhase.AwaitingRealConfirmation, false, false);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Descriptor_IsTheOnlySo101GeometrySource()
        {
            Assert.AreEqual(0.117f, RobotDescriptor.So101.LinkLengths[0], 1e-6f);
            Assert.AreEqual(0.133f, RobotDescriptor.So101.LinkLengths[1], 1e-6f);
            Assert.AreEqual(0.080f, RobotDescriptor.So101.LinkLengths[2], 1e-6f);
            Assert.AreEqual(RobotDescriptor.So101.LinkLengths[0], RobotConfig.ARM_LINK1_LENGTH);
            Assert.AreEqual(RobotDescriptor.So101.LinkLengths[1], RobotConfig.ARM_LINK2_LENGTH);
            Assert.AreEqual(RobotDescriptor.So101.LinkLengths[2], RobotConfig.ARM_LINK3_LENGTH);
            Assert.AreEqual(RobotDescriptor.So101.BaseHeight, RobotConfig.ARM_BASE_HEIGHT);
            Assert.AreEqual(RobotDescriptor.So101.HandWorkspaceRadius, RobotConfig.HAND_WORKSPACE_RADIUS);
            Assert.AreEqual(RobotDescriptor.So101.JointNames[0], RobotConfig.ARM_JOINT_NAMES[0]);
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, 0.15f);

            var joints = RobotProfileFactory.CreateSo101Joints(new[] { 0.2f, 0f, 0f, 0f, 0f, 0f });
            Assert.AreEqual(0.2f, joints[0].Home, 1e-4f);
            Assert.AreEqual(RobotDescriptor.So101.JointNames[5], joints[5].Name);

            var sim = RobotProfileFactory.FromGarageRobot(Garage("simulated", "so101-arm"));
            Assert.AreEqual(ArmDeployment.Sim, sim.Deployment);
            Assert.IsFalse(sim.JointZerosCalibrated);
            Assert.AreEqual(RobotDescriptor.So101.LinkLengths[0], sim.ArmLinkLengths[0]);
            Assert.AreEqual(RobotDescriptor.So101.Reach, sim.ArmMaxReach, 1e-4f);
            Assert.AreEqual(0f, sim.Joints[0].Home, 1e-5f);
            Assert.AreEqual(RobotDescriptor.MaxJointDeltaRad, sim.MaxJointDeltaRad);

            var active = RobotProfileFactory.FromGarageRobot(Garage("active", "omnibot-pro"));
            Assert.AreEqual(ArmDeployment.Real, active.Deployment);
            Assert.IsFalse(active.JointZerosCalibrated);
            Assert.AreEqual(RobotDescriptor.So101.Reach, active.ArmMaxReach, 1e-4f);

            var missing = RobotProfileFactory.FromGarageRobot(Garage(null, "so101-arm"));
            Assert.AreEqual(ArmDeployment.Real, missing.Deployment);
        }

        [Test]
        public void JointZeroFile_UncalibratedNonZero_IsRejected()
        {
            var snap = So101JointZeroConfig.Parse(
                "{\"calibrated\": false, \"jointZeroOffsetsRad\": [0.4, 0, 0, 0, 0, 0], \"note\": \"not OHH-94\"}");
            Assert.IsFalse(snap.Calibrated);
            Assert.AreEqual(0f, snap.OffsetsRad[0], 1e-6f);
        }

        [Test]
        public void JointZeroFile_CalibratedOffsets_AreRead()
        {
            var snap = So101JointZeroConfig.Parse(
                "{\"calibrated\": true, \"jointZeroOffsetsRad\": [0.25, 0, 0, 0, 0, 0], \"note\": \"OHH-94 measured\"}");
            Assert.IsTrue(snap.Calibrated);
            Assert.AreEqual(0.25f, snap.OffsetsRad[0], 1e-4f);
        }

        [Test]
        public void ShippedJointZeroFile_IsUncalibratedZeros()
        {
            So101JointZeroConfig.ClearCache();
            var asset = Resources.Load<TextAsset>("so101_joint_zeros");
            Assert.IsNotNull(asset, "Assets/Resources/so101_joint_zeros.json must be imported.");
            var parsed = So101JointZeroConfig.Parse(asset.text);
            Assert.IsFalse(parsed.Calibrated);
            Assert.AreEqual(6, parsed.OffsetsRad.Length);
            for (int i = 0; i < parsed.OffsetsRad.Length; i++)
                Assert.AreEqual(0f, parsed.OffsetsRad[i], 1e-6f);
            Assert.That(parsed.Note, Does.Contain("OHH-94"));
            So101JointZeroConfig.ClearCache();
        }

        private void Use(ArmDeployment deployment)
        {
            _scheme = new HandIK6DofScheme();
            _scheme.Configure(MakeProfile(deployment), _origin.transform);
            _link = new FakeRobotLink();
        }

        private static RobotProfile MakeProfile(ArmDeployment deployment)
        {
            return new RobotProfile
            {
                RobotId = "so101-arm",
                Name = "SO-101",
                HasArm = true,
                ArmDof = 6,
                Deployment = deployment,
                Joints = RobotProfileFactory.CreateSo101Joints(new float[6]),
                Topics = new RobotTopics
                {
                    ArmCommands = "/arm/joint_commands",
                    ArmEnable = "/arm/enable",
                },
                HandWorkspaceRadius = RobotDescriptor.So101.HandWorkspaceRadius,
                ArmBaseHeight = RobotDescriptor.So101.BaseHeight,
                ArmMaxReach = RobotDescriptor.So101.Reach,
                ArmLinkLengths = (float[])RobotDescriptor.So101.LinkLengths.Clone(),
                IkLocation = IkLocation.Headset,
                MaxJointDeltaRad = RobotDescriptor.MaxJointDeltaRad,
            };
        }

        private static GarageRobot Garage(string status, string modelId)
        {
            return new GarageRobot
            {
                UserRobot = new UserRobot { Status = status, Name = "Arm" },
                Category = new VrCategory { Id = "industrial-arm" },
                RobotType = new VrRobotType { Id = "arm" },
                HardwareModel = new VrHardwareModel
                {
                    Id = modelId,
                    Name = modelId,
                    HasArm = true,
                    Locomotion = "fixed-base",
                    Specs = new Dictionary<string, string> { { "dof", "6" } },
                },
            };
        }

        private void Tick(ArmOperatorInput input, HandPose right)
        {
            var left = new HandPose();
            _scheme.FeedArmInput(input);
            _scheme.Tick(ref right, ref left, _link);
        }

        private static ArmOperatorInput Enable(bool trackingGood)
        {
            return new ArmOperatorInput
            {
                EnableRequested = true,
                TrackingGood = trackingGood,
                DeltaTime = Dt,
            };
        }

        private static ArmOperatorInput Clutch(bool trackingGood, float pinch)
        {
            return new ArmOperatorInput
            {
                ClutchHeld = true,
                TrackingGood = trackingGood,
                DeltaTime = Dt,
            };
        }

        private HandPose FarHand() => FarHand(0f);

        private HandPose FarHand(float pinch)
        {
            return Hand(_origin.transform.position + new Vector3(1.5f, 0.4f, 1.2f), pinch);
        }

        private static HandPose Hand(Vector3 position, float pinch)
        {
            return new HandPose
            {
                Tracked = true,
                Quality = HandTrackingQuality.Good,
                Position = position,
                Rotation = Quaternion.identity,
                PinchStrength = pinch,
            };
        }

        /// <summary>Pinch that maps to the kinematic gripper home (0 rad).</summary>
        private static float HoldPinch()
        {
            var d = RobotDescriptor.So101;
            int last = d.JointCount - 1;
            return Mathf.InverseLerp(d.JointMax[last], d.JointMin[last], d.JointZeroRad[last]);
        }

        private static void AssertMatchesHome(JointStateMsg msg)
        {
            Assert.IsNotNull(msg);
            Assert.IsNotNull(msg.position);
            var joints = RobotProfileFactory.CreateSo101Joints(new float[6]);
            Assert.AreEqual(joints.Length, msg.position.Length);
            for (int i = 0; i < joints.Length; i++)
                Assert.AreEqual(joints[i].Home, msg.position[i], 1e-4f, joints[i].Name);
        }

        private static void AssertRateLimited(List<JointStateMsg> samples)
        {
            Assert.GreaterOrEqual(samples.Count, 2);
            for (int i = 1; i < samples.Count; i++)
            {
                var a = samples[i - 1].position;
                var b = samples[i].position;
                Assert.AreEqual(a.Length, b.Length);
                for (int j = 0; j < a.Length; j++)
                    Assert.LessOrEqual(Mathf.Abs(b[j] - a[j]), RobotDescriptor.MaxJointDeltaRad + 1e-4f);
            }
        }

        private sealed class FakeRobotLink : IRobotLink
        {
            public bool IsConnected { get; set; } = true;
            private readonly List<object> _messages = new List<object>();

            public void Advertise(string topic, string type) { }
            public void Publish<T>(string topic, T msg) => _messages.Add(msg);
            public void Subscribe(string topic, string type, System.Action<string> callback) { }
            public void Unsubscribe(string topic) { }

            public int Count<T>() where T : class
            {
                int n = 0;
                for (int i = 0; i < _messages.Count; i++)
                    if (_messages[i] is T) n++;
                return n;
            }

            public T Last<T>() where T : class
            {
                for (int i = _messages.Count - 1; i >= 0; i--)
                    if (_messages[i] is T msg) return msg;
                return null;
            }

            public List<T> All<T>() where T : class
            {
                var list = new List<T>();
                for (int i = 0; i < _messages.Count; i++)
                    if (_messages[i] is T msg) list.Add(msg);
                return list;
            }
        }
    }
}
