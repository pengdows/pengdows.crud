namespace CrudBenchmarks;

/// <summary>
/// Nearest-rank percentile over an in-memory sample set. Used by benchmarks that need their own
/// percentile reporting outside of BenchmarkDotNet's own per-iteration statistics (e.g. a
/// distribution collected across many logical operations within a single long-running benchmark
/// iteration, such as PeakConnectionsBenchmarks' per-operation connection hold times).
/// </summary>
internal static class PercentileMath
{
    /// <summary>
    /// Returns the nearest-rank percentile of <paramref name="sortedAscending"/>. The caller MUST
    /// pass the array already sorted in ascending order — this method does not sort, so an
    /// unsorted input silently returns a wrong-but-plausible value rather than throwing. Returns
    /// 0 for an empty array.
    /// </summary>
    public static double NearestRank(long[] sortedAscending, double percentile)
    {
        if (sortedAscending.Length == 0)
        {
            return 0.0;
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Length) - 1;
        rank = Math.Clamp(rank, 0, sortedAscending.Length - 1);
        return sortedAscending[rank];
    }
}
