namespace OmniBot.VR.Policy
{
    /// <summary>
    /// Sim sessions may run a policy after confirmation alone. Anything else
    /// fails closed and requires the supervise hold.
    /// </summary>
    public enum PolicySessionKind
    {
        Unspecified,
        Sim,
        Real,
    }

    public enum PolicyRunState
    {
        Idle,
        Loaded,
        Running,
        Stopped,
    }

    /// <summary>One servable policy. <see cref="Checkpoint"/> is what load publishes.</summary>
    public sealed class PolicyDescriptor
    {
        public string Id;
        public string Label;
        public string Checkpoint;
    }

    /// <summary>Guard-trip counters. Missing fields stay unset so the panel does not invent zeros.</summary>
    public struct PolicyGuardTrips
    {
        public bool HasBlackFrame;
        public int BlackFrame;
        public bool HasStaleInference;
        public int StaleInference;
        public bool HasClampedDeltas;
        public int ClampedDeltas;

        public bool HasAny => HasBlackFrame || HasStaleInference || HasClampedDeltas;
    }

    /// <summary>Latency and guard trips shown on the policy panel.</summary>
    public sealed class PolicyStatusView
    {
        public string Source = "none";
        public string PolicyName;
        public string PolicyState;
        public double? LatencyMs;
        public PolicyGuardTrips Trips;
        /// <summary><c>sim</c>, <c>real</c>, or null when the message did not say.</summary>
        public string Kind;
    }

    /// <summary>E-stop and deadman carried on an <c>/ohho/telemetry</c> document.</summary>
    public struct PolicySafetyNote
    {
        public bool HasEstop;
        public bool EstopActive;
        public bool HasDeadman;
        public bool DeadmanActive;
    }

    public struct PolicyCommandResult
    {
        public bool Accepted { get; }
        public string Reason { get; }

        public PolicyCommandResult(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason;
        }

        public static PolicyCommandResult Ok() => new PolicyCommandResult(true, null);

        public static PolicyCommandResult Refuse(string reason) => new PolicyCommandResult(false, reason);
    }

    public static class PolicyRefuse
    {
        public const string NoProfile = "no_profile";
        public const string NotConnected = "not_connected";
        public const string Estop = "estop_active";
        public const string Deadman = "deadman_active";
        public const string Confirmation = "confirmation_required";
        public const string Supervise = "supervise_required";
        public const string NotLoaded = "not_loaded";
        public const string NotSelected = "not_selected";
        public const string Running = "running";
        public const string NoArm = "no_arm";
        public const string TopicsMissing = "topics_missing";
    }
}
