using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OmniBot.VR.Editor
{
    public class BuildAutomator
    {
        [MenuItem("OmniBot/1-Click Save & Add To Build")]
        public static void SaveAndAddToBuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            
            // If the scene hasn't been saved yet, save it to Assets/
            string path = scene.path;
            if (string.IsNullOrEmpty(path))
            {
                bool saved = EditorSceneManager.SaveScene(scene, "Assets/OmniBotVR_Main.unity");
                if (saved) path = "Assets/OmniBotVR_Main.unity";
                else 
                {
                    Debug.LogError("Failed to save scene!");
                    return;
                }
            }
            
            // Add the current scene to the Build Settings
            var buildScenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(path, true)
            };
            
            EditorBuildSettings.scenes = buildScenes;
            Debug.Log("[OmniBot] Success! The scene was saved to " + path + " and added to your Build Settings.");
            
            // Bring up the build window so they can just hit Build
            EditorWindow.GetWindow(System.Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
        }

        // Batchmode entry point for CI and the OmniBot menu. Android ARM64 IL2CPP.
        // Signs with ci/debug.keystore when that file exists (generated in CI; not a release key).
        [MenuItem("OmniBot/Build Quest APK")]
        public static void BuildQuestApk()
        {
            const string packageId = "com.ohhorobotics.ohhovr";
            PlayerSettings.companyName = "ohho-robotics";
            PlayerSettings.productName = "OhhO-VR";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            if (!TryApplyCiDebugKeystore() && Application.isBatchMode)
            {
                Debug.LogError("[BuildAutomator] ci/debug.keystore is missing. CI must generate the debug keystore before this build. Release signing is OHH-232.");
                EditorApplication.Exit(1);
                return;
            }

            string outDir = "build/Android";
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "OhhO-VR.apk");
            Debug.Log("[BuildAutomator] Android ARM64 IL2CPP Quest APK to " + outPath + " (" + packageId + ")");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/scene1.unity" },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[BuildAutomator] Quest APK build SUCCEEDED (" + summary.totalSize + " bytes) at " + outPath);
                return;
            }

            Debug.LogError("[BuildAutomator] Quest APK build FAILED: result=" + summary.result + ", errors=" + summary.totalErrors);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        // Android debug keystore convention: alias androiddebugkey, store and key password "android".
        // The file is created by .github/workflows/unity-tests.yml and is not a release key (OHH-232).
        static bool TryApplyCiDebugKeystore()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ci", "debug.keystore"));
            if (!File.Exists(path))
            {
                Debug.Log("[BuildAutomator] No ci/debug.keystore; Android keystore settings left unchanged.");
                return false;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = "android";
            PlayerSettings.Android.keyaliasName = "androiddebugkey";
            PlayerSettings.Android.keyaliasPass = "android";
            Debug.Log("[BuildAutomator] Using CI debug keystore at " + path + " (alias androiddebugkey). Not a release key.");
            return true;
        }
    }
}
