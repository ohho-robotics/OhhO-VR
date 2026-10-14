using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace OmniBot.VR.PlayModeTests
{
    /// <summary>
    /// Play-mode tests start from an empty scene so scene1's network clients and
    /// XR objects are not awake. The empty scene is not saved. Outside batchmode
    /// the previously open scene is restored after the run.
    /// </summary>
    public sealed class EmptySceneSetup : IPrebuildSetup, IPostBuildCleanup
    {
        const string PreviousSceneKey = "OhhoPlayModeTests.PreviousScene";

        public void Setup()
        {
            var active = EditorSceneManager.GetActiveScene();
            SessionState.SetString(PreviousSceneKey, active.path ?? "");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public void Cleanup()
        {
            if (Application.isBatchMode) return;
            string path = SessionState.GetString(PreviousSceneKey, "");
            if (string.IsNullOrEmpty(path)) return;
            EditorSceneManager.OpenScene(path);
        }
    }
}
