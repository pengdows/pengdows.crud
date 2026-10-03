using testbed;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// REV-048: a check recorded with CheckFail never failed the run (Success was set as soon as
/// RunTest returned, and the JSON derived checksFailed from Success). REV-063: any startup timeout
/// was "unavailable" and the run still exited 0, though an always-on database that didn't run is a
/// failure and an opt-in one only runs when it was asked for.
/// </summary>
public sealed class ParallelTestOrchestratorOutcomeTests
{
    private static TestResult NewResult() => new()
    {
        ContainerName = "db",
        DatabaseProvider = "db",
        StartTime = DateTime.UtcNow
    };

    private static CheckResult Check(string outcome) => new() { Name = "c", Outcome = outcome };

    [Fact]
    public void ApplyChecks_AnyFailedCheck_FailsTheDatabase()
    {
        var result = NewResult();

        ParallelTestOrchestrator.ApplyChecks(result, new[] { Check("passed"), Check("failed"), Check("skipped") });

        Assert.False(result.Success);
        Assert.Equal(1, result.ChecksPassed);
        Assert.Equal(1, result.ChecksFailed);
        Assert.Equal(1, result.ChecksSkipped);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ApplyChecks_NoFailedCheck_PassesTheDatabase()
    {
        var result = NewResult();

        ParallelTestOrchestrator.ApplyChecks(result, new[] { Check("passed"), Check("skipped") });

        Assert.True(result.Success);
        Assert.Equal(0, result.ChecksFailed);
    }

    [Fact]
    public void ExitCode_AStartupTimeout_IsAFailure()
    {
        var passed = NewResult();
        passed.Success = true;
        var timedOut = NewResult();
        timedOut.ContainerStartTimeout = true;

        Assert.Equal(0, ParallelTestOrchestrator.ExitCode(new[] { passed }));
        Assert.Equal(1, ParallelTestOrchestrator.ExitCode(new[] { passed, timedOut }));
    }
}
