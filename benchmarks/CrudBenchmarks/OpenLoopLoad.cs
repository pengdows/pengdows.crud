using System.Collections.Concurrent;
using System.Diagnostics;

namespace CrudBenchmarks;

/// <summary>One call: when it was issued and when it finished (ms since the run started).</summary>
internal readonly record struct CallRecord(double StartMs, double EndMs, bool Succeeded, string? Error, bool IsProbe)
{
    public double LatencyMs => EndMs - StartMs;
}

internal static class OpenLoopSchedule
{
    /// <summary>How many arrivals have happened by <paramref name="elapsedSeconds"/> at a fixed rate.</summary>
    public static int ArrivalsDueBy(double elapsedSeconds, double ratePerSecond) =>
        elapsedSeconds <= 0 || ratePerSecond <= 0 ? 0 : (int)Math.Floor(elapsedSeconds * ratePerSecond);
}

internal sealed record BackpressureSummary(
    int Offered,
    int Ok,
    int FastRejects,
    int SlowFailures,
    double OkP50Ms,
    double OkP99Ms,
    double OkMaxMs,
    double FailP50Ms,
    double FailP99Ms,
    int PeakInFlight,
    double RecoveryMs,
    int ProbeOk,
    int ProbeFailed,
    double ProbeP50Ms,
    double ProbeP99Ms,
    IReadOnlyDictionary<string, int> Errors);

internal static class BackpressureStats
{
    /// <param name="loadEndMs">When arrivals stopped.</param>
    /// <param name="fastRejectMs">A failure faster than this was turned away at the door; slower was a wait that ran out.</param>
    public static BackpressureSummary Summarize(IReadOnlyList<CallRecord> records, double loadEndMs, double fastRejectMs)
    {
        var work = records.Where(r => !r.IsProbe).ToList();
        var probes = records.Where(r => r.IsProbe).ToList();

        static long[] SortedLatencies(IEnumerable<CallRecord> rs) =>
            rs.Select(r => (long)Math.Round(r.LatencyMs)).OrderBy(x => x).ToArray();

        var okLatencies = SortedLatencies(work.Where(r => r.Succeeded));
        var failures = work.Where(r => !r.Succeeded).ToList();
        var failLatencies = SortedLatencies(failures);
        var probeOkLatencies = SortedLatencies(probes.Where(r => r.Succeeded));

        var errors = failures
            .GroupBy(r => r.Error ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var recovery = work.Count == 0 ? 0 : Math.Max(0, work.Max(r => r.EndMs) - loadEndMs);

        return new BackpressureSummary(
            Offered: work.Count,
            Ok: okLatencies.Length,
            FastRejects: failures.Count(r => r.LatencyMs < fastRejectMs),
            SlowFailures: failures.Count(r => r.LatencyMs >= fastRejectMs),
            OkP50Ms: PercentileMath.NearestRank(okLatencies, 50),
            OkP99Ms: PercentileMath.NearestRank(okLatencies, 99),
            OkMaxMs: okLatencies.Length == 0 ? 0 : okLatencies[^1],
            FailP50Ms: PercentileMath.NearestRank(failLatencies, 50),
            FailP99Ms: PercentileMath.NearestRank(failLatencies, 99),
            PeakInFlight: PeakInFlight(work),
            RecoveryMs: recovery,
            ProbeOk: probeOkLatencies.Length,
            ProbeFailed: probes.Count(r => !r.Succeeded),
            ProbeP50Ms: PercentileMath.NearestRank(probeOkLatencies, 50),
            ProbeP99Ms: PercentileMath.NearestRank(probeOkLatencies, 99),
            Errors: errors);
    }

    // The most calls pending at one instant: a sweep over start (+1) and end (-1) events, ends first on
    // a tie so back-to-back calls do not count as overlapping.
    private static int PeakInFlight(IReadOnlyList<CallRecord> work)
    {
        var events = work
            .SelectMany(r => new[] { (At: r.StartMs, Delta: 1), (At: r.EndMs, Delta: -1) })
            .OrderBy(e => e.At).ThenBy(e => e.Delta);

        int current = 0, peak = 0;
        foreach (var e in events)
        {
            current += e.Delta;
            peak = Math.Max(peak, current);
        }

        return peak;
    }
}

internal static class OpenLoopLoad
{
    /// <summary>
    /// Issues <paramref name="call"/> at a fixed <paramref name="ratePerSecond"/> for
    /// <paramref name="durationSeconds"/> whether or not earlier calls have finished (open loop), and
    /// optionally a lighter <paramref name="probe"/> at <paramref name="probePerSecond"/> through the same
    /// path. Waits up to <paramref name="settleSeconds"/> after arrivals stop for everything to finish;
    /// anything still pending is cancelled and recorded as a failure.
    /// </summary>
    public static async Task<IReadOnlyList<CallRecord>> RunAsync(
        double ratePerSecond,
        double durationSeconds,
        Func<CancellationToken, Task> call,
        double probePerSecond,
        Func<CancellationToken, Task>? probe,
        double settleSeconds)
    {
        var records = new ConcurrentQueue<CallRecord>();
        var tasks = new ConcurrentBag<Task>();
        using var cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();

        void Launch(Func<CancellationToken, Task> fn, bool isProbe)
        {
            var start = clock.Elapsed.TotalMilliseconds;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await fn(cts.Token).ConfigureAwait(false);
                    records.Enqueue(new CallRecord(start, clock.Elapsed.TotalMilliseconds, true, null, isProbe));
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    records.Enqueue(new CallRecord(start, clock.Elapsed.TotalMilliseconds, false, "Unfinished", isProbe));
                }
                catch (Exception ex)
                {
                    records.Enqueue(new CallRecord(start, clock.Elapsed.TotalMilliseconds, false, ex.GetType().Name, isProbe));
                }
            }));
        }

        int launched = 0, probesLaunched = 0;
        while (clock.Elapsed.TotalSeconds < durationSeconds)
        {
            var elapsed = clock.Elapsed.TotalSeconds;
            var due = OpenLoopSchedule.ArrivalsDueBy(elapsed, ratePerSecond) - launched;
            for (var i = 0; i < due; i++)
            {
                Launch(call, isProbe: false);
            }

            launched += Math.Max(0, due);

            if (probe != null && probePerSecond > 0)
            {
                var probeDue = OpenLoopSchedule.ArrivalsDueBy(elapsed, probePerSecond) - probesLaunched;
                for (var i = 0; i < probeDue; i++)
                {
                    Launch(probe, isProbe: true);
                }

                probesLaunched += Math.Max(0, probeDue);
            }

            await Task.Delay(1).ConfigureAwait(false);
        }

        var everything = Task.WhenAll(tasks.ToArray());
        if (await Task.WhenAny(everything, Task.Delay(TimeSpan.FromSeconds(settleSeconds))).ConfigureAwait(false) != everything)
        {
            cts.Cancel();
            await Task.WhenAny(Task.WhenAll(tasks.ToArray()), Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        }

        return records.OrderBy(r => r.StartMs).ToList();
    }
}
