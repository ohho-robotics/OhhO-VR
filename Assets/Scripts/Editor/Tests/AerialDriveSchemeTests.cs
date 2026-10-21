using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// EditMode coverage for aerial mode-2 mapping, the grip deadman, the
    /// Ohho e-stop message, and the takeoff/land confirmation gate.
    /// </summary>
    public class AerialDriveSchemeTests
    {
        const float Lin = 12f;
        const float Ang = 3f;

        // Mode-2 radio (ELRS / EdgeTX mode 2), written down so the sign is not
        // "fixed" later:
        //   Left stick right  → rudder right → nose right.
        //   BODY_FRD positive yaw rate is nose-right (z-down, right-hand rule),
        //   which is the same sign sim-serve documents for angular.z.
        //   Left stick X is therefore not negated.
        //   A mode-2 ground scheme such as BaseController negates right-stick X
        //   because ROS ENU positive yaw is nose-left. That negation does not
        //   apply to this BODY_FRD yaw rate.
        [Test]
        public void LeftStickRight_IsPositiveBodyFrdYaw_NoseRight_Mode2Radio()
        {
            AerialTeleopCommand cmd = Map(lx: 1f, left: true);

            Assert.Greater(cmd.YawRate, 0f);
            Assert.AreEqual(Ang, cmd.YawRate, 1e-4f);
            Assert.IsTrue(cmd.Deadman);
            Assert.IsFalse(cmd.Estop);
        }

        [Test]
        public void LeftStickLeft_IsNegativeYaw_NoseLeft()
        {
            AerialTeleopCommand cmd = Map(lx: -1f, left: true);
            Assert.AreEqual(-Ang, cmd.YawRate, 1e-4f);
        }

        [Test]
        public void LeftStickUp_ClimbIsNegativeBodyFrdVz()
        {
            AerialTeleopCommand cmd = Map(ly: 1f, left: true);
            Assert.AreEqual(-Lin, cmd.Vz, 1e-4f);
            Assert.AreEqual(0f, cmd.Vx, 1e-4f);
        }

        [Test]
        public void LeftStickDown_DescentIsPositiveBodyFrdVz()
        {
            AerialTeleopCommand cmd = Map(ly: -1f, left: true);
            Assert.AreEqual(Lin, cmd.Vz, 1e-4f);
        }

        [Test]
        public void RightStickUp_IsForwardVx()
        {
            AerialTeleopCommand cmd = Map(ry: 1f, right: true);
            Assert.AreEqual(Lin, cmd.Vx, 1e-4f);
            Assert.AreEqual(0f, cmd.Vy, 1e-4f);
        }

        [Test]
        public void RightStickRight_IsRightVy()
        {
            AerialTeleopCommand cmd = Map(rx: 1f, right: true);
            Assert.AreEqual(Lin, cmd.Vy, 1e-4f);
        }

        [Test]
        public void DeadZone_AtPointOne_ContributesNothing()
        {
            Assert.AreEqual(0f, AerialDriveMapping.ApplyDeadZone(0.1f), 1e-6f);
            Assert.AreEqual(0f, AerialDriveMapping.ApplyDeadZone(-0.1f), 1e-6f);
            Assert.AreEqual(0f, AerialDriveMapping.ApplyDeadZone(0.09f), 1e-6f);

            AerialTeleopCommand cmd = Map(ry: 0.1f, left: true);
            Assert.AreEqual(0f, cmd.Vx, 1e-4f);
            Assert.IsTrue(cmd.Deadman);
        }

        [Test]
        public void DeadZone_MidStick_ScalesFromTheEdge()
        {
            // (0.55 - 0.1) / 0.9 = 0.5, then times the catalog cap.
            AerialTeleopCommand cmd = Map(ry: 0.55f, left: true);
            Assert.AreEqual(Lin * 0.5f, cmd.Vx, 1e-3f);
        }

        [Test]
        public void DeadmanRelease_ZeroVelocity_AndDeadmanFalse()
        {
            AerialTeleopCommand held = Map(lx: 1f, ly: 1f, rx: 1f, ry: 1f, left: true);
            Assert.IsTrue(held.Deadman);
            Assert.AreEqual(Lin, held.Vx, 1e-4f);
            Assert.AreEqual(-Lin, held.Vz, 1e-4f);

            AerialTeleopCommand released = Map(lx: 1f, ly: 1f, rx: 1f, ry: 1f);
            Assert.IsFalse(released.Deadman);
            Assert.IsFalse(released.Estop);
            Assert.AreEqual(AerialEstopEffect.None, released.EstopEffect);
            Assert.AreEqual(0f, released.Vx, 1e-4f);
            Assert.AreEqual(0f, released.Vy, 1e-4f);
            Assert.AreEqual(0f, released.Vz, 1e-4f);
            Assert.AreEqual(0f, released.YawRate, 1e-4f);
            Assert.IsNull(released.Flight);

            JObject json = Parse(released);
            Assert.IsFalse(json.Value<bool>("deadman"));
            Assert.IsFalse(json.Value<bool>("estop"));
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("x"), 1e-4f);
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("z"), 1e-4f);
            Assert.AreEqual(0f, json["velocity"]["angular"].Value<float>("z"), 1e-4f);
        }

        [Test]
        public void GripHeld_CenteredSticks_KeepsDeadmanWithZeroVelocity()
        {
            AerialTeleopCommand cmd = Map(left: true);
            Assert.IsTrue(cmd.Deadman);
            Assert.AreEqual(0f, cmd.Vx, 1e-4f);
            Assert.AreEqual(0f, cmd.Vz, 1e-4f);
            Assert.AreEqual(0f, cmd.YawRate, 1e-4f);
        }

        [Test]
        public void NoTurbo_RightGripDoesNotDoubleSpeed()
        {
            AerialTeleopCommand rightOnly = Map(ry: 1f, right: true);
            AerialTeleopCommand leftOnly = Map(ry: 1f, left: true);
            Assert.AreEqual(Lin, rightOnly.Vx, 1e-4f);
            Assert.AreEqual(leftOnly.Vx, rightOnly.Vx, 1e-4f);
            Assert.Less(rightOnly.Vx, Lin * 2f);
        }

        [Test]
        public void FullStick_DoesNotRequestTakeoffOrLand()
        {
            AerialTeleopCommand climb = Map(ly: 1f, left: true);
            AerialTeleopCommand descend = Map(ly: -1f, left: true);
            Assert.IsNull(climb.Flight);
            Assert.IsNull(descend.Flight);
            Assert.IsFalse(climb.Estop);
            Assert.IsFalse(descend.Estop);
        }

        [Test]
        public void BothGrips_PublishesEstopMessage_AndZeroVelocity()
        {
            AerialTeleopCommand cmd = Map(lx: 1f, ly: 1f, rx: 1f, ry: 1f, left: true, right: true, robotId: "px4-x500");

            Assert.IsTrue(cmd.Estop);
            Assert.IsFalse(cmd.Deadman);
            Assert.AreEqual(0f, cmd.Vx, 1e-4f);
            Assert.AreEqual(0f, cmd.Vy, 1e-4f);
            Assert.AreEqual(0f, cmd.Vz, 1e-4f);
            Assert.AreEqual(0f, cmd.YawRate, 1e-4f);
            Assert.IsNull(cmd.Flight);

            JObject json = Parse(cmd);
            Assert.IsTrue(json.Value<bool>("estop"));
            Assert.IsFalse(json.Value<bool>("deadman"));
            Assert.AreEqual(AerialDriveMapping.FrameBodyFrd, json.Value<string>("frame"));
            Assert.IsNull(json["flight"]);
            Assert.AreEqual(1, ((JObject)json["velocity"]["angular"]).Count);
            AssertForbidden(json);
        }

        [Test]
        public void BothGrips_X500_EstopLands_NotMotorKill()
        {
            foreach (string id in new[] { "px4-x500", "px4-sitl", "x500", "px4-quad" })
            {
                AerialTeleopCommand cmd = Map(left: true, right: true, robotId: id);
                Assert.AreEqual(AerialEstopEffect.Land, cmd.EstopEffect, id);
                Assert.IsTrue(cmd.Estop, id);
                AssertForbidden(Parse(cmd));
            }

            AerialTeleopCommand named = Map(left: true, right: true, robotId: "custom-quad", robotName: "Holybro X500");
            Assert.AreEqual(AerialEstopEffect.Land, named.EstopEffect);
        }

        [Test]
        public void BothGrips_Crazyflie_EstopMayCutMotors()
        {
            foreach (string id in new[] { "crazyflie-sim", "crazyflie-cf21", "crazyswarm" })
            {
                AerialTeleopCommand cmd = Map(left: true, right: true, robotId: id);
                Assert.AreEqual(AerialEstopEffect.MotorCut, cmd.EstopEffect, id);
                Assert.IsTrue(cmd.Estop, id);
                JObject json = Parse(cmd);
                Assert.IsTrue(json.Value<bool>("estop"), id);
                Assert.IsNull(json["flight"], id);
                AssertForbidden(json);
            }
        }

        [Test]
        public void BothGrips_DropsAQueuedFlightRequest()
        {
            var pending = new AerialFlightRequest { Request = "takeoff", AltM = 2.5, Confirm = AerialFlightConfirmation.Phrase };
            AerialTeleopCommand cmd = AerialDriveMapping.Map(new AerialMappingInput
            {
                RobotId = "px4-x500",
                MaxLinVel = Lin,
                MaxAngVel = Ang,
                LeftGrip = true,
                RightGrip = true,
                LeftY = 1f,
                PendingFlight = pending,
            });
            Assert.IsTrue(cmd.Estop);
            Assert.IsNull(cmd.Flight);
        }

        [Test]
        public void Takeoff_WithoutConfirmation_IsRefused()
        {
            AssertRefused(AerialFlightConfirmation.Takeoff(null));
            AssertRefused(AerialFlightConfirmation.Takeoff(""));
            AssertRefused(AerialFlightConfirmation.Takeoff("release"));
            AssertRefused(AerialFlightConfirmation.Takeoff("take off"));
        }

        [Test]
        public void Takeoff_WithConfirmation_EmitsFlightRequest()
        {
            AerialButtonResult result = AerialFlightConfirmation.Takeoff(AerialFlightConfirmation.Phrase);
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual("takeoff", result.Flight.Request);
            Assert.AreEqual(2.5, result.Flight.AltM.Value, 1e-6);
            Assert.AreEqual(AerialFlightConfirmation.Phrase, result.Flight.Confirm);

            AerialTeleopCommand cmd = Map(left: true, flight: result.Flight);
            JObject json = Parse(cmd);
            Assert.AreEqual("takeoff", json["flight"].Value<string>("request"));
            Assert.AreEqual(2.5, json["flight"].Value<double>("alt_m"), 1e-6);
            Assert.AreEqual(AerialFlightConfirmation.Phrase, json["flight"].Value<string>("confirm"));
            Assert.AreEqual(AerialDriveMapping.FrameBodyFrd, json.Value<string>("frame"));
            Assert.IsFalse(json.Value<bool>("estop"));
        }

        [Test]
        public void Takeoff_FoldedPhrase_MatchesSafetyGate()
        {
            string typed = "  " + AerialFlightConfirmation.Phrase.ToUpperInvariant() + ". ";
            AerialButtonResult result = AerialFlightConfirmation.Takeoff(typed, 3.0);
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(AerialFlightConfirmation.Phrase, result.Flight.Confirm);
            Assert.AreEqual(3.0, result.Flight.AltM.Value, 1e-6);
        }

        [Test]
        public void Takeoff_AboveTeleopCeiling_IsRefused()
        {
            AerialButtonResult high = AerialFlightConfirmation.Takeoff(AerialFlightConfirmation.Phrase, 3.1);
            Assert.IsFalse(high.Accepted);
            Assert.AreEqual(AerialFlightConfirmation.ReasonAltitude, high.Reason);
            Assert.IsNull(high.Flight);

            AerialButtonResult bad = AerialFlightConfirmation.Takeoff(AerialFlightConfirmation.Phrase, 0);
            Assert.AreEqual(AerialFlightConfirmation.ReasonBadAltitude, bad.Reason);
        }

        [Test]
        public void Land_WithoutConfirmation_IsRefused()
        {
            AssertRefused(AerialFlightConfirmation.Land(null));
            AssertRefused(AerialFlightConfirmation.Land("land now"));
        }

        [Test]
        public void Land_WithConfirmation_EmitsFlightRequest()
        {
            AerialButtonResult result = AerialFlightConfirmation.Land("  " + AerialFlightConfirmation.Phrase + ".");
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual("land", result.Flight.Request);
            Assert.IsFalse(result.Flight.AltM.HasValue);
            Assert.AreEqual(AerialFlightConfirmation.Phrase, result.Flight.Confirm);

            JObject json = Parse(Map(flight: result.Flight));
            Assert.AreEqual("land", json["flight"].Value<string>("request"));
            Assert.IsNull(json["flight"]["alt_m"]);
        }

        [Test]
        public void QuadrotorCatalogCap_StaysTwelveMetersPerSecond()
        {
            DriveSpec spec = DriveSpecs.Get(DriveKind.Quadrotor);
            Assert.AreEqual(12f, spec.MaxLinVel, 1e-4f);
            Assert.AreEqual(3f, spec.MaxAngVel, 1e-4f);

            AerialTeleopCommand cmd = Map(ry: 1f, left: true, maxLin: spec.MaxLinVel, maxAng: spec.MaxAngVel);
            Assert.AreEqual(spec.MaxLinVel, cmd.Vx, 1e-4f);
        }

        [Test]
        public void FlyingMessage_MatchesSimServeShape()
        {
            AerialTeleopCommand cmd = Map(ry: 1f, left: true, maxLin: 1f, maxAng: 1f);
            JObject json = Parse(cmd);
            Assert.AreEqual(4, json.Count);
            Assert.AreEqual("BODY_FRD", json.Value<string>("frame"));
            Assert.IsTrue(json.Value<bool>("deadman"));
            Assert.IsFalse(json.Value<bool>("estop"));
            Assert.AreEqual(1f, json["velocity"]["linear"].Value<float>("x"), 1e-4f);
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("y"), 1e-4f);
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("z"), 1e-4f);
            Assert.IsNull(json["velocity"]["angular"]["x"]);
            Assert.IsNull(json["velocity"]["angular"]["y"]);
        }

        [Test]
        public void Scheme_DeadmanRelease_SendsZeroVelocity()
        {
            AerialDriveScheme scheme = MakeScheme("px4-x500");
            var sent = new RecordingSender();
            scheme.UseSender(sent);

            Tick(scheme, ry: 1f, left: true);
            Assert.AreEqual(1, sent.Messages.Count);
            Assert.AreEqual(Lin, scheme.LastCommand.Vx, 1e-4f);
            Assert.IsTrue(scheme.LastCommand.Deadman);

            Tick(scheme);
            Assert.AreEqual(2, sent.Messages.Count);
            Assert.IsFalse(scheme.LastCommand.Deadman);
            Assert.AreEqual(0f, scheme.LastCommand.Vx, 1e-4f);
            Assert.AreEqual(0f, scheme.LastCommand.Vz, 1e-4f);
            JObject json = JObject.Parse(sent.Messages[1]);
            Assert.IsFalse(json.Value<bool>("deadman"));
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("x"), 1e-4f);
        }

        [Test]
        public void Scheme_BothGrips_SendsEstop()
        {
            AerialDriveScheme scheme = MakeScheme("px4-x500");
            var sent = new RecordingSender();
            scheme.UseSender(sent);

            Tick(scheme, lx: 1f, ly: 1f, left: true, right: true);
            Assert.AreEqual(1, sent.Messages.Count);
            Assert.IsTrue(scheme.LastCommand.Estop);
            Assert.AreEqual(AerialEstopEffect.Land, scheme.LastCommand.EstopEffect);
            JObject json = JObject.Parse(sent.Messages[0]);
            Assert.IsTrue(json.Value<bool>("estop"));
            Assert.AreEqual(0f, json["velocity"]["linear"].Value<float>("z"), 1e-4f);
            AssertForbidden(json);
        }

        [Test]
        public void Scheme_TakeoffRequiresConfirmation()
        {
            AerialDriveScheme scheme = MakeScheme("px4-x500");
            var sent = new RecordingSender();
            scheme.UseSender(sent);

            Assert.IsFalse(scheme.TryTakeoff("nope"));
            Assert.AreEqual(AerialFlightConfirmation.ReasonNotConfirmed, scheme.LastRefusal);
            Tick(scheme, ly: 1f, left: true);
            Assert.IsNull(scheme.LastCommand.Flight);
            Assert.IsNull(JObject.Parse(sent.Messages[0])["flight"]);

            Assert.IsTrue(scheme.TryTakeoff(AerialFlightConfirmation.Phrase));
            Tick(scheme, left: true);
            Assert.AreEqual("takeoff", scheme.LastCommand.Flight.Request);

            JObject json = JObject.Parse(sent.Messages[sent.Messages.Count - 1]);
            Assert.AreEqual("takeoff", json["flight"].Value<string>("request"));
            Assert.AreEqual(AerialFlightConfirmation.Phrase, json["flight"].Value<string>("confirm"));

            int count = sent.Messages.Count;
            Tick(scheme, left: true, dt: 0.1f);
            Assert.AreEqual(count + 1, sent.Messages.Count);
            JObject again = JObject.Parse(sent.Messages[sent.Messages.Count - 1]);
            Assert.IsNull(again["flight"]);
        }

        [Test]
        public void Scheme_LandRequiresConfirmation_AndEstopCancelsTakeoff()
        {
            AerialDriveScheme scheme = MakeScheme("crazyflie-cf21");
            var sent = new RecordingSender();
            scheme.UseSender(sent);

            Assert.IsFalse(scheme.TryLand(""));
            Assert.IsTrue(scheme.TryLand(AerialFlightConfirmation.Phrase));
            Tick(scheme, dt: 0f);
            Assert.AreEqual("land", JObject.Parse(sent.Messages[0])["flight"].Value<string>("request"));

            Assert.IsTrue(scheme.TryTakeoff(AerialFlightConfirmation.Phrase));
            Tick(scheme, left: true, right: true, dt: 0f);
            JObject estop = JObject.Parse(sent.Messages[sent.Messages.Count - 1]);
            Assert.IsTrue(estop.Value<bool>("estop"));
            Assert.IsNull(estop["flight"]);
            Assert.AreEqual(AerialEstopEffect.MotorCut, scheme.LastCommand.EstopEffect);
            Assert.IsFalse(scheme.TryTakeoff(AerialFlightConfirmation.Phrase));
            Assert.AreEqual(AerialFlightConfirmation.ReasonEstop, scheme.LastRefusal);
        }

        [Test]
        public void Scheme_RightGrip_IsDeadmanNotTurbo()
        {
            AerialDriveScheme scheme = MakeScheme("px4-x500");
            Tick(scheme, ry: 1f, right: true);
            Assert.AreEqual(Lin, scheme.LastCommand.Vx, 1e-4f);
            Assert.IsTrue(scheme.LastCommand.Deadman);
            Assert.IsFalse(scheme.LastCommand.Estop);
        }

        [Test]
        public void LoopbackUrl_AllowsSimServeOnly()
        {
            Assert.IsTrue(SimServeAerialClient.IsLoopbackUrl("ws://127.0.0.1:8765"));
            Assert.IsTrue(SimServeAerialClient.IsLoopbackUrl("ws://localhost:8765"));
            Assert.IsTrue(SimServeAerialClient.IsLoopbackUrl("ws://[::1]:8765"));
            Assert.IsFalse(SimServeAerialClient.IsLoopbackUrl("ws://192.168.1.101:8765"));
            Assert.IsFalse(SimServeAerialClient.IsLoopbackUrl("ws://10.0.0.8:8765"));
            Assert.IsFalse(SimServeAerialClient.IsLoopbackUrl("wss://drone.local:8765"));
            Assert.IsFalse(SimServeAerialClient.IsLoopbackUrl("http://127.0.0.1:8765"));
            Assert.IsFalse(SimServeAerialClient.IsLoopbackUrl(""));
        }

        static void AssertRefused(AerialButtonResult result)
        {
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(AerialFlightConfirmation.ReasonNotConfirmed, result.Reason);
            Assert.IsNull(result.Flight);
        }

        static void AssertForbidden(JObject json)
        {
            string text = json.ToString().ToLowerInvariant();
            Assert.IsFalse(text.Contains("motor_kill"));
            Assert.IsFalse(text.Contains("force_disarm"));
            Assert.IsFalse(text.Contains("flight_termination"));
            Assert.IsFalse(text.Contains("21196"));
        }

        static JObject Parse(AerialTeleopCommand cmd)
        {
            return JObject.Parse(SimServeAerialJson.Serialize(cmd));
        }

        static AerialTeleopCommand Map(
            float lx = 0f, float ly = 0f, float rx = 0f, float ry = 0f,
            bool left = false, bool right = false,
            string robotId = "px4-x500", string robotName = null,
            float maxLin = Lin, float maxAng = Ang,
            AerialFlightRequest flight = null)
        {
            return AerialDriveMapping.Map(new AerialMappingInput
            {
                RobotId = robotId,
                RobotName = robotName,
                MaxLinVel = maxLin,
                MaxAngVel = maxAng,
                LeftX = lx,
                LeftY = ly,
                RightX = rx,
                RightY = ry,
                LeftGrip = left,
                RightGrip = right,
                PendingFlight = flight,
            });
        }

        static AerialDriveScheme MakeScheme(string robotId)
        {
            var scheme = new AerialDriveScheme();
            scheme.Configure(new RobotProfile
            {
                RobotId = robotId,
                Name = robotId,
                Drive = DriveKind.Quadrotor,
                MaxLinVel = Lin,
                MaxAngVel = Ang,
                Topics = new RobotTopics { CmdVel = "/cmd_vel/teleop" },
            });
            return scheme;
        }

        static void Tick(
            AerialDriveScheme scheme,
            float lx = 0f, float ly = 0f, float rx = 0f, float ry = 0f,
            bool left = false, bool right = false, float dt = 0f)
        {
            var input = new TeleopInput
            {
                LeftStick = new Vector2(lx, ly),
                RightStick = new Vector2(rx, ry),
                LeftGrip = left,
                RightGrip = right,
                BothGrips = left && right,
                DeltaTime = dt,
            };
            scheme.Tick(ref input, null);
        }

        sealed class RecordingSender : ISimServeSender
        {
            public bool IsConnected => true;
            public readonly List<string> Messages = new List<string>();
            public void Send(string json) => Messages.Add(json);
        }
    }
}
