using System.Diagnostics;
using System.Reflection;
using pengdows.crud.infrastructure;

namespace pengdows.crud.Tests;

/// <summary>
/// Allocation budgets are measured with GC.GetAllocatedBytesForCurrentThread. A Debug build of the code
/// under test allocates its async state machines on the heap (Release makes them structs), so a budget
/// written for Release cannot hold under Debug: 216 B per uncontended governor acquire against a 48 B
/// budget, 792 B against 568 B for the mapper. Tests still run in both builds; Debug gets a stated
/// allowance on top of the Release budget, so a real regression fails in either.
/// </summary>
internal static class OptimizedBuild
{
    internal static bool IsOptimized { get; } =
        typeof(PoolGovernor).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    internal static long Budget(long releaseBytes, long debugExtraBytes) =>
        IsOptimized ? releaseBytes : releaseBytes + debugExtraBytes;
}
