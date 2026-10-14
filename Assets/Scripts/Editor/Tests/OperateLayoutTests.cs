using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Core;
using OmniBot.VR.MR;
using OmniBot.VR.Recording;
using OmniBot.VR.UI;
using OmniBot.VR.UI.Operate;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Operate dashboard wiring: region placement, comfort, billboard lock,
    /// and the mode bar's confirm / e-stop rules.
    /// </summary>
    public class OperateLayoutTests
    {
        private readonly List<Object> _owned = new List<Object>();

        [SetUp]
        public void SetUp() => OperateSafetyLatch.Clear();

        [TearDown]
        public void TearDown()
        {
            OperateSafetyLatch.Clear();
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            }
            _owned.Clear();
        }

        [Test]
        public void Regions_DoNotOverlap_AndReadInOneGlance()
        {
            var regions = OperateLayoutSpec.Regions;
            Assert.AreEqual(5, regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                for (int j = i + 1; j < regions.Count; j++)
                    Assert.IsFalse(regions[i].Overlaps(regions[j]));
            }

            Assert.Less(OperateLayoutSpec.Telemetry.CenterX, OperateLayoutSpec.SimScreen.CenterX);
            Assert.Less(OperateLayoutSpec.SimScreen.CenterX, OperateLayoutSpec.Arm.CenterX);
            Assert.Greater(OperateLayoutSpec.ModeBar.YMin, OperateLayoutSpec.SimScreen.YMax);
            Assert.Less(OperateLayoutSpec.Legend.YMax, OperateLayoutSpec.SimScreen.YMin);
        }

        [Test]
        public void Comfort_BodyTextClearsVisualAngle_AndPassthroughContrast()
        {
            float distance = OperateComfort.OperateDistanceMetres;
            float scale = OperateComfort.CanvasMetresPerUnit;
            Assert.IsTrue(OperateComfort.MeetsVisualAngle(OperateComfort.BodyFontSize, distance, scale));
            Assert.GreaterOrEqual(
                OperateComfort.VisualAngleDegrees(OperateComfort.BodyFontSize, distance, scale),
                OperateComfort.MinVisualAngleDegrees);
            Assert.IsFalse(OperateComfort.MeetsVisualAngle(20f, distance, scale));

            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Text, OperateComfort.Panel));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Muted, OperateComfort.Panel));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Text, OperateComfort.Chip));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Danger, OperateComfort.Chip));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Safe, OperateComfort.Chip));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.Amber, OperateComfort.Chip));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.AccentText, OperateComfort.Accent));
            Assert.IsTrue(OperateComfort.PassthroughTextIsReadable(OperateComfort.DangerText, OperateComfort.DangerButton));
        }

        [Test]
        public void Realm_UnresolvedHostIsReal_ExplicitSimWins()
        {
            Assert.AreEqual(OperateRealm.Sim, OperateRealmRules.Resolve("sim", "10.0.0.5"));
            Assert.AreEqual(OperateRealm.Real, OperateRealmRules.Resolve("real", "127.0.0.1"));
            Assert.AreEqual(OperateRealm.Sim, OperateRealmRules.Resolve(null, "localhost"));
            Assert.AreEqual(OperateRealm.Real, OperateRealmRules.Resolve(null, "192.168.1.101"));
            Assert.AreEqual(OperateRealm.Real, OperateRealmRules.Resolve("", ""));
        }

        [Test]
        public void Deadman_BothGripsIsStop_OneGripIsHeld()
        {
            Assert.AreEqual(OperateDeadman.Open, OperateReadout.Deadman(false, false));
            Assert.AreEqual(OperateDeadman.Held, OperateReadout.Deadman(true, false));
            Assert.AreEqual(OperateDeadman.Stop, OperateReadout.Deadman(true, true));
            Assert.AreEqual("DEADMAN STOP", OperateReadout.DeadmanLabel(OperateDeadman.Stop));
        }

        [Test]
        public void Policy_StaysPendingUntilConfirmed()
        {
            var commands = new OperateModeCommands();
            Assert.IsTrue(commands.Request(OperateDriveMode.Nav2));
            Assert.IsFalse(commands.Request(OperateDriveMode.Policy));
            Assert.IsTrue(commands.PolicyConfirmationPending);
            Assert.AreEqual(OperateDriveMode.Nav2, commands.Mode);

            commands.CancelPolicy();
            Assert.IsFalse(commands.PolicyConfirmationPending);
            Assert.AreEqual(OperateDriveMode.Nav2, commands.Mode);

            Assert.IsFalse(commands.Request(OperateDriveMode.Policy));
            Assert.IsTrue(commands.ConfirmPolicy());
            Assert.AreEqual(OperateDriveMode.Policy, commands.Mode);
            Assert.AreEqual("policy", commands.ModeWireValue);
        }

        [Test]
        public void Reset_RefusesWhileGripIsHeld()
        {
            var commands = new OperateModeCommands();
            commands.LatchEStop();
            Assert.IsFalse(OperateSafetyRules.ResetAllowed(true));
            Assert.IsFalse(commands.TryReset(true));
            Assert.IsTrue(commands.EStopLatched);
            Assert.IsTrue(commands.TryReset(false));
            Assert.IsFalse(commands.EStopLatched);
        }

        [Test]
        public void Dashboard_HasEveryPanel_AndComfortableText()
        {
            var dash = BuildDashboard();
            Assert.IsNotNull(Find(dash.transform, OperateLayoutSpec.ModeBarName));
            Assert.IsNotNull(Find(dash.transform, OperateLayoutSpec.TelemetryName));
            Assert.IsNotNull(Find(dash.transform, OperateLayoutSpec.SimScreenName));
            Assert.IsNotNull(Find(dash.transform, OperateLayoutSpec.ArmName));
            Assert.IsNotNull(Find(dash.transform, OperateLayoutSpec.LegendName));
            Assert.IsNotNull(dash.SimFeedImage);
            Assert.IsNotNull(dash.SimFeedController);
            Assert.IsNotNull(dash.RecentreButton);
            Assert.IsFalse(dash.PolicyConfirmRoot.activeSelf);

            var sim = Find(dash.transform, OperateLayoutSpec.SimScreenName).GetComponent<RectTransform>();
            var telemetry = Find(dash.transform, OperateLayoutSpec.TelemetryName).GetComponent<RectTransform>();
            var arm = Find(dash.transform, OperateLayoutSpec.ArmName).GetComponent<RectTransform>();
            var bar = Find(dash.transform, OperateLayoutSpec.ModeBarName).GetComponent<RectTransform>();
            var legend = Find(dash.transform, OperateLayoutSpec.LegendName).GetComponent<RectTransform>();
            Assert.Less(telemetry.anchorMax.x, sim.anchorMin.x);
            Assert.Less(sim.anchorMax.x, arm.anchorMin.x);
            Assert.Greater(bar.anchorMin.y, sim.anchorMax.y);
            Assert.Less(legend.anchorMax.y, sim.anchorMin.y);

            var texts = dash.GetComponentsInChildren<TMP_Text>(true);
            Assert.Greater(texts.Length, 8);
            for (int i = 0; i < texts.Length; i++)
                AssertReadable(texts[i]);
        }

        [Test]
        public void Dashboard_PolicyConfirm_DoesNotPublishUntilYes()
        {
            var dash = BuildDashboard();
            dash.PolicyButton.onClick.Invoke();
            Assert.AreEqual(OperateDriveMode.Teleop, dash.Commands.Mode);
            Assert.IsTrue(dash.PolicyConfirmRoot.activeSelf);
            Assert.AreEqual(0, dash.ModePublishAttempts);

            dash.PolicyConfirmNoButton.onClick.Invoke();
            Assert.AreEqual(OperateDriveMode.Teleop, dash.Commands.Mode);
            Assert.IsFalse(dash.PolicyConfirmRoot.activeSelf);

            dash.PolicyButton.onClick.Invoke();
            dash.PolicyConfirmYesButton.onClick.Invoke();
            Assert.AreEqual(OperateDriveMode.Policy, dash.Commands.Mode);
            Assert.AreEqual(1, dash.ModePublishAttempts);
            Assert.AreEqual("POLICY", Chip(dash, "Operate Mode Text"));
        }

        [Test]
        public void Dashboard_EStopLatches_AndBlocksModePublishUntilReset()
        {
            var dash = BuildDashboard();
            dash.EStopButton.onClick.Invoke();
            dash.EStopButton.onClick.Invoke();
            Assert.IsTrue(dash.Commands.EStopLatched);
            Assert.IsTrue(OperateSafetyLatch.UiEStopLatched);
            Assert.AreEqual("E-STOP ON", Chip(dash, "Operate E-Stop Text"));

            int attempts = dash.ModePublishAttempts;
            dash.Nav2Button.onClick.Invoke();
            Assert.AreEqual(OperateDriveMode.Nav2, dash.Commands.Mode);
            Assert.AreEqual(attempts, dash.ModePublishAttempts);
            Assert.IsTrue(OperateSafetyLatch.UiEStopLatched);

            dash.ResetButton.onClick.Invoke();
            Assert.IsFalse(OperateSafetyLatch.UiEStopLatched);
            Assert.IsFalse(dash.Commands.EStopLatched);
            Assert.AreEqual(attempts, dash.ModePublishAttempts);

            dash.Nav2Button.onClick.Invoke();
            Assert.AreEqual(attempts + 1, dash.ModePublishAttempts);
        }

        [Test]
        public void Dashboard_RecordDoesNotSpawnEpisodeManagerInEditMode()
        {
            var dash = BuildDashboard();
            int before = Object.FindObjectsOfType<EpisodeManager>().Length;
            dash.RecordButton.onClick.Invoke();
            Assert.IsTrue(dash.Commands.Recording);
            Assert.AreEqual(before, Object.FindObjectsOfType<EpisodeManager>().Length);
            Assert.AreEqual("Recording", dash.RecordButton.GetComponentInChildren<TMP_Text>().text);
        }

        [Test]
        public void Dashboard_RealmChip_FollowsLabel()
        {
            var dash = BuildDashboard();
            dash.SetRealmLabel("sim");
            Assert.AreEqual("SIM", Chip(dash, "Operate Realm Text"));
            dash.SetRealmLabel("hardware");
            Assert.AreEqual("REAL", Chip(dash, "Operate Realm Text"));
        }

        [Test]
        public void BillboardLock_StopsFaceFollow_UntilUnlocked()
        {
            var cam = CameraGo(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            var panel = Track(new GameObject("panel"));
            var placer = panel.AddComponent<WorldSpaceUiPlacer>();
            placer.SetViewCamera(cam);
            panel.transform.position = new Vector3(0f, 1.4f, 1.6f);
            panel.transform.rotation = Quaternion.Euler(0f, 80f, 0f);
            placer.SetBillboardLocked(true);
            Quaternion frozen = panel.transform.rotation;
            placer.TickBillboard(1f);
            Assert.Less(Quaternion.Angle(frozen, panel.transform.rotation), 0.01f);

            placer.SetBillboardLocked(false);
            placer.TickBillboard(1f);
            Assert.Greater(Quaternion.Angle(frozen, panel.transform.rotation), 1f);
        }

        [Test]
        public void BillboardLock_RecentreDoesNotUnlock()
        {
            var cam = CameraGo(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            var panel = Track(new GameObject("panel"));
            var placer = panel.AddComponent<WorldSpaceUiPlacer>();
            placer.SetViewCamera(cam);
            placer.SetDistance(1.6f);
            panel.transform.position = new Vector3(3f, 0f, 0f);
            placer.SetBillboardLocked(true);
            placer.Recentre();
            Assert.IsTrue(placer.IsBillboardLocked);
            Assert.AreEqual(1.6f, panel.transform.position.z, 0.05f);
            Quaternion frozen = panel.transform.rotation;
            cam.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            placer.TickBillboard(1f);
            Assert.Less(Quaternion.Angle(frozen, panel.transform.rotation), 0.01f);
        }

        [Test]
        public void SmoothHud_LockStopsFollow()
        {
            var cam = CameraGo(Vector3.zero, Quaternion.identity);
            var rig = Track(new GameObject("rig"));
            rig.transform.position = new Vector3(3f, 0f, 0f);
            var hud = rig.AddComponent<SmoothHUD>();
            hud.SetViewCamera(cam.transform);
            hud.SetPlacementLocked(true);
            hud.TickFollow(1f);
            Assert.AreEqual(3f, rig.transform.position.x, 0.001f);

            hud.SetPlacementLocked(false);
            hud.TickFollow(1f);
            Assert.Less(rig.transform.position.x, 2.9f);
        }

        [Test]
        public void EnterOperation_LocksPlacement_AndRecentreFacesForward()
        {
            var cam = CameraGo(new Vector3(0f, 1.7f, 0f), Quaternion.identity);
            var rig = Track(new GameObject("OhhO Screen Root"));
            rig.transform.position = new Vector3(4f, 0f, 4f);
            rig.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var hud = rig.AddComponent<SmoothHUD>();
            hud.SetViewCamera(cam.transform);

            var canvasGo = new GameObject("OhhO Teleop Screen", typeof(RectTransform));
            canvasGo.transform.SetParent(rig.transform, false);
            canvasGo.transform.localPosition = new Vector3(1f, 0f, 0.2f);
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            canvasGo.AddComponent<Canvas>();
            var placer = canvasGo.AddComponent<WorldSpaceUiPlacer>();
            placer.SetViewCamera(cam);

            var page = new GameObject("Teleop Page", typeof(RectTransform));
            page.transform.SetParent(canvasGo.transform, false);
            var dash = OperateDashboard.Build(page.GetComponent<RectTransform>());
            dash.EnterOperation();

            Assert.IsTrue(placer.IsBillboardLocked);
            Assert.IsTrue(hud.IsPlacementLocked);
            Assert.AreEqual(0f, rig.transform.position.x, 0.02f);
            Assert.AreEqual(1.6f, rig.transform.position.y, 0.02f);
            Assert.AreEqual(0f, rig.transform.position.z, 0.02f);
            Assert.AreEqual(OperateComfort.OperateDistanceMetres, canvasGo.transform.localPosition.z, 0.02f);
            Assert.Less(Quaternion.Angle(canvasGo.transform.localRotation, Quaternion.identity), 0.1f);

            cam.transform.position = new Vector3(2f, 1.7f, -3f);
            cam.transform.rotation = Quaternion.Euler(0f, 50f, 0f);
            hud.TickFollow(1f);
            placer.TickBillboard(1f);
            Assert.AreEqual(0f, rig.transform.position.x, 0.02f);
            Assert.AreEqual(OperateComfort.OperateDistanceMetres, canvasGo.transform.localPosition.z, 0.02f);

            dash.ExitOperation();
            Assert.IsFalse(placer.IsBillboardLocked);
            Assert.IsFalse(hud.IsPlacementLocked);
        }

        [Test]
        public void UiEStop_BlocksMotion_AndGripRelease()
        {
            var link = new RecordingLink();
            var scheme = new DifferentialDriveScheme();
            scheme.Configure(DriveProfile());

            var drive = StickInput();
            scheme.Tick(ref drive, link);
            Assert.Greater(NonZeroTwists(link), 0);
            Assert.IsFalse(scheme.IsEmergencyStopActive);

            link.Published.Clear();
            OperateSafetyLatch.Latch();
            var again = StickInput();
            scheme.Tick(ref again, link);
            Assert.AreEqual(0, NonZeroTwists(link));
            Assert.IsTrue(scheme.IsEmergencyStopActive);
            Assert.IsTrue(SawBool(link, true));

            link.Published.Clear();
            var release = new TeleopInput
            {
                BothGrips = false,
                BothGripsReleased = true,
                LeftStick = new Vector2(0f, 1f),
                DeltaTime = 0.1f,
            };
            scheme.Tick(ref release, link);
            Assert.IsTrue(scheme.IsEmergencyStopActive);
            Assert.IsFalse(SawBool(link, false));
            Assert.AreEqual(0, NonZeroTwists(link));
        }

        [Test]
        public void GripEStop_StillClearsOnRelease_WhenUiLatchIsOff()
        {
            var link = new RecordingLink();
            var scheme = new DifferentialDriveScheme();
            scheme.Configure(DriveProfile());
            var press = new TeleopInput { BothGrips = true, BothGripsPressed = true, DeltaTime = 0.1f };
            scheme.Tick(ref press, link);
            Assert.IsTrue(scheme.IsEmergencyStopActive);

            var release = new TeleopInput { BothGrips = false, BothGripsReleased = true, DeltaTime = 0.1f };
            scheme.Tick(ref release, link);
            Assert.IsFalse(scheme.IsEmergencyStopActive);
            Assert.IsTrue(SawBool(link, false));
        }

        [Test]
        public void ReleaseUiStop_WaitsForLatchClear_AndRefusesHeldGrips()
        {
            var link = new RecordingLink();
            var scheme = new DifferentialDriveScheme();
            scheme.Configure(DriveProfile());
            var press = new TeleopInput { BothGrips = true, BothGripsPressed = true, DeltaTime = 0.1f };
            scheme.Tick(ref press, link);

            scheme.ReleaseUiStop(link, gripHeld: true);
            Assert.IsTrue(scheme.IsEmergencyStopActive);

            OperateSafetyLatch.Latch();
            scheme.ReleaseUiStop(link, gripHeld: false);
            Assert.IsTrue(scheme.IsEmergencyStopActive);

            OperateSafetyLatch.Clear();
            link.Published.Clear();
            scheme.ReleaseUiStop(link, gripHeld: false);
            Assert.IsFalse(scheme.IsEmergencyStopActive);
            Assert.IsTrue(SawBool(link, false));
        }

        private OperateDashboard BuildDashboard()
        {
            var page = Track(new GameObject("Teleop Page", typeof(RectTransform)));
            return OperateDashboard.Build(page.GetComponent<RectTransform>());
        }

        private Camera CameraGo(Vector3 position, Quaternion rotation)
        {
            var go = Track(new GameObject("view-camera"));
            go.transform.position = position;
            go.transform.rotation = rotation;
            return go.AddComponent<Camera>();
        }

        private GameObject Track(GameObject go)
        {
            _owned.Add(go);
            return go;
        }

        private static void AssertReadable(TMP_Text tmp)
        {
            Assert.IsTrue(
                OperateComfort.MeetsVisualAngle(
                    tmp.fontSize,
                    OperateComfort.OperateDistanceMetres,
                    OperateComfort.CanvasMetresPerUnit),
                tmp.name + " font " + tmp.fontSize);

            var image = tmp.GetComponentInParent<Image>(true);
            Color background = image != null ? image.color : OperateComfort.Panel;
            float ratio = OperateComfort.ContrastRatio(tmp.color, OperateComfort.ReadableBackground(background));
            var path = tmp.name;
            var walk = tmp.transform.parent;
            while (walk != null)
            {
                path = walk.name + "/" + path;
                walk = walk.parent;
            }
            Assert.GreaterOrEqual(
                ratio,
                OperateComfort.MinContrastRatio,
                path + " fg=" + tmp.color + " bg=" + background
                    + " img=" + (image != null ? image.gameObject.name : "panel-fallback"));
        }

        private static string Chip(OperateDashboard dash, string name)
        {
            var found = Find(dash.transform, name);
            Assert.IsNotNull(found, name);
            return found.GetComponent<TMP_Text>().text;
        }

        private static Transform Find(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            return null;
        }

        private static RobotProfile DriveProfile()
        {
            return new RobotProfile
            {
                RobotId = "operate-test",
                Name = "Operate Test",
                Drive = DriveKind.Differential,
                MaxLinVel = 0.5f,
                MaxAngVel = 1f,
                Topics = new RobotTopics
                {
                    CmdVel = "/cmd_vel/teleop",
                    ControlMode = "/control_mode",
                    EmergencyStop = "/emergency_stop",
                },
            };
        }

        private static TeleopInput StickInput()
        {
            return new TeleopInput
            {
                LeftStick = new Vector2(0f, 1f),
                DeltaTime = 0.1f,
            };
        }

        private static int NonZeroTwists(RecordingLink link)
        {
            int count = 0;
            for (int i = 0; i < link.Published.Count; i++)
            {
                var twist = link.Published[i].msg as TwistMsg;
                if (twist != null && Mathf.Abs(twist.linear.x) > 0.01f) count++;
            }
            return count;
        }

        private static bool SawBool(RecordingLink link, bool value)
        {
            for (int i = 0; i < link.Published.Count; i++)
            {
                var msg = link.Published[i].msg as BoolMsg;
                if (msg != null && msg.data == value) return true;
            }
            return false;
        }

        private sealed class RecordingLink : IRobotLink
        {
            public readonly List<(string topic, object msg)> Published = new List<(string, object)>();
            public bool IsConnected => true;
            public void Advertise(string topic, string type) { }
            public void Publish<T>(string topic, T msg) => Published.Add((topic, msg));
            public void Subscribe(string topic, string type, System.Action<string> callback) { }
            public void Unsubscribe(string topic) { }
        }
    }
}
