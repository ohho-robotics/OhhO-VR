using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace OmniBot.VR.PlayModeTests
{
    /// <summary>
    /// Play-mode coverage for login stub → garage → picker → teleop → e-stop.
    /// The fixture runs in an empty scene (<see cref="EmptySceneSetup"/>) and
    /// talks to FakeRobotLink, which is compiled into Assembly-CSharp from the
    /// staged Runtime folder. This assembly cannot reference Assembly-CSharp,
    /// so the harness is invoked by name.
    /// </summary>
    [PrebuildSetup("OmniBot.VR.PlayModeTests.EmptySceneSetup")]
    [PostBuildCleanup("OmniBot.VR.PlayModeTests.EmptySceneSetup")]
    public class OperatorFlowPlayModeTests
    {
        [Test]
        public void LoginStub_Garage_Picker_Teleop_EStop_OnFakeLink()
        {
            var result = Run("OmniBot.VR.PlayMode.OperatorFlowHarness", "RunLoginGaragePickerTeleopEStop");
            string detail = Field<string>(result, "Detail");

            Assert.IsTrue(Field<bool>(result, "LoginPanelShown"), detail);
            Assert.IsTrue(Field<bool>(result, "SignedOutShowsLogin"), detail);
            Assert.IsTrue(Field<bool>(result, "SignedInShowsConsole"), detail);
            Assert.IsTrue(Field<bool>(result, "PilotOpensGarage"), detail);

            Assert.IsTrue(Field<bool>(result, "PickerCategoryShown"), detail);
            Assert.IsTrue(Field<bool>(result, "PickerTypeShown"), detail);
            Assert.IsTrue(Field<bool>(result, "PickerModelShown"), detail);
            Assert.IsTrue(Field<bool>(result, "PickerNameShown"), detail);
            Assert.IsTrue(Field<bool>(result, "PickerCancelled"), detail);
            Assert.IsTrue(Field<bool>(result, "PickerHiddenAfterCancel"), detail);

            Assert.IsTrue(Field<bool>(result, "TeleopActive"), detail);
            Assert.IsTrue(Field<bool>(result, "TeleopPanelShown"), detail);
            Assert.AreEqual("omnibot-pro", Field<string>(result, "ProfileRobotId"), detail);
            Assert.IsTrue(Field<bool>(result, "DriveIsMecanum"), detail);
            Assert.AreEqual(0.2f, Field<float>(result, "MaxLinVel"), 0.0001f, detail);
            Assert.AreEqual(1.0f, Field<float>(result, "MaxAngVel"), 0.0001f, detail);
            Assert.IsTrue(Field<bool>(result, "AdvertisedCmdVel"), detail);
            Assert.IsTrue(Field<bool>(result, "AdvertisedEmergencyStop"), detail);

            Assert.AreEqual(0, Field<int>(result, "DeadZoneAddedNonZero"), detail);
            Assert.AreEqual(1, Field<int>(result, "EStopTrueCount"), detail);
            Assert.AreEqual(0, Field<int>(result, "NonZeroWhileHeld"), detail);
            Assert.AreEqual(1, Field<int>(result, "EStopFalseCount"), detail);

            Assert.AreEqual(0.2f, Field<float>(result, "ResumeLinearX"), 0.0001f, detail);
            Assert.AreEqual(0.2f, Field<float>(result, "ResumeLinearY"), 0.0001f, detail);
            Assert.AreEqual(-1.0f, Field<float>(result, "ResumeAngularZ"), 0.0001f, detail);
            Assert.IsTrue(Field<bool>(result, "TeleopModePublished"), detail);
            Assert.AreEqual(0.2f, Field<float>(result, "TurboLinearX"), 0.0001f, detail);

            Assert.AreEqual(0f, Field<float>(result, "StopLinearX"), 0.0001f, detail);
            Assert.AreEqual(0f, Field<float>(result, "StopLinearY"), 0.0001f, detail);
            Assert.AreEqual(0f, Field<float>(result, "StopAngularZ"), 0.0001f, detail);
            Assert.IsTrue(Field<bool>(result, "StopPublishedNav2"), detail);
            Assert.IsTrue(Field<bool>(result, "TeleopInactiveAfterStop"), detail);
        }

        [Test]
        public void DisconnectedFakeLink_PublishesNothing()
        {
            var result = Run("OmniBot.VR.PlayMode.OperatorFlowHarness", "RunDisconnected");
            string detail = Field<string>(result, "Detail");
            Assert.AreEqual(0, Field<int>(result, "PublicationCount"), detail);
            Assert.AreEqual(0, Field<int>(result, "AdvertisementCount"), detail);
        }

        internal static object Run(string typeName, string methodName)
        {
            var type = FindType(typeName);
            Assert.IsNotNull(type,
                typeName + " is not in Assembly-CSharp. From the repo root run " +
                "bash Tests/PlayMode/stage-into-assets.sh before play-mode tests.");
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, methodName);
            try
            {
                return method.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
                throw;
            }
        }

        internal static T Field<T>(object box, string name)
        {
            var field = box.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(field, name);
            return (T)field.GetValue(box);
        }

        internal static Type FindType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
