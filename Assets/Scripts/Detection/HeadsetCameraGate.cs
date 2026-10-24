using System.Collections.Generic;

namespace OmniBot.VR.Detection
{
    /// <summary>
    /// One permission the headset app explains to the user.
    /// </summary>
    public readonly struct PermissionNotice
    {
        public PermissionNotice(string permissionId, string rationale, bool requestedAtRuntime)
        {
            PermissionId = permissionId ?? "";
            Rationale = rationale ?? "";
            RequestedAtRuntime = requestedAtRuntime;
        }

        public string PermissionId { get; }
        public string Rationale { get; }

        /// <summary>
        /// True when OhhO code calls the Android permission dialog for this id.
        /// A manifest <c>uses-permission</c> that the app does not request is false.
        /// </summary>
        public bool RequestedAtRuntime { get; }
    }

    /// <summary>
    /// Headset camera permission for robot detection. The editor build of
    /// <c>OVRPermissionsRequester.IsPermissionGranted</c> returns true, which is
    /// not a Quest grant. This gate treats only an Android player as a headset.
    /// </summary>
    public static class HeadsetCameraGate
    {
        public const string HandTrackingPermissionId = "com.oculus.permission.HAND_TRACKING";

        public const string Rationale =
            "OhhO uses the headset camera to see the AprilTags on the robot and lock the robot in the room. Images stay on the headset. If you deny camera access, robot detection stays off.";

        public const string HandTrackingRationale =
            "OhhO uses hand tracking so a hand pose can drive the robot arm. Hand poses stay on the headset. The base still drives from the controllers if hand tracking is unavailable.";

        public const string DeniedReason =
            "Headset camera permission was denied. Robot detection is off. Place the robot manually. Headset camera images are not used.";

        /// <summary>
        /// Runtime and manifest permissions this app explains.
        /// Headset camera is the only one OhhO requests at runtime
        /// (<see cref="RequestAccess"/>). Hand tracking is declared in
        /// <c>Assets/Plugins/Android/AndroidManifest.xml</c> and is not requested
        /// through <c>OVRPermissionsRequester</c>. Face, body, eye, scene, and
        /// microphone startup requests stay off.
        /// </summary>
        public static IReadOnlyList<PermissionNotice> Notices { get; } = new[]
        {
            new PermissionNotice(
                OVRPermissionsRequester.PassthroughCameraAccessPermission,
                Rationale,
                requestedAtRuntime: true),
            new PermissionNotice(
                HandTrackingPermissionId,
                HandTrackingRationale,
                requestedAtRuntime: false),
        };

        public const string EditorReason =
            "Robot detection needs the Quest headset camera. It is off in the editor.";

        public static bool IsHeadsetPlayer
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsGrantedOnHeadset()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return OVRPermissionsRequester.IsPermissionGranted(
                OVRPermissionsRequester.Permission.PassthroughCameraAccess);
#else
            return false;
#endif
        }

        public static void RequestAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.PassthroughCameraAccess,
            });
#endif
        }
    }
}
