namespace OmniBot.VR.Core
{
    /// <summary>
    /// Exponential reconnect delays: 2 s, 4 s, 8 s, then give up.
    /// Matches the schedule <see cref="ROSBridgeClient"/> has always used.
    /// </summary>
    public sealed class ReconnectBackoff
    {
        public const int MaxAttempts = 3;

        public int Attempts { get; private set; }
        public bool Exhausted => Attempts >= MaxAttempts;

        /// <summary>
        /// Arm the next attempt. Delays are 2, 4, then 8 seconds.
        /// Returns false once <see cref="MaxAttempts"/> is used up.
        /// </summary>
        public bool TrySchedule(out float delaySeconds)
        {
            if (Attempts >= MaxAttempts)
            {
                delaySeconds = 0f;
                return false;
            }

            Attempts++;
            delaySeconds = 1 << Attempts;
            return true;
        }

        public void Reset()
        {
            Attempts = 0;
        }
    }
}
