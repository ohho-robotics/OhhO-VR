using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using OmniBot.VR.Core;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Manip;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Play-mode pass of the sim clutch. The first joint command after the
    /// trigger closes must be the held pose, even if the hand is elsewhere.
    /// Does not connect to a robot.
    /// </summary>
    public class ArmEnablePlayModeTests
    {
        [UnityTest]
        public IEnumerator SimClutch_FirstCommandMatchesHeldPose()
        {
            var origin = new GameObject("playmode-arm-workspace");
            origin.transform.position = new Vector3(0f, RobotDescriptor.So101.BaseHeight, 0f);
            var visual = origin.AddComponent<WorkspaceSphereVisual>();
            visual.Configure(RobotDescriptor.So101.HandWorkspaceRadius);

            var scheme = new HandIK6DofScheme();
            scheme.Configure(Profile(), origin.transform);
            var link = new FakeRobotLink();
            var hand = new HandPose
            {
                Tracked = true,
                Quality = HandTrackingQuality.Good,
                Position = origin.transform.position + new Vector3(0.35f, 0.15f, 0.25f),
                Rotation = Quaternion.identity,
            };
            var left = new HandPose();

            scheme.FeedArmInput(new ArmOperatorInput
            {
                EnableRequested = true,
                TrackingGood = true,
                DeltaTime = 0.05f,
            });
            scheme.Tick(ref hand, ref left, link);
            Assert.IsTrue(scheme.ArmEnabled);
            Assert.AreEqual(ArmDeployment.Sim, Profile().Deployment);
            Assert.AreEqual(0, link.Count<JointStateMsg>());
            yield return null;

            float elapsed = 0f;
            while (link.Count<JointStateMsg>() == 0 && elapsed < 2f)
            {
                scheme.FeedArmInput(new ArmOperatorInput
                {
                    ClutchHeld = true,
                    TrackingGood = true,
                });
                scheme.Tick(ref hand, ref left, link);
                yield return null;
                elapsed += Time.deltaTime;
            }

            var msg = link.Last<JointStateMsg>();
            Assert.IsNotNull(msg, "Clutch did not publish a joint command in play mode.");
            var homes = RobotProfileFactory.CreateSo101Joints(new float[6]);
            Assert.AreEqual(homes.Length, msg.position.Length);
            for (int i = 0; i < homes.Length; i++)
                Assert.AreEqual(homes[i].Home, msg.position[i], 1e-3f, homes[i].Name);
            Assert.AreEqual(RobotDescriptor.So101.HandWorkspaceRadius, visual.Radius, 1e-4f);

            Object.Destroy(origin);
            yield return null;
        }

        private static RobotProfile Profile()
        {
            return new RobotProfile
            {
                RobotId = "so101-arm",
                Name = "SO-101",
                HasArm = true,
                ArmDof = 6,
                Deployment = ArmDeployment.Sim,
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
        }
    }
}
