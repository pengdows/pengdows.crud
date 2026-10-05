using System;
using System.Threading.Tasks;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-043: one pass of GC.GetAllocatedBytesForCurrentThread picks up any stray allocation that
/// lands on the thread (and, across an await, can resume on another thread), so allocation
/// comparisons flaked by a few bytes per row under a loaded full-suite run. The lowest of several
/// passes is the code's own cost; noise only ever adds — except a resume on another thread, which
/// reads another thread's counter and can come out low, so <see cref="PerRunAsync"/> discards it.
/// </summary>
internal static class AllocationMeasurement
{
    private const int Passes = 5;

    public static long Lowest(Func<long> measure)
    {
        var lowest = long.MaxValue;
        for (var i = 0; i < Passes; i++)
        {
            lowest = Math.Min(lowest, measure());
        }

        return lowest;
    }

    /// <summary>
    /// The bytes one run of <paramref name="operation"/> allocates on this thread, over
    /// <paramref name="runs"/> runs after a warm-up; <see cref="long.MaxValue"/> (which
    /// <see cref="LowestAsync"/> passes over) when any run resumed on another thread.
    /// </summary>
    public static async Task<long> PerRunAsync(Func<Task> operation, int runs = 20)
    {
        await operation(); // warm caches
        var thread = Environment.CurrentManagedThreadId;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < runs; i++)
        {
            await operation();
            if (Environment.CurrentManagedThreadId != thread)
            {
                return long.MaxValue;
            }
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
    }

    public static async Task<long> LowestAsync(Func<Task<long>> measure)
    {
        var lowest = long.MaxValue;
        for (var i = 0; i < Passes; i++)
        {
            lowest = Math.Min(lowest, await measure());
        }

        return lowest;
    }
}
