using System.IO;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Safety;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Edit-mode tests for the versioned real-session acknowledgement.
    /// Restores whatever operator-rules version was already in PlayerPrefs.
    /// </summary>
    public class OperatorRulesAcknowledgementTests
    {
        private bool _hadKey;
        private int _previousVersion;

        [SetUp]
        public void SetUp()
        {
            _hadKey = PlayerPrefs.HasKey(OperatorRulesAcknowledgement.PrefKey);
            _previousVersion = PlayerPrefs.GetInt(OperatorRulesAcknowledgement.PrefKey, 0);
            OperatorRulesAcknowledgement.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadKey)
            {
                PlayerPrefs.SetInt(OperatorRulesAcknowledgement.PrefKey, _previousVersion);
                PlayerPrefs.Save();
            }
            else
            {
                OperatorRulesAcknowledgement.Clear();
            }
        }

        [Test]
        public void RealSession_IsBlocked_UntilCurrentRulesAreAccepted()
        {
            Assert.IsTrue(OperatorRulesAcknowledgement.Blocks(SessionTarget.Real));
            string refusal;
            Assert.IsFalse(RealSessionGate.TryBegin(SessionTarget.Real, out refusal));
            StringAssert.Contains(
                "version " + OperatorRulesAcknowledgement.RulesVersion,
                refusal);
        }

        [Test]
        public void SimSession_IsAllowed_WithoutAcceptance()
        {
            string refusal;
            Assert.IsFalse(OperatorRulesAcknowledgement.Blocks(SessionTarget.Sim));
            Assert.IsTrue(RealSessionGate.TryBegin(SessionTarget.Sim, out refusal));
            Assert.IsNull(refusal);
        }

        [Test]
        public void UnspecifiedSession_IsBlocked_WithoutAcceptance()
        {
            Assert.AreEqual(SessionTarget.Unspecified, default(SessionTarget));
            Assert.IsTrue(OperatorRulesAcknowledgement.Blocks(SessionTarget.Unspecified));
            string refusal;
            Assert.IsFalse(RealSessionGate.TryBegin(SessionTarget.Unspecified, out refusal));
            Assert.IsNotNull(refusal);
        }

        [Test]
        public void Accept_AllowsRealSession_AndPersistsTheVersion()
        {
            OperatorRulesAcknowledgement.Accept();

            Assert.AreEqual(
                OperatorRulesAcknowledgement.RulesVersion,
                PlayerPrefs.GetInt(OperatorRulesAcknowledgement.PrefKey, 0));
            Assert.IsTrue(OperatorRulesAcknowledgement.IsCurrentVersionAccepted);

            string refusal;
            Assert.IsTrue(RealSessionGate.TryBegin(SessionTarget.Real, out refusal));
            Assert.IsNull(refusal);
        }

        [Test]
        public void StoredOlderVersion_BlocksReal_AndStillAllowsSim()
        {
            OperatorRulesAcknowledgement.Accept();
            int bumped = OperatorRulesAcknowledgement.RulesVersion + 1;

            Assert.IsFalse(OperatorRulesAcknowledgement.Blocks(
                SessionTarget.Real, OperatorRulesAcknowledgement.RulesVersion));
            Assert.IsTrue(OperatorRulesAcknowledgement.Blocks(SessionTarget.Real, bumped));
            Assert.IsFalse(OperatorRulesAcknowledgement.Blocks(SessionTarget.Sim, bumped));
        }

        [Test]
        public void Clear_RequiresAcceptanceAgain()
        {
            OperatorRulesAcknowledgement.Accept();
            OperatorRulesAcknowledgement.Clear();

            Assert.IsFalse(OperatorRulesAcknowledgement.IsCurrentVersionAccepted);
            Assert.IsTrue(OperatorRulesAcknowledgement.Blocks(SessionTarget.Real));
            Assert.IsFalse(OperatorRulesAcknowledgement.Blocks(SessionTarget.Sim));
        }

        [Test]
        public void Component_ForwardsTheSameDecision()
        {
            var go = new GameObject("ohh-245-ack-test");
            try
            {
                var ack = go.AddComponent<RealSessionAcknowledgement>();
                Assert.AreEqual(OperatorRulesAcknowledgement.Summary, ack.Summary);
                Assert.IsFalse(ack.CurrentRulesAccepted);

                string refusal;
                Assert.IsFalse(ack.TryBegin(SessionTarget.Real, out refusal));
                Assert.IsNotNull(refusal);
                Assert.IsTrue(ack.TryBegin(SessionTarget.Sim, out refusal));

                ack.AcceptCurrentRules();
                Assert.IsTrue(ack.CurrentRulesAccepted);
                Assert.IsTrue(ack.TryBegin(SessionTarget.Real, out refusal));
                Assert.IsNull(refusal);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OperatorRulesDocument_MatchesRulesVersion_AndMandatoryPhrases()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", OperatorRulesAcknowledgement.RulesDocument));
            string doc = File.ReadAllText(path);

            StringAssert.Contains(
                "Rules version: " + OperatorRulesAcknowledgement.RulesVersion,
                doc);
            foreach (string phrase in OperatorRulesAcknowledgement.MandatoryPhrases)
            {
                StringAssert.Contains(phrase, OperatorRulesAcknowledgement.Summary);
                StringAssert.Contains(phrase, doc);
            }
        }

        [Test]
        public void WiringTodo_NamesTheUnwiredStartPath()
        {
            StringAssert.Contains("TODO(OHH-245)", RealSessionGate.WiringTodo);
            StringAssert.Contains("OhhoVrApp.OnRobotPicked", RealSessionGate.WiringTodo);
            StringAssert.Contains("SessionTarget.Sim", RealSessionGate.WiringTodo);
            StringAssert.Contains("SessionTarget.Real", RealSessionGate.WiringTodo);
        }
    }
}
