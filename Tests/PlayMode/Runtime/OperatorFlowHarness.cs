#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OmniBot.VR.App;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Drive;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.UI.Console;
using OmniBot.VR.UI.Garage;
using OmniBot.VR.UI.Selection;
using UnityEngine;

namespace OmniBot.VR.PlayMode
{
    /// <summary>
    /// Drives the real headset flow against <see cref="FakeRobotLink"/>:
    /// login stub, console, garage, add-robot picker, teleop, both-grips e-stop.
    /// Supabase, rosbridge, and the Quest runtime are not contacted.
    /// </summary>
    public static class OperatorFlowHarness
    {
        const string CmdVel = "/cmd_vel/teleop";
        const string ControlMode = "/control_mode";
        const string EmergencyStop = "/emergency_stop";
        const float Step = 0.1f;

        public static OperatorFlowResult RunLoginGaragePickerTeleopEStop()
        {
            var result = new OperatorFlowResult();
            var log = new StringBuilder();
            var prefs = PrefSnapshot.Capture();
            GameObject root = null;
            try
            {
                PlayerPrefs.SetInt("auto_connect", 0);
                CalibrationManager.Reset("omnibot-pro");
                PlayerPrefs.Save();

                IsolateScene();
                root = BuildApp(out var app, out var console, out var garage, out var picker,
                    out var loginPanel, out var consolePanel, out var garagePanel, out var teleopPanel,
                    out var teleop, out var link, out var pickerPanels);

                app.ShowLogin();
                result.LoginPanelShown = loginPanel.activeInHierarchy && !consolePanel.activeInHierarchy &&
                                          !garagePanel.activeInHierarchy && !teleopPanel.activeInHierarchy;
                log.Append("login=").Append(result.LoginPanelShown).Append("; ");

                Invoke(app, "OnAuthChanged", false);
                result.SignedOutShowsLogin = loginPanel.activeInHierarchy && !consolePanel.activeInHierarchy;
                log.Append("signedOut=").Append(result.SignedOutShowsLogin).Append("; ");

                Invoke(app, "OnAuthChanged", true);
                result.SignedInShowsConsole = consolePanel.activeInHierarchy && !loginPanel.activeInHierarchy;
                log.Append("console=").Append(result.SignedInShowsConsole).Append("; ");

                Invoke(console, "OnProductSelected", new VrProduct { Slug = "pilot", Name = "OhhO Pilot" });
                result.PilotOpensGarage = garagePanel.activeInHierarchy && !consolePanel.activeInHierarchy;
                log.Append("garage=").Append(result.PilotOpensGarage).Append("; ");

                bool cancelled = false;
                picker.OnCancelled += () => cancelled = true;
                picker.Open();
                result.PickerCategoryShown = pickerPanels.Category.activeInHierarchy &&
                                              !pickerPanels.Type.activeInHierarchy;
                Invoke(picker, "OnCategoryPicked", new VrCategory { Id = "mobile-manipulator", Label = "Mobile manipulator" });
                result.PickerTypeShown = pickerPanels.Type.activeInHierarchy && !pickerPanels.Category.activeInHierarchy;
                Invoke(picker, "OnTypePicked", new VrRobotType { Id = "omnibot-pro", Name = "OmniBot Pro", Category = "mobile-manipulator" });
                result.PickerModelShown = pickerPanels.Model.activeInHierarchy && !pickerPanels.Type.activeInHierarchy;
                Invoke(picker, "OnModelPicked", new VrHardwareModel { Id = "omnibot-pro", Name = "OmniBot Pro", Locomotion = "mecanum", HasArm = true });
                result.PickerNameShown = pickerPanels.Name.activeInHierarchy && !pickerPanels.Model.activeInHierarchy;
                Invoke(picker, "Cancel");
                result.PickerCancelled = cancelled;
                result.PickerHiddenAfterCancel = !pickerPanels.Category.activeSelf && !pickerPanels.Type.activeSelf &&
                                                  !pickerPanels.Model.activeSelf && !pickerPanels.Name.activeSelf &&
                                                  !pickerPanels.Confirm.activeSelf;
                log.Append("picker=").Append(result.PickerNameShown).Append("; ");

                Invoke(garage, "OnRobotSelected", FixtureOmniBot());
                result.TeleopActive = teleop.IsTeleopActive;
                result.TeleopPanelShown = teleopPanel.activeInHierarchy && !garagePanel.activeInHierarchy;
                result.ProfileRobotId = teleop.Profile != null ? teleop.Profile.RobotId : "";
                result.MaxLinVel = teleop.Profile != null ? teleop.Profile.MaxLinVel : float.NaN;
                result.MaxAngVel = teleop.Profile != null ? teleop.Profile.MaxAngVel : float.NaN;
                var drive = GetField(teleop, "_drive") as IDriveScheme;
                result.DriveIsMecanum = drive is MecanumDriveScheme;
                result.AdvertisedCmdVel = link.HasAdvertised(CmdVel, "geometry_msgs/Twist");
                result.AdvertisedEmergencyStop = link.HasAdvertised(EmergencyStop, "std_msgs/Bool");
                log.Append("teleop=").Append(result.ProfileRobotId).Append("; ");

                if (drive == null) throw new System.InvalidOperationException("TeleopController did not install a drive scheme.");

                Tick(drive, link, Stick(0.05f, 0f), grips: false, press: false, release: false);
                result.DeadZoneAddedNonZero = link.CountNonZeroTwist(CmdVel);

                Tick(drive, link, Stick(1f, 1f, 1f), grips: true, press: true, release: false);
                Tick(drive, link, Stick(1f, 1f, 1f), grips: true, press: false, release: false);
                result.EStopTrueCount = link.CountBool(EmergencyStop, true);
                result.NonZeroWhileHeld = link.CountNonZeroTwist(CmdVel);

                Tick(drive, link, Stick(0f, 0f), grips: false, press: false, release: true);
                result.EStopFalseCount = link.CountBool(EmergencyStop, false);

                Tick(drive, link, Stick(1f, 1f, 1f), grips: false, press: false, release: false);
                var resume = link.LastTwist(CmdVel);
                if (resume != null)
                {
                    result.ResumeLinearX = resume.linear.x;
                    result.ResumeLinearY = resume.linear.y;
                    result.ResumeAngularZ = resume.angular.z;
                }
                result.TeleopModePublished = link.HasString(ControlMode, "teleop");

                Tick(drive, link, Stick(0f, 1f, 0f), leftGrip: false, rightGrip: true, press: false, release: false);
                var turbo = link.LastTwist(CmdVel);
                if (turbo != null) result.TurboLinearX = turbo.linear.x;

                teleop.StopTeleop();
                var stopped = link.LastTwist(CmdVel);
                if (stopped != null)
                {
                    result.StopLinearX = stopped.linear.x;
                    result.StopLinearY = stopped.linear.y;
                    result.StopAngularZ = stopped.angular.z;
                }
                result.StopPublishedNav2 = link.HasString(ControlMode, "nav2");
                result.TeleopInactiveAfterStop = !teleop.IsTeleopActive;
                log.Append("estopTrue=").Append(result.EStopTrueCount)
                    .Append(" nonZeroWhileHeld=").Append(result.NonZeroWhileHeld)
                    .Append(" resumeX=").Append(result.ResumeLinearX);
            }
            catch (System.Exception ex)
            {
                log.Append(" exception=").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                result.Detail = log.ToString();
                throw new System.InvalidOperationException(result.Detail, ex);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                DestroyNamed("HandWorkspaceOrigin");
                DestroyComponents<ConnectionManager>();
                DestroyComponents<ROSBridgeClient>();
                prefs.Restore();
            }

            result.Detail = log.ToString();
            return result;
        }

