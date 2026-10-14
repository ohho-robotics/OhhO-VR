using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Core;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// In-memory fake transport for testing drive schemes without WebSocket networking.
    /// </summary>
    public class FakeRobotLink : IRobotLink
    {
        public bool IsConnected { get; set; } = true;
        public readonly List<(string topic, object msg)> Published = new List<(string, object)>();
        public readonly List<(string topic, string type)> Advertised = new List<(string, string)>();

        public void Advertise(string topic, string type) => Advertised.Add((topic, type));

        public void Publish<T>(string topic, T msg) => Published.Add((topic, msg));

        public void Subscribe(string topic, string type, Action<string> callback) { }

        public void Unsubscribe(string topic) { }

        public void Clear() => Published.Clear();
    }

    /// <summary>
    /// Unit tests for teleoperation drive schemes: deadman grip enforcement,
    /// emergency-stop latching/clearing, velocity clamping, and control-mode transitions.
    /// </summary>
    public class TeleopControlTests
    {
        private RobotProfile MakeOmniBotProfile()
        {
            return new RobotProfile
            {
                RobotId = "omnibot-pro",
                Name = "OmniBot Pro",
                Drive = DriveKind.Mecanum,
                BaseDof = 3,
                MaxLinVel = 0.2f,
                MaxAngVel = 1.0f,
                HasArm = true,
                ArmDof = 6,
                Topics = new RobotTopics
                {
                    CmdVel = "/cmd_vel/teleop",
                    ControlMode = "/control_mode",
                    EmergencyStop = "/emergency_stop"
                },
                RequireDeadman = false
            };
        }

        private FakeRobotLink _link;
        private MecanumDriveScheme _scheme;
        private RobotProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _link = new FakeRobotLink();
            _profile = MakeOmniBotProfile();
            _scheme = new MecanumDriveScheme();
            _scheme.Configure(_profile);
        }

        [Test]
        public void EmergencyStop_BothGripsPressed_ActivatesEStopAndPublishesZeroTwist()
        {
            var input = new TeleopInput
            {
                BothGrips = true,
                BothGripsPressed = true,
                LeftGrip = true,
                RightGrip = true,
                DeltaTime = 0.05f
            };

            _scheme.Tick(ref input, _link);

            Assert.IsTrue(_scheme.IsEmergencyStopActive);

            // Verify /emergency_stop published true
            var eStopMsg = _link.Published.Find(p => p.topic == _profile.Topics.EmergencyStop);
            Assert.IsNotNull(eStopMsg.msg, "EmergencyStop topic should be published");
            Assert.IsTrue(((BoolMsg)eStopMsg.msg).data);

            // Verify zero twist published to cmd_vel
            var cmdVelMsg = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNotNull(cmdVelMsg.msg, "CmdVel zero twist should be published on E-stop");
            var twist = (TwistMsg)cmdVelMsg.msg;
            Assert.AreEqual(0f, twist.linear.x, 0.0001f);
            Assert.AreEqual(0f, twist.linear.y, 0.0001f);
            Assert.AreEqual(0f, twist.angular.z, 0.0001f);
        }

        [Test]
        public void EmergencyStop_BothGripsReleased_ClearsEStop()
        {
            // Activate first
            var pressInput = new TeleopInput
            {
                BothGrips = true,
                BothGripsPressed = true,
                LeftGrip = true,
                RightGrip = true,
                DeltaTime = 0.05f
            };
            _scheme.Tick(ref pressInput, _link);
            Assert.IsTrue(_scheme.IsEmergencyStopActive);

            _link.Clear();

            // Release
            var releaseInput = new TeleopInput
            {
                BothGrips = false,
                BothGripsReleased = true,
                LeftGrip = false,
                RightGrip = false,
                DeltaTime = 0.05f
            };
            _scheme.Tick(ref releaseInput, _link);

            Assert.IsFalse(_scheme.IsEmergencyStopActive);
            var eStopMsg = _link.Published.Find(p => p.topic == _profile.Topics.EmergencyStop);
            Assert.IsNotNull(eStopMsg.msg, "EmergencyStop topic should publish false on release");
            Assert.IsFalse(((BoolMsg)eStopMsg.msg).data);
        }

        [Test]
        public void EmergencyStop_WhileActive_BlocksAllDriveCommands()
        {
            _scheme.TriggerEmergencyStop(_link);
            _link.Clear();

            // Provide maximum thumbstick deflection while e-stop is active
            var input = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                RightStick = new Vector2(1f, 0f),
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input, _link);

            // No non-zero velocity commands should be published
            foreach (var (topic, msg) in _link.Published)
            {
                if (topic == _profile.Topics.CmdVel && msg is TwistMsg twist)
                {
                    Assert.AreEqual(0f, twist.linear.x, "Linear X must be 0 while E-stop is active");
                    Assert.AreEqual(0f, twist.linear.y, "Linear Y must be 0 while E-stop is active");
                    Assert.AreEqual(0f, twist.angular.z, "Angular Z must be 0 while E-stop is active");
                }
            }
        }

        [Test]
        public void EmergencyStop_ProgrammaticTriggerAndClear()
        {
            _scheme.TriggerEmergencyStop(_link);
            Assert.IsTrue(_scheme.IsEmergencyStopActive);

            var eStopMsg = _link.Published.Find(p => p.topic == _profile.Topics.EmergencyStop);
            Assert.IsTrue(((BoolMsg)eStopMsg.msg).data);

            _link.Clear();
            _scheme.ClearEmergencyStop(_link);
            Assert.IsFalse(_scheme.IsEmergencyStopActive);

            var clearMsg = _link.Published.Find(p => p.topic == _profile.Topics.EmergencyStop);
            Assert.IsFalse(((BoolMsg)clearMsg.msg).data);
        }

        [Test]
        public void Deadman_WhenRequiredAndGripNotHeld_BlocksMotion()
        {
            _scheme.RequireDeadman = true;

            // Deflect thumbstick forward but do not hold any grip
            var input = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = false,
                RightGrip = false,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input, _link);

            // Should NOT publish drive velocity
            var cmdVel = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNull(cmdVel.msg, "Should not publish driving command without deadman grip");
        }

        [Test]
        public void Deadman_WhenRequiredAndGripHeld_PublishesVelocity()
        {
            _scheme.RequireDeadman = true;

            // Deflect thumbstick forward AND hold left grip (deadman)
            var input = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = true,
                RightGrip = false,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input, _link);

            var cmdVel = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNotNull(cmdVel.msg, "Should publish driving command when deadman grip is held");
            var twist = (TwistMsg)cmdVel.msg;
            Assert.Greater(twist.linear.x, 0f, "Forward velocity should be positive");
            Assert.LessOrEqual(twist.linear.x, _profile.MaxLinVel);
        }

        [Test]
        public void Deadman_WhenReleasedWhileDriving_ImmediatelyHaltsAndPublishesZero()
        {
            _scheme.RequireDeadman = true;

            // Frame 1: driving with deadman held
            var input1 = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = true,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input1, _link);
            Assert.IsTrue(_link.Published.Exists(p => p.topic == _profile.Topics.CmdVel));

            _link.Clear();

            // Frame 2: operator releases deadman grip while still holding stick
            var input2 = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = false,
                DeltaTime = 0.05f
            };
            _scheme.Tick(ref input2, _link);

            // Frame 2 must immediately publish zero twist to halt robot
            var zeroCmd = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNotNull(zeroCmd.msg, "Zero velocity must be published immediately on deadman release");
            var twist = (TwistMsg)zeroCmd.msg;
            Assert.AreEqual(0f, twist.linear.x, 0.0001f);
            Assert.AreEqual(0f, twist.linear.y, 0.0001f);
            Assert.AreEqual(0f, twist.angular.z, 0.0001f);
        }

        [Test]
        public void Deadman_BothGripsHeld_TriggersEStopInsteadOfDrive()
        {
            _scheme.RequireDeadman = true;

            var input = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = true,
                RightGrip = true,
                BothGrips = true,
                BothGripsPressed = true,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input, _link);

            Assert.IsTrue(_scheme.IsEmergencyStopActive);
            var eStopMsg = _link.Published.Find(p => p.topic == _profile.Topics.EmergencyStop);
            Assert.IsNotNull(eStopMsg.msg);
            Assert.IsTrue(((BoolMsg)eStopMsg.msg).data);
        }

        [Test]
        public void Deadman_WhenNotRequired_AllowsDrivingWithoutGrips()
        {
            _scheme.RequireDeadman = false;

            var input = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                LeftGrip = false,
                RightGrip = false,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref input, _link);

            var cmdVel = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNotNull(cmdVel.msg);
            Assert.Greater(((TwistMsg)cmdVel.msg).linear.x, 0f);
        }

        [Test]
        public void TurboMode_WithRightGripAlone_AppliesMultiplierClampedToMax()
        {
            _scheme.RequireDeadman = false;

            // Half deflection forward: ly = 0.5f. Turbo (right grip) applies 2x multiplier
            var normalInput = new TeleopInput
            {
                LeftStick = new Vector2(0f, 0.5f),
                RightGrip = false,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref normalInput, _link);
            var normalTwist = (TwistMsg)_link.Published.Find(p => p.topic == _profile.Topics.CmdVel).msg;

            _link.Clear();

            var turboInput = new TeleopInput
            {
                LeftStick = new Vector2(0f, 0.5f),
                RightGrip = true, // Turbo
                LeftGrip = false,
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref turboInput, _link);
            var turboTwist = (TwistMsg)_link.Published.Find(p => p.topic == _profile.Topics.CmdVel).msg;

            Assert.Greater(turboTwist.linear.x, normalTwist.linear.x, "Turbo should produce higher speed");
            Assert.LessOrEqual(turboTwist.linear.x, _profile.MaxLinVel, "Speed must be clamped to MaxLinVel");
        }

        [Test]
        public void ControlMode_SwitchesToTeleopWhileDriving_AndNav2AfterIdleTimeout()
        {
            // Frame 1: driving
            var drivingInput = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref drivingInput, _link);

            var modeMsg = _link.Published.Find(p => p.topic == _profile.Topics.ControlMode);
            Assert.IsNotNull(modeMsg.msg);
            Assert.AreEqual("teleop", ((StringMsg)modeMsg.msg).data);

            _link.Clear();

            // Advance idle time beyond 0.5s timeout (NavModeRevertDelay)
            var idleInput = new TeleopInput
            {
                LeftStick = Vector2.zero,
                DeltaTime = 0.6f
            };
            _scheme.Tick(ref idleInput, _link);

            var revertMsg = _link.Published.Find(p => p.topic == _profile.Topics.ControlMode);
            Assert.IsNotNull(revertMsg.msg);
            Assert.AreEqual("nav2", ((StringMsg)revertMsg.msg).data);
        }

        [Test]
        public void Stop_PublishesZeroTwistAndNav2Mode()
        {
            // Drive first to enter teleop mode
            var drivingInput = new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                DeltaTime = 0.1f
            };
            _scheme.Tick(ref drivingInput, _link);
            _link.Clear();

            // Stop
            _scheme.Stop(_link);

            var cmdVel = _link.Published.Find(p => p.topic == _profile.Topics.CmdVel);
            Assert.IsNotNull(cmdVel.msg, "Stop must publish zero twist");
            var twist = (TwistMsg)cmdVel.msg;
            Assert.AreEqual(0f, twist.linear.x, 0.0001f);

            var mode = _link.Published.Find(p => p.topic == _profile.Topics.ControlMode);
            Assert.IsNotNull(mode.msg, "Stop must publish idle control mode");
            Assert.AreEqual("nav2", ((StringMsg)mode.msg).data);
        }
    }
}
