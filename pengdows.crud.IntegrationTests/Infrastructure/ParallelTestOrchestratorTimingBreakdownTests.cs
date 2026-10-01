using testbed;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Locks down <see cref="ParallelTestOrchestrator.BuildTimingBreakdown"/> — the pure data-shaping
/// step behind the "TIMING BREAKDOWN" section <see cref="ParallelTestOrchestrator"/> prints after
/// a run. It exists to separate spinup (container startup) from exercise (the test provider's own
/// run) and whatever's left over (setup overhead: context creation, test-provider construction),
/// sorted by spinup descending — the same signal <see cref="TestConfiguration.StartupWeightSeconds"/>
/// currently has to guess at by hand — so a live run's real numbers can be compared directly
/// against the configured dispatch-order weight instead of re-deriving them from raw console logs.
/// </summary>
public sealed class ParallelTestOrchestratorTimingBreakdownTests
{
    private static TestResult Result(
        string name,
        double spinupSeconds,
        double? exerciseSeconds,
        double totalSeconds,
        int configuredWeightSeconds = 0,
        bool success = true)
    {
        return new TestResult
        {
            ContainerName = name,
            DatabaseProvider = name,
            StartTime = DateTime.UtcNow,
            ContainerStartTime = TimeSpan.FromSeconds(spinupSeconds),
            TestTime = exerciseSeconds.HasValue ? TimeSpan.FromSeconds(exerciseSeconds.Value) : null,
            TotalTime = TimeSpan.FromSeconds(totalSeconds),
            ConfiguredStartupWeightSeconds = configuredWeightSeconds,
            Success = success
        };
    }

    [Fact]
    public void BuildTimingBreakdown_SortsBySpinupDescending()
    {
        var results = new[]
        {
            Result("Fast", spinupSeconds: 1, exerciseSeconds: 0.5, totalSeconds: 1.6),
            Result("Heaviest", spinupSeconds: 60, exerciseSeconds: 2, totalSeconds: 62.5),
            Result("Medium", spinupSeconds: 20, exerciseSeconds: 1, totalSeconds: 21.3)
        };

        var breakdown = ParallelTestOrchestrator.BuildTimingBreakdown(results);

        Assert.Equal(new[] { "Heaviest", "Medium", "Fast" }, breakdown.Select(r => r.ContainerName));
    }

    [Fact]
    public void BuildTimingBreakdown_SeparatesSpinupExerciseAndSetupOverhead()
    {
        // Total (10s) minus spinup (6s) minus exercise (3s) leaves 1s unaccounted for — the gap
        // between the container-start stopwatch stopping and the test-run stopwatch starting
        // (GetDatabaseContextAsync + TestProviderFactory), which no existing stopwatch captures.
        var results = new[] { Result("Db", spinupSeconds: 6, exerciseSeconds: 3, totalSeconds: 10) };

        var row = Assert.Single(ParallelTestOrchestrator.BuildTimingBreakdown(results));

        Assert.Equal(6, row.SpinupSeconds);
        Assert.Equal(3, row.ExerciseSeconds);
        Assert.Equal(10, row.TotalSeconds);
        Assert.Equal(1, row.SetupOverheadSeconds, precision: 6);
    }

    [Fact]
    public void BuildTimingBreakdown_MissingExerciseTime_ReportsNullExerciseAndZeroFloorsOverhead()
    {
        // A container-start timeout means TestTime is never set (RunTestAsync never reaches the
        // test-run stopwatch). SetupOverheadSeconds has nothing meaningful to compute against a
        // null exercise time, so it's floored at 0 rather than reporting a misleading
        // total-minus-spinup value that would actually just be "time spent failing to connect".
        var results = new[]
        {
            Result("TimedOut", spinupSeconds: 30, exerciseSeconds: null, totalSeconds: 45, success: false)
        };

        var row = Assert.Single(ParallelTestOrchestrator.BuildTimingBreakdown(results));

        Assert.Null(row.ExerciseSeconds);
        Assert.Equal(0, row.SetupOverheadSeconds);
    }

    [Fact]
    public void BuildTimingBreakdown_IncludesConfiguredWeightForComparisonAgainstMeasuredSpinup()
    {
        // The whole point of measuring this: compare the hand-guessed StartupWeightSeconds
        // dispatch-order hint against what a live run actually observed, so the guess can be
        // corrected. Db2's configured weight (70, per GetTestConfigurations' comment) badly
        // undershoots a measured 95s spinup in this example.
        var results = new[]
        {
            Result("Db2 [11.5.8.0]", spinupSeconds: 95, exerciseSeconds: 4, totalSeconds: 99, configuredWeightSeconds: 70)
        };

        var row = Assert.Single(ParallelTestOrchestrator.BuildTimingBreakdown(results));

        Assert.Equal(70, row.ConfiguredWeightSeconds);
        Assert.Equal(95, row.SpinupSeconds);
        Assert.True(row.SpinupSeconds > row.ConfiguredWeightSeconds);
    }
}
