using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Management;

namespace OmniBot.VR.PlayModeTests
{
    /// <summary>
    /// The test runner already replaces the open scene with an empty bootstrap
    /// scene that holds PlaymodeTestsController, before this setup runs. Replacing
    /// that scene again drops the controller, and batchmode then waits forever
    /// for a run that never starts.
    ///
    /// XR Plug-in Management otherwise calls OpenXR InitializeLoaderSync on that
    /// reload. With no headset the display subsystem fails
    /// (XR_ERROR_FORM_FACTOR_UNAVAILABLE) and the editor stops producing results.
    /// Auto-start is turned off for this run only. The settings asset is not saved,
    /// and the runner restores the previous scene itself.
    /// </summary>
    public sealed class EmptySceneSetup : IPrebuildSetup, IPostBuildCleanup
    {
        const string DisableXrKey = "OhhoPlayModeTests.DisableXr";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DisableXrBeforeAutoStart()
        {
            // SubsystemRegistration is before XRGeneralSettings.AttemptInitializeXRSDKOnLoad
            // (AfterAssembliesLoaded). The SessionState flag survives the play-mode
            // domain reload; batchmode covers the CI runner even if that flag was missed.
            if (!Application.isBatchMode && !SessionState.GetBool(DisableXrKey, false))
                return;
            DisableXrInit();
        }

        public void Setup()
        {
            SessionState.SetBool(DisableXrKey, true);
            DisableXrInit();
        }

        public void Cleanup()
        {
            SessionState.EraseBool(DisableXrKey);
            RestoreXrInit();
        }

        static void DisableXrInit()
        {
            Touch(XRGeneralSettings.Instance, false);
            Touch(XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone), false);
            Touch(XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android), false);
        }

        static void RestoreXrInit()
        {
            Touch(XRGeneralSettings.Instance, true);
            Touch(XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone), true);
            Touch(XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android), true);
        }

        static void Touch(XRGeneralSettings settings, bool initOnStart)
        {
            if (settings == null) return;
            settings.InitManagerOnStart = initOnStart;
            var manager = settings.Manager;
            if (manager == null) return;
            if (!initOnStart)
            {
                manager.automaticLoading = false;
                manager.automaticRunning = false;
                if (manager.activeLoader != null)
                    manager.DeinitializeLoader();
            }
        }
    }
}