        public static DisconnectedLinkResult RunDisconnected()
        {
            var result = new DisconnectedLinkResult();
            var prefs = PrefSnapshot.Capture();
            GameObject root = null;
            try
            {
                PlayerPrefs.SetInt("auto_connect", 0);
                CalibrationManager.Reset("omnibot-pro");
                PlayerPrefs.Save();
                IsolateScene();

                root = new GameObject("ohho-disconnected");
                var link = root.AddComponent<FakeRobotLink>();
                link.Connected = false;
                var teleop = root.AddComponent<TeleopController>();
                SetField(teleop, "linkProvider", link);

                var profile = RobotProfileFactory.FromGarageRobot(FixtureOmniBot());
                if (profile == null) throw new System.InvalidOperationException("Fixture profile was null.");
                teleop.StartTeleop(profile);
                result.TeleopMarkedActive = teleop.IsTeleopActive;

                var drive = GetField(teleop, "_drive") as IDriveScheme;
                if (drive != null)
                {
                    Tick(drive, link, Stick(1f, 1f, 1f), grips: true, press: true, release: false);
                    Tick(drive, link, Stick(1f, 1f, 1f), grips: false, press: false, release: false);
                }

                result.PublicationCount = link.Publications.Count;
                result.AdvertisementCount = link.Advertisements.Count;
                result.Detail = "active=" + result.TeleopMarkedActive +
                                " pubs=" + result.PublicationCount +
                                " ads=" + result.AdvertisementCount;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                DestroyNamed("HandWorkspaceOrigin");
                DestroyComponents<ConnectionManager>();
                DestroyComponents<ROSBridgeClient>();
                prefs.Restore();
            }
            return result;
        }

