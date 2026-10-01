using System.Linq;
using CrudBenchmarks;
using Xunit;

namespace CrudBenchmarks.Tests;

// Backs PeakConnectionsBenchmarks' client-side connection hold-time percentiles (P50/P95/P99),
// added in response to an independent review's finding that the benchmark's server-side
// `pg_stat_activity` sampler undercounts connections that are leased but currently idle on the
// wire (e.g. during app-side hydration) — see PeakConnectionsBenchmarks.cs's class doc comment.
public sealed class PercentileMathTests
{
    [Fact]
    public void NearestRank_EmptyArray_ReturnsZero()
    {
        Assert.Equal(0.0, PercentileMath.NearestRank(Array.Empty<long>(), 50));
    }

    [Fact]
    public void NearestRank_SingleValue_ReturnsThatValueForAnyPercentile()
    {
        var values = new long[] { 42 };
        Assert.Equal(42.0, PercentileMath.NearestRank(values, 1));
        Assert.Equal(42.0, PercentileMath.NearestRank(values, 50));
        Assert.Equal(42.0, PercentileMath.NearestRank(values, 99));
    }

    [Fact]
    public void NearestRank_P50_ReturnsMedianOfTenSortedValues()
    {
        var values = new long[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        Assert.Equal(50.0, PercentileMath.NearestRank(values, 50));
    }

    [Fact]
    public void NearestRank_P95_ReturnsNinteenthOfTwentySortedValues()
    {
        var values = Enumerable.Range(1, 20).Select(i => (long)(i * 10)).ToArray(); // 10..200
        // ceil(0.95 * 20) - 1 = 19 - 1 = index 18 (19th value, 1-based) = 190
        Assert.Equal(190.0, PercentileMath.NearestRank(values, 95));
    }

    [Fact]
    public void NearestRank_P99_ReturnsLastOfTenSortedValues()
    {
        var values = new long[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        Assert.Equal(100.0, PercentileMath.NearestRank(values, 99));
    }

    [Fact]
    public void NearestRank_InputIsUnordered_ThrowsOrIsCallerResponsibilityDocumented()
    {
        // NearestRank assumes ascending-sorted input (documented on the method) — this test
        // locks down that an unsorted array silently produces a wrong-but-plausible answer
        // rather than throwing, so callers are on notice to sort before calling.
        var unsorted = new long[] { 100, 10, 50 };
        var result = PercentileMath.NearestRank(unsorted, 50);
        Assert.Equal(10.0, result); // index 1 of the unsorted array, NOT the true median (50)
    }
}
