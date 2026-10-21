namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// Drops events that arrive faster than <c>maxHz</c>. Times are seconds on
    /// the same clock the caller uses. The first sample is always accepted.
    /// A sample at exactly one period later is accepted, so the long-run rate
    /// stays at or under <c>maxHz</c>.
    /// </summary>
    public sealed class RateGate
    {
        readonly double _minInterval;
        double _last = double.NegativeInfinity;

        public RateGate(double maxHz)
        {
            _minInterval = maxHz <= 0.0 ? 0.0 : 1.0 / maxHz;
        }

        public bool TryEnter(double nowSeconds)
        {
            if (nowSeconds - _last < _minInterval) return false;
            _last = nowSeconds;
            return true;
        }

        public void Reset()
        {
            _last = double.NegativeInfinity;
        }
    }
}
