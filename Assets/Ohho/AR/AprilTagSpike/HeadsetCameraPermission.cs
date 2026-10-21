namespace OmniBot.VR.AprilTagSpike
{
    /// <summary>
    /// Android permission for the Quest Passthrough Camera API (Horizon OS v74+).
    /// The string matches <c>OVRPermissionsRequester.PassthroughCameraAccessPermission</c>
    /// in Meta XR Core SDK 203.0.0. Duplicated so this spike does not request it.
    /// </summary>
    public static class HeadsetCameraPermission
    {
        public const string PermissionId = "horizonos.permission.HEADSET_CAMERA";

        /// <summary>Meta's floor for the Passthrough Camera API on Quest 3 / 3S.</summary>
        public const int MinimumHorizonOsVersion = 74;
    }
}
