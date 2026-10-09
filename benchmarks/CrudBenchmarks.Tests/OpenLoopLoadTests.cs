using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

/// <summary>
/// The earlier storms were closed-loop (a fixed set of callers each waiting for its last call), which
/// slows the offered load down whenever the system is slow and so can never overload it. Backpressure
/// only shows when arrivals keep coming at a fixed rate above capacity. These cover the harness's
/// arithmetic; the databases supply the rest.
/// </summary>
public class OpenLoopLoadTests
{
    [Theory]
    [InlineData(0.0, 100.0, 0)]
    [InlineData(1.0, 100.0, 100)]
    [InlineData(0.0149, 100.0, 1)]
    [InlineData(2.5, 40.0, 100)]
    [InlineData(-1.0, 100.0, 0)]
    public void ArrivalsDueBy_IsTheNumberOfArrivalsThatHaveHappenedByThenAtAFixedRate(
        double elapsedSeconds, double ratePerSecond, int expected)
    {
        Assert.Equal(expected, OpenLoopSchedule.ArrivalsDueBy(elapsedSeconds, ratePerSecond));
    }

    private static CallRecord Work(double start, double end, bool ok, string? error = null) =>
        new(start, end, ok, error, IsProbe: false);

    private static CallRecord Probe(double start, double end, bool ok, string? error = null) =>
        new(start, end, ok, error, IsProbe: true);

    [Fact]
    public void Summarize_SeparatesWorkFromProbes_AndFastRejectsFromSlowFailures()
    {
        var records = new[]
        {
            Work(0, 300, ok: true),
            Work(0, 400, ok: true),
            Work(0, 5000, ok: true),
            Work(0, 10, ok: false, "PoolSaturatedException"),     // turned away at once
            Work(0, 5000, ok: false, "TimeoutException"),          // waited out the whole timeout
            Probe(0, 5, ok: true),
            Probe(0, 7000, ok: true),
            Probe(0, 6000, ok: false, "TimeoutException"),
        };

        var s = BackpressureStats.Summarize(records, loadEndMs: 1000, fastRejectMs: 1000);

        Assert.Equal(5, s.Offered);
        Assert.Equal(3, s.Ok);
        Assert.Equal(1, s.FastRejects);
        Assert.Equal(1, s.SlowFailures);
        Assert.Equal(2, s.ProbeOk);
        Assert.Equal(1, s.ProbeFailed);
    }

    [Fact]
    public void Summarize_PercentilesAreOverSuccessfulWorkOnly_AndFailuresHaveTheirOwn()
    {
        var records = Enumerable.Range(1, 100).Select(i => Work(0, i * 10, ok: true))
            .Concat(new[] { Work(0, 9000, ok: false, "TimeoutException") })
            .ToArray();

        var s = BackpressureStats.Summarize(records, loadEndMs: 0, fastRejectMs: 1000);

        Assert.Equal(500, s.OkP50Ms);
        Assert.Equal(990, s.OkP99Ms);
        Assert.Equal(1000, s.OkMaxMs);
        Assert.Equal(9000, s.FailP50Ms);
    }

    [Fact]
    public void Summarize_PeakInFlight_IsTheMostCallsPendingAtOnce()
    {
        var records = new[]
        {
            Work(0, 10, ok: true),
            Work(5, 15, ok: true),
            Work(12, 20, ok: true),
        };

        Assert.Equal(2, BackpressureStats.Summarize(records, loadEndMs: 0, fastRejectMs: 1000).PeakInFlight);
    }

    [Fact]
    public void Summarize_RecoveryIsHowLongAfterTheLoadStoppedTheLastCallWasStillPending()
    {
        var records = new[] { Work(0, 500, ok: true), Work(900, 4000, ok: true) };

        Assert.Equal(3000, BackpressureStats.Summarize(records, loadEndMs: 1000, fastRejectMs: 1000).RecoveryMs);
    }

    [Fact]
    public void Summarize_RecoveryIsZero_WhenEverythingFinishedBeforeTheLoadEnded()
    {
        var records = new[] { Work(0, 500, ok: true) };

        Assert.Equal(0, BackpressureStats.Summarize(records, loadEndMs: 1000, fastRejectMs: 1000).RecoveryMs);
    }

    [Fact]
    public void Summarize_CountsFailuresByType()
    {
        var records = new[]
        {
            Work(0, 10, ok: false, "PoolSaturatedException"),
            Work(0, 10, ok: false, "PoolSaturatedException"),
            Work(0, 5000, ok: false, "TimeoutException"),
        };

        var errors = BackpressureStats.Summarize(records, loadEndMs: 0, fastRejectMs: 1000).Errors;

        Assert.Equal(2, errors["PoolSaturatedException"]);
        Assert.Equal(1, errors["TimeoutException"]);
    }

    [Fact]
    public async Task RunAsync_OffersCallsAtTheRequestedRate_RegardlessOfHowSlowTheyAre()
    {
        // 200/s for 0.5 s with calls that take longer than the whole run: a closed-loop harness would
        // stop at the first batch; an open-loop one keeps arriving.
        var records = await OpenLoopLoad.RunAsync(
            ratePerSecond: 200,
            durationSeconds: 0.5,
            call: async ct =>
            {
                await Task.Delay(1500, ct);
            },
            probePerSecond: 0,
            probe: null,
            settleSeconds: 5);

        Assert.InRange(records.Count(r => !r.IsProbe), 70, 130);
        Assert.All(records, r => Assert.True(r.Succeeded));
    }
}
