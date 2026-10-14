using UnityEngine;

namespace OmniBot.VR.Safety
{
    /// <summary>
    /// In-app first-run acknowledgement for operator rules.
    /// Add this component beside the session starter. It does not subscribe to
    /// input and it does not start teleop. The starter calls <see cref="TryBegin"/>
    /// and shows <see cref="Summary"/> until the operator calls
    /// <see cref="AcceptCurrentRules"/>.
    /// Scene1 is unchanged: the garage pick path cannot yet tell a real robot
    /// from a sim, so this component is the hook rather than an automatic block
    /// of every session. See <see cref="RealSessionGate.WiringTodo"/>.
    /// </summary>
    public sealed class RealSessionAcknowledgement : MonoBehaviour
    {
        public string Summary => OperatorRulesAcknowledgement.Summary;

        public int RulesVersion => OperatorRulesAcknowledgement.RulesVersion;

        public bool CurrentRulesAccepted =>
            OperatorRulesAcknowledgement.IsCurrentVersionAccepted;

        public void AcceptCurrentRules() => OperatorRulesAcknowledgement.Accept();

        public bool TryBegin(SessionTarget target, out string refusal) =>
            RealSessionGate.TryBegin(target, out refusal);
    }
}
