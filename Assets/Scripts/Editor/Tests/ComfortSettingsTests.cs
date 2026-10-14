using System.IO;
using NUnit.Framework;
using OmniBot.VR.Comfort;
using OmniBot.VR.Control;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    public class ComfortSettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            ComfortSettingsStore.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            ComfortSettingsStore.Reset();
        }

        [Test]
        public void Defaults_MatchTheFloatingScreen_AndDoNotMoveTheUser()
        {
            var settings = ComfortSettings.CreateDefault();
            Assert.AreEqual(1.2f, settings.ScreenDistanceMeters, 0.001f);
            Assert.AreEqual(1f, settings.ScreenSize, 0.001f);
            Assert.AreEqual(1f, settings.TextScale, 0.001f);
            Assert.IsFalse(settings.LeftHandedControls);
            Assert.IsFalse(ComfortSettings.ForcesUserLocomotion);
            Assert.IsFalse(ComfortSettingsStore.HasSaved);
        }

        [Test]
        public void Save_ThenLoad_RoundTrips_AndClamps()
        {
            var settings = new ComfortSettings
            {
                screenDistanceMeters = 10f,
                screenSize = 0.1f,
                textScale = 9f,
                leftHandedControls = true,
            };
            ComfortSettingsStore.Save(settings);

            var loaded = ComfortSettingsStore.Load();
            Assert.AreEqual(ComfortSettings.MaxScreenDistanceMeters, loaded.ScreenDistanceMeters, 0.001f);
            Assert.AreEqual(ComfortSettings.MinScreenSize, loaded.ScreenSize, 0.001f);
            Assert.AreEqual(ComfortSettings.MaxTextScale, loaded.TextScale, 0.001f);
            Assert.IsTrue(loaded.LeftHandedControls);
            Assert.IsTrue(ComfortSettingsStore.HasSaved);
            Assert.IsFalse(ComfortSettings.ForcesUserLocomotion);
            StringAssert.DoesNotContain("ForcesUserLocomotion", PlayerPrefs.GetString(ComfortSettingsStore.PrefKey));
        }

        [Test]
        public void Save_LowDistance_ClampsToMinimum()
        {
            ComfortSettingsStore.Save(new ComfortSettings { screenDistanceMeters = 0.01f, textScale = 0.1f });
            var loaded = ComfortSettingsStore.Current;
            Assert.AreEqual(ComfortSettings.MinScreenDistanceMeters, loaded.ScreenDistanceMeters, 0.001f);
            Assert.AreEqual(ComfortSettings.MinTextScale, loaded.TextScale, 0.001f);
        }

        [Test]
        public void Reset_ClearsTheSavedValue()
        {
            ComfortSettingsStore.Save(new ComfortSettings { leftHandedControls = true, screenDistanceMeters = 2f });
            ComfortSettingsStore.Reset();
            Assert.IsFalse(ComfortSettingsStore.HasSaved);
            Assert.IsFalse(ComfortSettingsStore.Current.LeftHandedControls);
            Assert.AreEqual(ComfortSettings.DefaultScreenDistanceMeters, ComfortSettingsStore.Current.ScreenDistanceMeters, 0.001f);
        }

        [Test]
        public void Save_RaisesChanged()
        {
            var calls = 0;
            ComfortSettings last = null;
            void Handler(ComfortSettings settings)
            {
                calls++;
                last = settings;
            }

            ComfortSettingsStore.Changed += Handler;
            try
            {
                ComfortSettingsStore.Save(new ComfortSettings { screenSize = 1.5f });
                Assert.AreEqual(1, calls);
                Assert.AreEqual(1.5f, last.ScreenSize, 0.001f);
            }
            finally
            {
                ComfortSettingsStore.Changed -= Handler;
            }
        }

        [Test]
        public void LeftHandedMapping_SwapsSticksAndGrips_AndKeepsEmergencyStopEdges()
        {
            var input = new TeleopInput
            {
                LeftStick = new Vector2(0.2f, 0.3f),
                RightStick = new Vector2(-0.4f, 0.5f),
                LeftGrip = false,
                RightGrip = true,
                BothGrips = false,
                BothGripsPressed = false,
                BothGripsReleased = true,
                DeltaTime = 0.016f,
            };

            var same = ComfortControlMapping.Apply(input, leftHanded: false);
            Assert.AreEqual(input.LeftStick, same.LeftStick);
            Assert.AreEqual(input.RightStick, same.RightStick);
            Assert.AreEqual(input.LeftGrip, same.LeftGrip);
            Assert.AreEqual(input.RightGrip, same.RightGrip);
            Assert.AreEqual(input.BothGripsReleased, same.BothGripsReleased);

            var mapped = ComfortControlMapping.Apply(input, leftHanded: true);
            Assert.AreEqual(input.RightStick, mapped.LeftStick);
            Assert.AreEqual(input.LeftStick, mapped.RightStick);
            Assert.IsTrue(mapped.LeftGrip);
            Assert.IsFalse(mapped.RightGrip);
            Assert.IsFalse(mapped.BothGrips);
            Assert.IsFalse(mapped.BothGripsPressed);
            Assert.IsTrue(mapped.BothGripsReleased);
            Assert.AreEqual(0.016f, mapped.DeltaTime, 0.0001f);

            var stop = new TeleopInput
            {
                LeftGrip = true,
                RightGrip = true,
                BothGrips = true,
                BothGripsPressed = true,
                BothGripsReleased = false,
            };
            var stopped = ComfortControlMapping.Apply(stop, leftHanded: true);
            Assert.IsTrue(stopped.BothGrips);
            Assert.IsTrue(stopped.BothGripsPressed);
            Assert.IsFalse(stopped.BothGripsReleased);
            Assert.IsTrue(stopped.LeftGrip);
            Assert.IsTrue(stopped.RightGrip);
        }

        [Test]
        public void AppScripts_DoNotMoveTheHeadsetUser()
        {
            Assert.IsFalse(ComfortSettings.ForcesUserLocomotion);
            var root = Path.Combine(Application.dataPath, "Scripts");
            var tokens = new[]
            {
                "Locomotor",
                "TeleportInteractor",
                "ContinuousMoveProvider",
                "CharacterController",
            };
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Editor/"))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                foreach (var token in tokens)
                {
                    Assert.IsFalse(
                        text.Contains(token),
                        Path.GetFileName(file) + " contains " + token);
                }
            }
        }
    }
}
