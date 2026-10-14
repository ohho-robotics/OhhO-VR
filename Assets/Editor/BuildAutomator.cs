using UnityEditor;
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

        [MenuItem("OmniBot/Build Quest APK")]
        public static void BuildQuestApk()
        {
            string[] scenes = new string[] { "Assets/scene1.unity" };
            string outDir = "build/Android";
            string outPath = $"{outDir}/OhhO-VR.apk";

            if (!System.IO.Directory.Exists(outDir))
            {
                System.IO.Directory.CreateDirectory(outDir);
            }

            Debug.Log($"[BuildAutomator] Starting Android Quest APK build to {outPath}...");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[BuildAutomator] Quest APK build SUCCEEDED ({summary.totalSize} bytes) at {outPath}");
            }
            else
            {
                Debug.LogError($"[BuildAutomator] Quest APK build FAILED: result={summary.result}, errors={summary.totalErrors}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
