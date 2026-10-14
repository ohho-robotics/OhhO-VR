using System;
using System.IO;
using NUnit.Framework;
using OmniBot.VR.Control;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class LegacyControlQuarantineTests
    {
        private static readonly string[] ArchivedTypes =
        {
            "OmniBot.VR.Input.BaseController",
            "OmniBot.VR.Input.HandTrackingArmController",
            "OmniBot.VR.UI.HUDManager",
            "OmniBot.VR.UI.TelemetryPanel",
            "OmniBot.VR.UI.RecordingPanel",
            "OmniBot.VR.UI.ControlPanel",
            "OmniBot.VR.Recording.DatasetRecorder",
        };

        [Test]
        public void ArchivedPublishers_AreAbsentFromLoadedAssemblies()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var typeName in ArchivedTypes)
                {
                    Assert.IsNull(asm.GetType(typeName), asm.GetName().Name + " still contains " + typeName);
                }
            }
        }

        [Test]
        public void ArmIKSolver_RemainsAvailableToLiveHandIk()
        {
            var solver = typeof(TeleopController).Assembly.GetType("OmniBot.VR.Input.ArmIKSolver");
            Assert.IsNotNull(solver);
        }

        [Test]
        public void SceneBuilders_DoNotAddArchivedComponents()
        {
            string editorDir = Path.Combine(Application.dataPath, "Editor");
            string[] banned =
            {
                "BaseController",
                "HandTrackingArmController",
                "HUDManager",
                "TelemetryPanel",
                "DatasetRecorder",
                "RecordingPanel",
                "ControlPanel",
            };

            foreach (var file in Directory.GetFiles(editorDir, "*.cs"))
            {
                string text = File.ReadAllText(file);
                foreach (var name in banned)
                {
                    Assert.IsFalse(
                        text.Contains("AddComponent<" + name),
                        Path.GetFileName(file) + " adds " + name);
                }
            }
        }
    }
}
