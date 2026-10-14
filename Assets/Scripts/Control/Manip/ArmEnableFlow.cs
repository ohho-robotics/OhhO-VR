using UnityEngine;

namespace OmniBot.VR.Control.Manip
{
    public enum ArmEnablePhase
    {
        Off = 0,
        AwaitingRealConfirmation = 1,
        Armed = 2,
    }

    /// <summary>
    /// Whether <see cref="ArmEnableFlow"/> should publish <c>/arm/enable</c>.
    /// A disarm always wins if it is combined with an enable in the same step.
    /// </summary>
    public struct EnableSignal
    {
        public bool Publish;
        public bool Value;
        public bool PhaseChanged;

        public static EnableSignal None => default;

        public static EnableSignal Combine(EnableSignal a, EnableSignal b)
        {
            if (!a.Publish && !b.Publish)
                return new EnableSignal { PhaseChanged = a.PhaseChanged || b.PhaseChanged };
            if (a.Publish && b.Publish && (!a.Value || !b.Value))
                return new EnableSignal { Publish = true, Value = false, PhaseChanged = true };
            if (b.Publish) return b;
            return a;
        }
    }

    /// <summary>
    /// Arm torque gate. Sim arms enable on one explicit request. Real arms
    /// stay off until a second, separate confirm. Motion is not this class —
    /// callers must also require the clutch. Emergency stop clears torque and
    /// does not restore it when the stop is released (same rule as the robot
    /// <c>ArmSafety.on_emergency_stop</c>).
    /// </summary>
    public sealed class ArmEnableFlow
    {
        public ArmDeployment Deployment = ArmDeployment.Real;
        public ArmEnablePhase Phase { get; private set; } = ArmEnablePhase.Off;
        public bool ClutchEngaged { get; private set; }
        public bool EmergencyStopLatched { get; private set; }

        /// <summary>Torque is on. Clutch may still be open, so the arm must not move.</summary>
        public bool TorqueEnabled => Phase == ArmEnablePhase.Armed && !EmergencyStopLatched;

        /// <summary>Torque on and the operator is holding the clutch.</summary>
        public bool MotionAllowed => TorqueEnabled && ClutchEngaged;

        public void Reset(ArmDeployment deployment)
        {
            Deployment = deployment;
            Phase = ArmEnablePhase.Off;
            ClutchEngaged = false;
            EmergencyStopLatched = false;
        }

        public EnableSignal LatchEmergencyStop()
        {
            bool publishOff = Phase == ArmEnablePhase.Armed;
            bool changed = publishOff || !EmergencyStopLatched || Phase != ArmEnablePhase.Off || ClutchEngaged;
            EmergencyStopLatched = true;
            Phase = ArmEnablePhase.Off;
            ClutchEngaged = false;
            if (publishOff)
                return new EnableSignal { Publish = true, Value = false, PhaseChanged = true };
            return new EnableSignal { PhaseChanged = changed };
        }

        public EnableSignal ReleaseEmergencyStop()
        {
            if (!EmergencyStopLatched) return EnableSignal.None;
            EmergencyStopLatched = false;
            Phase = ArmEnablePhase.Off;
            ClutchEngaged = false;
            return new EnableSignal { PhaseChanged = true };
        }

        /// <summary>
        /// Explicit enable request (Left X or thumbs-up). Does not confirm a
        /// pending REAL enable and does not turn an armed arm off.
        /// </summary>
        public EnableSignal RequestEnable()
        {
            if (EmergencyStopLatched || Phase != ArmEnablePhase.Off)
                return EnableSignal.None;

            ClutchEngaged = false;
            if (Deployment == ArmDeployment.Sim)
            {
                Phase = ArmEnablePhase.Armed;
                return new EnableSignal { Publish = true, Value = true, PhaseChanged = true };
            }

            Phase = ArmEnablePhase.AwaitingRealConfirmation;
            return new EnableSignal { PhaseChanged = true };
        }

        /// <summary>Second explicit action. Ignored unless a REAL confirm is pending.</summary>
        public EnableSignal ConfirmReal()
        {
            if (EmergencyStopLatched) return EnableSignal.None;
            if (Deployment != ArmDeployment.Real) return EnableSignal.None;
            if (Phase != ArmEnablePhase.AwaitingRealConfirmation) return EnableSignal.None;

            Phase = ArmEnablePhase.Armed;
            ClutchEngaged = false;
            return new EnableSignal { Publish = true, Value = true, PhaseChanged = true };
        }

