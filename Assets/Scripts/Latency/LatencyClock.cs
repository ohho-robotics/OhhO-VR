using System;

namespace OmniBot.VR.Latency
{
    /// <summary>Millisecond clock used by the harness. Tests inject a manual clock.</summary>
    public interface ILatencyClock
    {
        long NowMs { get; }
        string Kind { get; }
    }

    public sealed class SystemLatencyClock : ILatencyClock
    {
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public string Kind => "system";
    }

    public sealed class ManualLatencyClock : ILatencyClock
    {
        public long NowMs { get; private set; }
        public string Kind => "manual";

        public ManualLatencyClock(long startMs)
        {
            NowMs = startMs;
        }

        public void Advance(long deltaMs)
        {
            if (deltaMs < 0) throw new ArgumentOutOfRangeException(nameof(deltaMs));
            NowMs += deltaMs;
        }
    }
}
