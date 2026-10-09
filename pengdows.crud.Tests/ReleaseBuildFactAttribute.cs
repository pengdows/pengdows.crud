using System.Diagnostics;
using System.Reflection;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A fact whose assertion is a property of optimized code. Allocation budgets are measured with
/// GC.GetAllocatedBytesForCurrentThread, and a Debug build of the code under test allocates its async
/// state machines on the heap (Release makes them structs), so a budget written for Release cannot
/// hold under Debug: 216 B per uncontended governor acquire against a 48 B budget, 792 B against 568 B
/// for the mapper. CI runs Release; this skips, with the reason stated, when the library under test was
/// built without optimizations instead of failing for a reason that says nothing about the code.
/// </summary>
public sealed class ReleaseBuildFactAttribute : FactAttribute
{
    public ReleaseBuildFactAttribute()
    {
        if (!IsOptimized(typeof(PoolGovernor).Assembly))
        {
            Skip = "Allocation budgets hold only for optimized code; pengdows.crud was built without " +
                   "optimizations (Debug). Run with -c Release.";
        }
    }

    internal static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
}
