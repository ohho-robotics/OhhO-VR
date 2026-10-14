using UnityEngine;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Control;
using OmniBot.VR.Control.Session;
using OmniBot.VR.UI;
using OmniBot.VR.UI.Garage;
using OmniBot.VR.UI.Session;

namespace OmniBot.VR.App
{
    /// <summary>
    /// Top-level app flow for the OhhO VR headset — mirrors the website's own
    /// journey: <b>sign in → console (VR products) → open Pilot → garage → simulate or drive real → teleop</b>.
    ///
    /// It wires the platform services together once the manifest loads (so the
    /// Supabase auth + garage clients get the same project the website uses), then
    /// routes between panels based on auth state. A returning user with a saved
    /// session skips straight past login — the connected-experience goal: sign in
    /// once, anywhere, and your robots are already there.
    ///
    /// Drop this on a bootstrap GameObject alongside OhhoPlatform, OhhoCatalog,
    /// SupabaseAuthService and GarageClient, and assign the three panels.
    /// </summary>
    public class OhhoVrApp : MonoBehaviour
    {
        [Header("Panels (assign in the scene)")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private GameObject consolePanel;
        [SerializeField] private GameObject garagePanel;
        [Tooltip("Simulate / Drive real, shown after a garage pick and before teleop.")]
        [SerializeField] private GameObject sessionPickerPanel;
        [Tooltip("The teleop page shown after a session target is chosen (camera feed, telemetry, recording).")]
        [SerializeField] private GameObject teleopPanel;
        [SerializeField] private GameObject cameraPanel;
        [SerializeField] private GameObject controlsPanel;

        [Header("Services")]
        [SerializeField] private OhhoPlatform platform;
        [SerializeField] private SupabaseAuthService auth;
        [SerializeField] private GarageClient garage;

        [Header("Teleop (Phase 2)")]
        [Tooltip("The garage panel controller — listened to for robot selection.")]
        [SerializeField] private GaragePanelController garagePanelController;
        [Tooltip("The fleet panel controller — enhanced garage with quick-resume. Optional.")]
        [SerializeField] private FleetPanelController fleetPanelController;
        [Tooltip("The teleop control layer. Receives the selected robot's profile.")]
        [SerializeField] private TeleopController teleopController;

        [Header("Phase 5 — polish")]
        [Tooltip("First-run onboarding overlay. Shows control hints on the first teleop session.")]
        [SerializeField] private OnboardingController onboarding;

        private SessionPickerPanel _picker;
        private bool _pickerHooked;
        private RobotProfile _pendingProfile;
        private string _pendingName;
        private string _pendingUserRobotId;
        private OfflineKinematicLink _offlineLink;

        private void Awake()
        {
            if (platform == null) platform = OhhoPlatform.Instance;
            if (auth == null) auth = SupabaseAuthService.Instance;
            if (garage == null) garage = GarageClient.Instance;
        }

        private void OnEnable()
        {
            if (platform != null) platform.OnLoaded += OnManifestLoaded;
            if (auth != null) auth.OnAuthChanged += OnAuthChanged;
            if (garagePanelController != null) garagePanelController.RobotPicked += OnRobotPicked;
            if (fleetPanelController != null)
            {
                fleetPanelController.RobotPicked += OnRobotPicked;
                fleetPanelController.QuickResume += OnQuickResume;
            }
        }

        private void OnDisable()
        {
            if (platform != null) platform.OnLoaded -= OnManifestLoaded;
            if (auth != null) auth.OnAuthChanged -= OnAuthChanged;
            if (garagePanelController != null) garagePanelController.RobotPicked -= OnRobotPicked;
            if (fleetPanelController != null)
            {
                fleetPanelController.RobotPicked -= OnRobotPicked;
                fleetPanelController.QuickResume -= OnQuickResume;
            }
            UnhookPicker();
        }

        private void Start()
        {
            // Show login immediately; the manifest load decides where we land.
            ShowLogin();
            if (platform != null && platform.Manifest != null) OnManifestLoaded(platform.Manifest);
        }

        private void OnManifestLoaded(VrManifest manifest)
        {
            // Configure auth + garage with the same Supabase project the site uses.
            auth?.Configure(manifest.Auth);
            garage?.Configure(manifest.Auth);

            // Returning user? Restore the session and skip login.
            if (auth != null)
                auth.RestoreSession(restored => { if (restored) ShowConsole(); else ShowLogin(); });
        }

        private void OnAuthChanged(bool signedIn)
        {
            if (signedIn) ShowConsole();
            else ShowLogin();
        }

        // ── Panel routing ─────────────────────────────────────────────────────
        public void ShowLogin() => Activate(login: true, console: false, garage: false, teleop: false);
        public void ShowConsole() => Activate(login: false, console: true, garage: false, teleop: false);
        public void ShowGarage() => Activate(login: false, console: false, garage: true, teleop: false);
        public void ShowSessionPicker()
        {
            EnsurePicker();
            Activate(login: false, console: false, garage: false, teleop: false, session: true);
        }
        public void ShowTeleop()
        {
            Activate(login: false, console: false, garage: false, teleop: true, camera: true, controls: true);
            if (ActiveSession.Current != null)
                PaintSessionBanners(ActiveSession.Current.Kind);
        }

        private void Activate(bool login, bool console, bool garage, bool teleop, bool camera = false, bool controls = false, bool session = false)
        {
            if (loginPanel != null) loginPanel.SetActive(login);
            if (consolePanel != null) consolePanel.SetActive(console);
            if (garagePanel != null) garagePanel.SetActive(garage);
            if (teleopPanel != null) teleopPanel.SetActive(teleop);
            if (cameraPanel != null) cameraPanel.SetActive(camera);
            if (controlsPanel != null) controlsPanel.SetActive(controls);
            if (sessionPickerPanel != null) sessionPickerPanel.SetActive(session);
        }

        /// <summary>Open ohho-robotics.com in the headset's system browser.</summary>
        public void OpenWebsite()
        {
            string url = platform != null ? platform.PlatformBaseUrl : "https://ohho-robotics.com";
            Application.OpenURL(url);
        }

        // ── Phase 2: robot selection → teleop ──────────────────────────────────

        /// <summary>
        /// Called when the user picks a robot in the garage. Builds the profile,
        /// then opens the session picker (Simulate / Drive real) instead of
        /// jumping straight to teleop.
        /// </summary>
        private void OnRobotPicked(GarageRobot robot)
        {
            BeginRobot(robot, quickResume: false);
        }

        /// <summary>
        /// Quick-resume: jump straight into teleop with the last-driven robot.
        /// Resolves the saved robot id from the catalog, builds + calibrates the
        /// profile, and starts teleop. Falls back to the garage if the robot is
        /// no longer in the user's account.
        /// </summary>
        private void OnQuickResume()
        {
            if (!LastRobotStore.HasLastRobot)
            {
                ShowGarage();
                return;
            }

            string robotId = LastRobotStore.LastRobotId;
            var catalog = OhhoCatalog.Instance;
            if (catalog == null || !catalog.IsLoaded) { ShowGarage(); return; }

            // Find the robot in the user's garage by hardware model id
            var garageClient = GarageClient.Instance;
            if (garageClient == null) { ShowGarage(); return; }

            garageClient.GetUserRobots((robots, err) =>
            {
                if (err != null || robots == null) { ShowGarage(); return; }
                Core.Platform.UserRobot match = null;
                foreach (var r in robots)
                {
                    if (r.HardwareModelId == robotId) { match = r; break; }
                }
                if (match == null) { ShowGarage(); return; }
                var resolved = catalog.Resolve(match);
                if (resolved == null) { ShowGarage(); return; }
                BeginRobot(resolved, quickResume: true);
            });
        }

        /// <summary>Stop teleoperation and return to the garage.</summary>
        public void StopTeleopAndReturnToGarage()
        {
            if (teleopController != null) teleopController.StopTeleop();
            ShowGarage();
        }

        // ── Session picker ────────────────────────────────────────────────────

        private void BeginRobot(GarageRobot robot, bool quickResume)
        {
            if (robot == null) return;
            var profile = RobotProfileFactory.FromGarageRobot(robot);
            if (profile == null)
            {
                Debug.LogWarning($"[OhhoVrApp] Could not build a profile for {robot.DisplayName}.");
                return;
            }
            if (teleopController == null)
            {
                Debug.LogWarning("[OhhoVrApp] TeleopController not assigned — cannot start teleop.");
                return;
            }

            var cal = CalibrationManager.Load(profile.RobotId);
            CalibrationManager.ApplyTo(profile, cal);

            _pendingProfile = profile;
            _pendingName = robot.DisplayName;
            _pendingUserRobotId = robot.UserRobot != null ? robot.UserRobot.Id : null;

            RobotEndpoint saved = null;
            bool hasChoice = LastRobotStore.TryLoadChoice(profile.RobotId, out saved);
            if (!hasChoice && !string.IsNullOrEmpty(_pendingUserRobotId))
                hasChoice = LastRobotStore.TryLoadChoice(_pendingUserRobotId, out saved);

            // Quick-resume of a sim target skips the picker. A real robot still
            // stops on the safety notice — resume must not skip that.
            if (quickResume && hasChoice && saved != null && saved.Kind == SessionKind.Sim)
            {
                OnSessionConfirmed(SessionPlanner.FromSaved(saved, safetyAcknowledged: false, tutorialSeen: true));
                return;
            }

            string ip = EndpointAddresses.DefaultIp;
            int port = EndpointAddresses.RosbridgePort;
            if (hasChoice && saved != null && !string.IsNullOrEmpty(saved.Host))
            {
                ip = saved.Host;
                if (saved.Port > 0) port = saved.Port;
            }
            else
            {
                var conn = ConnectionManager.Instance;
                if (conn != null)
                {
                    ip = conn.CurrentIp;
                    port = conn.CurrentPort;
                }
            }

            var step = quickResume && hasChoice && saved != null && saved.Kind == SessionKind.Real
                ? PickerStep.RealSafety
                : PickerStep.Mode;
            EnsurePicker();
            ShowSessionPicker();
            _picker.Open(profile.RobotId, _pendingUserRobotId, robot.DisplayName, ip, port, step);
        }

        private void OnSessionConfirmed(SessionLaunch plan)
        {
            if (plan == null || !plan.ReadyToDrive || plan.Endpoint == null || _pendingProfile == null) return;
            if (teleopController == null) return;
            if (!plan.DeadmanRequired)
            {
                Debug.LogWarning("[OhhoVrApp] Refusing a session that would turn the deadman off.");
                return;
            }

            var endpoint = plan.Endpoint;
            if (endpoint.Kind == SessionKind.Real)
                endpoint.Offline = false;

            if (!endpoint.Offline && (endpoint.VideoUrls == null || endpoint.VideoUrls.Count == 0))
                endpoint.VideoUrls = EndpointAddresses.VideoUrlsFor(endpoint.Host, _pendingProfile.Topics != null
                    ? _pendingProfile.Topics.Images
                    : null);

            RealRobotSafety.Apply(_pendingProfile, endpoint.Kind);
            if (endpoint.Kind == SessionKind.Real && !_pendingProfile.RequireDeadman)
            {
                Debug.LogWarning("[OhhoVrApp] Refusing real teleop because the deadman is off.");
                return;
            }

            ActiveSession.Set(endpoint);
            string host = endpoint.Offline ? "in-headset" : endpoint.Host;
            int port = endpoint.Port > 0 ? endpoint.Port : EndpointAddresses.RosbridgePort;
            LastRobotStore.Save(_pendingProfile.RobotId, _pendingName, host, port, endpoint);
            if (!string.IsNullOrEmpty(_pendingUserRobotId) && _pendingUserRobotId != _pendingProfile.RobotId)
            {
                LastRobotStore.SaveChoice(_pendingUserRobotId, endpoint);
                LastRobotStore.RememberAlias(_pendingUserRobotId);
            }

            if (endpoint.Offline)
            {
                var conn = ConnectionManager.Instance;
                if (conn != null && conn.IsConnected) conn.Disconnect();
                if (_offlineLink == null) _offlineLink = new OfflineKinematicLink();
                _offlineLink.IsConnected = true;
                teleopController.SetLink(_offlineLink);
            }
            else
            {
                teleopController.SetLink(null);
                ConnectionManager.Instance.ConnectEndpoint(endpoint);
            }

            teleopController.StartTeleop(_pendingProfile);
            ShowTeleop();
            PaintSessionBanners(endpoint.Kind);

            if (onboarding != null)
                onboarding.ShowIfFirstTime(teleopController.DriveHint, teleopController.ManipHint);
        }

        private void PaintSessionBanners(SessionKind kind)
        {
            SessionModeBanner.Apply(teleopPanel, kind);
            SessionModeBanner.Apply(cameraPanel, kind);
            SessionModeBanner.Apply(controlsPanel, kind);
            SessionModeBanner.Apply(garagePanel, kind);
            SessionModeBanner.Apply(SessionPage, kind);
        }

        private GameObject SessionPage => _picker != null ? _picker.gameObject : sessionPickerPanel;

        private void EnsurePicker()
        {
            if (_picker != null)
            {
                HookPicker();
                return;
            }

            if (sessionPickerPanel != null)
            {
                _picker = sessionPickerPanel.GetComponent<SessionPickerPanel>();
                if (_picker == null) _picker = sessionPickerPanel.AddComponent<SessionPickerPanel>();
            }
            else
            {
                var parent = garagePanel != null ? garagePanel.transform.parent : transform;
                var go = new GameObject("Session Page", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                if (garagePanel != null)
                {
                    var src = garagePanel.GetComponent<RectTransform>();
                    var dst = (RectTransform)go.transform;
                    dst.anchorMin = src.anchorMin;
                    dst.anchorMax = src.anchorMax;
                    dst.pivot = src.pivot;
                    dst.anchoredPosition = src.anchoredPosition;
                    dst.sizeDelta = src.sizeDelta;
                    dst.offsetMin = src.offsetMin;
                    dst.offsetMax = src.offsetMax;
                    dst.localRotation = src.localRotation;
                    dst.localScale = src.localScale;
                }
                go.SetActive(false);
                _picker = go.AddComponent<SessionPickerPanel>();
                sessionPickerPanel = go;
            }

            HookPicker();
        }

        private void HookPicker()
        {
            if (_pickerHooked || _picker == null) return;
            _picker.Confirmed += OnSessionConfirmed;
            _picker.Cancelled += ShowGarage;
            _pickerHooked = true;
        }

        private void UnhookPicker()
        {
            if (!_pickerHooked || _picker == null) return;
            _picker.Confirmed -= OnSessionConfirmed;
            _picker.Cancelled -= ShowGarage;
            _pickerHooked = false;
        }
    }
}
