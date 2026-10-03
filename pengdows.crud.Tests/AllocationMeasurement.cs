using System;
using System.Threading.Tasks;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-043: one pass of GC.GetAllocatedBytesForCurrentThread picks up any stray allocation that
/// lands on the thread (and, across an await, can resume on another thread), so allocation
/// comparisons flaked by a few bytes per row under a loaded full-suite run. The lowest of several
/// passes is the code's own cost; noise only ever adds.
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