        public static GarageRobot FixtureOmniBot()
        {
            return new GarageRobot
            {
                UserRobot = new UserRobot
                {
                    Id = "user-robot-ci",
                    Name = "Desk OmniBot",
                    HardwareModelId = "omnibot-pro",
                    RobotTypeId = "omnibot-pro",
                    Status = "active",
                },
                RobotType = new VrRobotType
                {
                    Id = "omnibot-pro",
                    Name = "OmniBot Pro",
                    Category = "mobile-manipulator",
                },
                HardwareModel = new VrHardwareModel
                {
                    Id = "omnibot-pro",
                    Name = "OmniBot Pro",
                    Manufacturer = "OhhO",
                    Locomotion = "mecanum",
                    HasArm = true,
                    Specs = new Dictionary<string, string> { { "dof", "6" } },
                },
                Category = new VrCategory { Id = "mobile-manipulator", Label = "Mobile manipulator" },
            };
        }

        static GameObject BuildApp(
            out OhhoVrApp app,
            out ConsolePanelController console,
            out GaragePanelController garage,
            out RobotSelectionController picker,
            out GameObject loginPanel,
            out GameObject consolePanel,
            out GameObject garagePanel,
            out GameObject teleopPanel,
            out TeleopController teleop,
            out FakeRobotLink link,
            out PickerPanels pickerPanels)
        {
            var root = new GameObject("ohho-flow");
            root.SetActive(false);

            loginPanel = Child(root, "login", true);
            consolePanel = Child(root, "console", true);
            garagePanel = Child(root, "garage", true);
            teleopPanel = Child(root, "teleop", true);
            var cameraPanel = Child(root, "camera", true);
            var controlsPanel = Child(root, "controls", true);

            pickerPanels = new PickerPanels
            {
                Category = Child(root, "picker-category", false),
                Type = Child(root, "picker-type", false),
                Model = Child(root, "picker-model", false),
                Name = Child(root, "picker-name", false),
                Confirm = Child(root, "picker-confirm", false),
            };

            app = root.AddComponent<OhhoVrApp>();
            console = root.AddComponent<ConsolePanelController>();
            garage = root.AddComponent<GaragePanelController>();
            picker = root.AddComponent<RobotSelectionController>();
            teleop = root.AddComponent<TeleopController>();
            link = root.AddComponent<FakeRobotLink>();
            link.Connected = true;

            SetField(app, "loginPanel", loginPanel);
            SetField(app, "consolePanel", consolePanel);
            SetField(app, "garagePanel", garagePanel);
            SetField(app, "teleopPanel", teleopPanel);
            SetField(app, "cameraPanel", cameraPanel);
            SetField(app, "controlsPanel", controlsPanel);
            SetField(app, "garagePanelController", garage);
            SetField(app, "teleopController", teleop);
            SetField(console, "app", app);
            SetField(teleop, "linkProvider", link);
            SetField(picker, "categoryStep", pickerPanels.Category);
            SetField(picker, "typeStep", pickerPanels.Type);
            SetField(picker, "modelStep", pickerPanels.Model);
            SetField(picker, "nameStep", pickerPanels.Name);
            SetField(picker, "confirmStep", pickerPanels.Confirm);

            root.SetActive(true);
            root.SetActive(false);
            SetField(app, "platform", null);
            SetField(app, "auth", null);
            SetField(app, "garage", null);
            root.SetActive(true);
            return root;
        }

        struct PickerPanels
        {
            public GameObject Category;
            public GameObject Type;
            public GameObject Model;
            public GameObject Name;
            public GameObject Confirm;
        }

