namespace OmniBot.VR.Telemetry
{
    /// <summary>
    /// Arrival rate over a 2 second window. The stamp buffer is fixed so
    /// <see cref="Mark"/> does not allocate.
    /// </summary>
    public sealed class RateTracker
    {
        readonly double[] _stamps = new double[24];
        int _count;
        int _head;

        public void Mark(double now)
        {
            _stamps[_head] = now;
            _head++;
            if (_head >= _stamps.Length) _head = 0;
            if (_count < _stamps.Length) _count++;
        }

        public double Hz(double now)
        {
            int n = 0;
            double min = 0;
            double max = 0;
            for (int i = 0; i < _count; i++)
            {
                double t = _stamps[i];
                if (t > now || now - t > 2.0) continue;
                if (n == 0) min = max = t;
                else
                {
                    if (t < min) min = t;
                    if (t > max) max = t;
                }
                n++;
            }
            if (n < 2) return 0;
            double span = max - min;
            if (span <= 1e-9) return 0;
            return (n - 1) / span;
        }
    }
}
