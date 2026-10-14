using System;

namespace OmniBot.VR.Latency
{
    public static class LatencyStats
    {
        /// <summary>
        /// Nearest-rank percentile. <paramref name="sortedAscending"/> must be
        /// sorted ascending and non-empty. Rank is ceil(p * n), clamped to 1..n.
        /// </summary>
        public static long PercentileNearestRank(long[] sortedAscending, double p)
        {
            if (sortedAscending == null || sortedAscending.Length == 0)
                throw new ArgumentException("percentile requires samples", nameof(sortedAscending));
            if (p <= 0) return sortedAscending[0];
            if (p >= 1) return sortedAscending[sortedAscending.Length - 1];
            int rank = (int)Math.Ceiling(p * sortedAscending.Length);
            if (rank < 1) rank = 1;
            if (rank > sortedAscending.Length) rank = sortedAscending.Length;
            return sortedAscending[rank - 1];
        }
    }
}
