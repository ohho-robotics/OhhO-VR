namespace OmniBot.VR.Detection
{
    /// <summary>
    /// Headset camera permission for robot detection. The editor build of
    /// <c>OVRPermissionsRequester.IsPermissionGranted</c> returns true, which is
    /// not a Quest grant. This gate treats only an Android player as a headset.
    /// </summary>
    public static class HeadsetCameraGate
    {
        public const string Rationale =
            "OhhO uses the headset camera to see the AprilTags on the robot and lock the robot in the room. Images stay on the headset. If you deny camera access, robot detection stays off.";

        public const string DeniedReason =
            "Headset camera permission was denied. Robot detection is off.";

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
