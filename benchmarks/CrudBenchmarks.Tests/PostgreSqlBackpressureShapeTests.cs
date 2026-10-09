using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

/// <summary>
/// Pins the design of the overload test so a later edit cannot quietly tilt it: every arm gets the same
/// capacity and the same wait budget, the offered load really is above capacity, and the server's own
/// limit sits above what any arm may open (so any difference is the admission control, not the server).
/// </summary>
public class PostgreSqlBackpressureShapeTests
{
    private static string[] ArmNames() =>
        typeof(PostgreSqlBackpressureBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

    [Fact]
    public void OfferedLoad_IsAtLeastTwiceWhatTheConcurrencyCanServe()
    {
        var capacity = PostgreSqlBackpressureBenchmarks.CapacityPerSecond;

        Assert.Equal(PostgreSqlBackpressureBenchmarks.Concurrency * 1000.0 / PostgreSqlBackpressureBenchmarks.ServiceMilliseconds, capacity, 6);
        Assert.True(PostgreSqlBackpressureBenchmarks.OfferedRatePerSecond >= 2 * capacity - 1e-9,
            $"offered {PostgreSqlBackpressureBenchmarks.OfferedRatePerSecond}/s vs capacity {capacity}/s");
    }

    [Fact]
    public void TheServerAllowsMoreConnectionsThanAnyArmMayOpen()
    {
        Assert.True(PostgreSqlBackpressureBenchmarks.ServerMaxConnections > PostgreSqlBackpressureBenchmarks.Concurrency);
    }

    [Fact]
    public void EveryArmWithATimeout_GetsTheSameWaitBudget()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), PostgreSqlBackpressureBenchmarks.AcquireTimeout);
        // The pool-only "tuned" arm sets Npgsql's Timeout (seconds) to the same budget.
        Assert.Equal(PostgreSqlBackpressureBenchmarks.AcquireTimeout.TotalSeconds,
            PostgreSqlBackpressureBenchmarks.TunedPoolTimeoutSeconds);
    }

    [Fact]
    public void TheBoundedQueueIsDeeperThanTheConcurrency_ButShallow()
    {
        Assert.InRange(PostgreSqlBackpressureBenchmarks.BoundedQueueDepth,
            PostgreSqlBackpressureBenchmarks.Concurrency + 1,
            4 * PostgreSqlBackpressureBenchmarks.Concurrency);
    }

    [Fact]
    public void TheArmsAreThePoolAloneAtItsDefaultAndTuned_StormGate_AndTheGovernorUnboundedAndBounded()
    {
        Assert.Equal(
            new[]
            {
                "Pengdows_Governor",
                "Pengdows_Governor_BoundedQueue",
                "PoolOnly_Dapper",
                "PoolOnly_Dapper_TunedTimeout",
                "StormGate_Dapper"
            },
            ArmNames());
    }
}