        static GameObject Child(GameObject parent, string name, bool active)
        {
            var go = new GameObject(name);
            go.SetActive(active);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static Vector2 Stick(float x, float y, float yaw = 0f)
        {
            Yaw = yaw;
            return new Vector2(x, y);
        }

        static float Yaw;

        static void Tick(IDriveScheme drive, FakeRobotLink link, Vector2 left, bool grips, bool press, bool release)
        {
            Tick(drive, link, left, grips, grips, press, release);
        }

        static void Tick(IDriveScheme drive, FakeRobotLink link, Vector2 left, bool leftGrip, bool rightGrip, bool press, bool release)
        {
            var input = new TeleopInput
            {
                LeftStick = left,
                RightStick = new Vector2(Yaw, 0f),
                LeftGrip = leftGrip,
                RightGrip = rightGrip,
                BothGrips = leftGrip && rightGrip,
                BothGripsPressed = press,
                BothGripsReleased = release,
                DeltaTime = Step,
            };
            drive.Tick(ref input, link);
        }

        static void IsolateScene()
        {
            // EmptySceneSetup already replaces scene1. Only drop headset singletons
            // so a leftover broker client cannot satisfy IsConnected, and so this
            // does not destroy the play-mode test runner.
            DestroyComponents<OhhoPlatform>();
            DestroyComponents<SupabaseAuthService>();
            DestroyComponents<GarageClient>();
            DestroyComponents<OhhoCatalog>();
            DestroyComponents<ConnectionManager>();
            DestroyComponents<ROSBridgeClient>();
        }

        static void DestroyNamed(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name || !go.scene.IsValid()) continue;
                Object.DestroyImmediate(go);
            }
        }

        static void DestroyComponents<T>() where T : Component
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
            {
                if (c == null || !c.gameObject.scene.IsValid()) continue;
                Object.DestroyImmediate(c.gameObject);
            }
        }

        static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new System.MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new System.MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        static void Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new System.MissingMethodException(target.GetType().FullName, name);
            method.Invoke(target, args);
        }

        sealed class PrefSnapshot
        {
            readonly Dictionary<string, IntPref> _ints = new Dictionary<string, IntPref>();
            readonly Dictionary<string, StringPref> _strings = new Dictionary<string, StringPref>();

            struct IntPref { public bool Had; public int Value; }
            struct StringPref { public bool Had; public string Value; }

            public static PrefSnapshot Capture()
            {
                var snap = new PrefSnapshot();
                snap.CaptureInt("auto_connect");
                snap.CaptureInt("ohho.has_last_robot");
                snap.CaptureInt("ohho.last_robot_port");
                snap.CaptureString("ohho.last_robot_id");
                snap.CaptureString("ohho.last_robot_name");
                snap.CaptureString("ohho.last_robot_ip");
                snap.CaptureString("ohho.calibration.omnibot-pro");
                return snap;
            }

            public void Restore()
            {
                foreach (var pair in _ints)
                {
                    if (!pair.Value.Had) PlayerPrefs.DeleteKey(pair.Key);
                    else PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                }
                foreach (var pair in _strings)
                {
                    if (!pair.Value.Had) PlayerPrefs.DeleteKey(pair.Key);
                    else PlayerPrefs.SetString(pair.Key, pair.Value.Value ?? "");
                }
                PlayerPrefs.Save();
            }

            void CaptureInt(string key)
            {
                _ints[key] = new IntPref { Had = PlayerPrefs.HasKey(key), Value = PlayerPrefs.GetInt(key, 0) };
            }

            void CaptureString(string key)
            {
                _strings[key] = new StringPref { Had = PlayerPrefs.HasKey(key), Value = PlayerPrefs.GetString(key, "") };
            }
        }
    }

    public sealed class OperatorFlowResult
    {
        public string Detail = "";
        public bool LoginPanelShown;
        public bool SignedOutShowsLogin;
        public bool SignedInShowsConsole;
        public bool PilotOpensGarage;
        public bool PickerCategoryShown;
        public bool PickerTypeShown;
        public bool PickerModelShown;
        public bool PickerNameShown;
        public bool PickerCancelled;
        public bool PickerHiddenAfterCancel;
        public bool TeleopActive;
        public bool TeleopPanelShown;
        public string ProfileRobotId = "";
        public bool DriveIsMecanum;
        public float MaxLinVel = float.NaN;
        public float MaxAngVel = float.NaN;
        public bool AdvertisedCmdVel;
        public bool AdvertisedEmergencyStop;
        public int DeadZoneAddedNonZero = -1;
        public int EStopTrueCount = -1;
        public int NonZeroWhileHeld = -1;
        public int EStopFalseCount = -1;
        public float ResumeLinearX = float.NaN;
        public float ResumeLinearY = float.NaN;
        public float ResumeAngularZ = float.NaN;
        public bool TeleopModePublished;
        public float TurboLinearX = float.NaN;
        public float StopLinearX = float.NaN;
        public float StopLinearY = float.NaN;
        public float StopAngularZ = float.NaN;
        public bool StopPublishedNav2;
        public bool TeleopInactiveAfterStop;
    }

    public sealed class DisconnectedLinkResult
    {
        public string Detail = "";
        public bool TeleopMarkedActive;
        public int PublicationCount = -1;
        public int AdvertisementCount = -1;
    }
}
#endif
