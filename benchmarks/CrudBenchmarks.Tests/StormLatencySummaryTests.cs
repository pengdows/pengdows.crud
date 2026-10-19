using System.Diagnostics;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// Storm sidecars report tail latency per arm, with successes and failures apart: a governed arm's fast typed refusal
// and an uncontrolled arm's server refusal are both failures, and mixing them into the success latency would hide both.
public class StormLatencySummaryTests
{
    private static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

    [Fact]
    public void Successes_AndFailures_AreReportedSeparately_WithP50P95P99()
    {
        var successes = Enumerable.Range(1, 100).Select(i => Ms(i)).ToArray();
        var failures = new[] { Ms(5000), Ms(5001) };

        var text = StormLatencySummary.Format(successes, failures);

        Assert.Contains("| Successes | 100 | 50.000 ms | 95.000 ms | 99.000 ms | 100.000 ms |", text);
        Assert.Contains("| Failures | 2 | 5000.000 ms | 5001.000 ms | 5001.000 ms | 5001.000 ms |", text);
    }

    [Fact]
    public void NoFailures_StillPrintsTheRow_WithZeroCount()
    {
        var text = StormLatencySummary.Format(new[] { Ms(1) }, Array.Empty<long>());

        Assert.Contains("| Failures | 0 | - | - | - | - |", text);
    }

    [Fact]
    public void UnsortedInput_IsSortedBeforeThePercentilesAreTaken()
    {
        var text = StormLatencySummary.Format(new[] { Ms(3), Ms(1), Ms(2) }, Array.Empty<long>());

        Assert.Contains("| Successes | 3 | 2.000 ms | 3.000 ms | 3.000 ms | 3.000 ms |", text);
    }
}