        public EnableSignal Cancel()
        {
            if (Phase != ArmEnablePhase.AwaitingRealConfirmation) return EnableSignal.None;
            Phase = ArmEnablePhase.Off;
            ClutchEngaged = false;
            return new EnableSignal { PhaseChanged = true };
        }

        /// <summary>Immediate disarm. Confirmation is not required to turn the arm off.</summary>
        public EnableSignal Disarm()
        {
            if (Phase == ArmEnablePhase.AwaitingRealConfirmation)
            {
                Phase = ArmEnablePhase.Off;
                ClutchEngaged = false;
                return new EnableSignal { PhaseChanged = true };
            }
            if (Phase != ArmEnablePhase.Armed) return EnableSignal.None;
            Phase = ArmEnablePhase.Off;
            ClutchEngaged = false;
            return new EnableSignal { Publish = true, Value = false, PhaseChanged = true };
        }

        public void SetClutch(bool held)
        {
            ClutchEngaged = TorqueEnabled && held;
        }

        public string Describe(bool trackingGood, string jogJoint)
        {
            if (EmergencyStopLatched)
                return "E-stop — arm off until you enable again";
            switch (Phase)
            {
                case ArmEnablePhase.AwaitingRealConfirmation:
                    return "REAL robot — press Left X again to confirm arm enable";
                case ArmEnablePhase.Armed:
                    if (!ClutchEngaged)
                        return "Armed — hold the right index trigger to move";
                    if (!trackingGood)
                        return "Clutch — joint jog (" + (string.IsNullOrEmpty(jogJoint) ? "joint" : jogJoint) + "), right stick Y";
                    return "Clutch — hand IK";
                default:
                    return Deployment == ArmDeployment.Real
                        ? "Arm off — Left X or thumbs-up requests enable. REAL needs a second Left X"
                        : "Arm off — Left X or thumbs-up enables (sim)";
            }
        }
    }

    /// <summary>
    /// One frame of operator intent for the arm gate. Levels (clutch, sticks,
    /// e-stop) persist until the next feed. Edges are one-shot.
    /// </summary>
    public struct ArmOperatorInput
    {
        public bool EnableRequested;
        public bool ConfirmRequested;
        public bool DisarmRequested;
        public bool CancelRequested;
        public bool ClutchHeld;
        public bool TrackingGood;
        public float JogAxis;
        public bool CycleJointPressed;
        public bool EmergencyStop;
        public float DeltaTime;
    }

    /// <summary>
    /// Applies <see cref="ArmOperatorInput"/> to an <see cref="ArmEnableFlow"/>
    /// once per tick. Emergency stop wins over enable. Confirm is ignored on
    /// the same step as a fresh enable request so one gesture cannot skip the
    /// REAL gate.
    /// </summary>
    public sealed class ArmSessionGate
    {
        public readonly ArmEnableFlow Flow = new ArmEnableFlow();
        public ArmOperatorInput Input { get; private set; }
        public bool ClutchRising { get; private set; }

        public void Bind(RobotProfile profile)
        {
            Flow.Reset(profile != null ? profile.Deployment : ArmDeployment.Real);
        }

        public EnableSignal Consume(ArmOperatorInput input)
        {
            Input = input;
            if (input.EmergencyStop)
            {
                ClutchRising = false;
                return Flow.LatchEmergencyStop();
            }

            EnableSignal signal = Flow.ReleaseEmergencyStop();
            if (input.DisarmRequested)
                signal = EnableSignal.Combine(signal, Flow.Disarm());
            else if (input.CancelRequested)
                signal = EnableSignal.Combine(signal, Flow.Cancel());
            else if (input.ConfirmRequested && !input.EnableRequested)
                signal = EnableSignal.Combine(signal, Flow.ConfirmReal());
            else if (input.EnableRequested)
                signal = EnableSignal.Combine(signal, Flow.RequestEnable());

            bool prevClutch = Flow.ClutchEngaged;
            Flow.SetClutch(input.ClutchHeld);
            ClutchRising = Flow.ClutchEngaged && !prevClutch;
            return signal;
        }
    }

    public static class ArmEnablePublish
    {
        public static void Apply(EnableSignal signal, RobotProfile profile, IRobotLink link)
        {
            if (!signal.Publish || profile == null || profile.Topics == null || link == null || !link.IsConnected)
                return;
            if (string.IsNullOrEmpty(profile.Topics.ArmEnable)) return;
            link.Publish(profile.Topics.ArmEnable, new Core.BoolMsg(signal.Value));
            Debug.Log("[ArmEnable] /arm/enable " + (signal.Value ? "true" : "false"));
        }
    }
}
