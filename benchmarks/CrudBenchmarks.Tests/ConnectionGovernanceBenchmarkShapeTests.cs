using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The connection-storm benchmark exists to compare how each library behaves when concurrency
// is 4x the server's max_connections. It once covered only Dapper and EF Core (the governed
// arms used StormGate), so pengdows.crud's own PoolGovernor was never measured under the herd.
public class ConnectionGovernanceBenchmarkShapeTests
{
    private static MethodInfo[] BenchmarkMethods() =>
        typeof(PostgreSqlConnectionGovernanceBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .ToArray();

    [Fact]
    public void StormBenchmark_HasAPengdowsArm_ThatDoesNotUseStormGate()
    {
        var arm = BenchmarkMethods()
            .SingleOrDefault(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>() is
                { Framework: "Pengdows", Scenario: "Governed" });

        Assert.NotNull(arm);
    }

    // The opt-in server-ceiling clamp is measured next to the default-configured pengdows arm, on the
    // same connection string and the same load, so the only difference between the two is the flag.
    [Fact]
    public void StormBenchmark_HasAPengdowsArmWithTheServerCeilingClampOn()
    {
        var arm = BenchmarkMethods()
            .SingleOrDefault(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>() is
                { Framework: "PengdowsClamped", Scenario: "Governed" });

        Assert.NotNull(arm);
    }

    [Fact]
    public void StormBenchmark_EveryArmDeclaresACorrectnessIdentity_SoFailuresAreAttributed()
    {
        Assert.All(BenchmarkMethods(),
            m => Assert.NotNull(m.GetCustomAttribute<CorrectnessIdentityAttribute>()));
    }

    // No client gets an explicit pool ceiling: Dapper, EF Core and pengdows.crud all run on the
    // provider default (Npgsql 100), against a server capped at 25. Capping every pool at 20 on
    // 2026-10-08 made every arm pass (0 failures), which is a configuration the storm is meant to
    // test the absence of. StormGate still carries its own 20 permits.
    [Fact]
    public void ClientConnectionString_SetsNoExplicitPoolCeiling()
    {
        var cs = PostgreSqlConnectionGovernanceBenchmarks.BuildClientConnectionString(
            "Host=localhost;Port=5432;Database=gov_test;Username=postgres;Password=x");

        // A plain builder holds only the keys actually present (the provider builders report every
        // known key), so this fails if any pool-size key was set.
        var keys = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = cs }.Keys
            .Cast<string>();
        Assert.DoesNotContain(keys, k => k.Contains("pool size", StringComparison.OrdinalIgnoreCase));
    }

    // A storm, not a busy afternoon: at 100-way (4x the server's 25) a pool capped below the server
    // limit just queues and nobody fails (measured 2026-10-08, 0 / 5,200 on every arm). The load must
    // be large enough that callers outnumber the pool by orders of magnitude.
    [Fact]
    public void StormLoad_IsAtLeastFortyTimesTheServersConnectionLimit()
    {
        Assert.True(
            PostgreSqlConnectionGovernanceBenchmarks.StormParallelism
            >= 40 * PostgreSqlConnectionGovernanceBenchmarks.ServerMaxConnections);
        Assert.True(
            PostgreSqlConnectionGovernanceBenchmarks.StormOperationsPerRun
            >= 2 * PostgreSqlConnectionGovernanceBenchmarks.StormParallelism);
    }

    // The storms so far ran queries that return in under a millisecond, so even 1,000 callers drain
    // through a pool of 20 in tens of milliseconds, far inside any pool timeout. Real work holds the
    // connection: a spike of slow calls is where queue wait outruns the timeouts. WorkMilliseconds is a
    // parameter so the instant-query rows stay comparable with every earlier run.
    [Fact]
    public void WorkMilliseconds_IsAParam_CoveringInstantAndSlowCalls()
    {
        var prop = typeof(PostgreSqlConnectionGovernanceBenchmarks).GetProperty("WorkMilliseconds");
        Assert.NotNull(prop);

        var values = prop!.GetCustomAttribute<ParamsAttribute>()?.Values;
        Assert.NotNull(values);
        Assert.Contains(0, values!);
        Assert.Contains(values!, v => v is int ms && ms > 0);
    }

    [Fact]
    public void ParameterKey_MatchesTheKeyTheCorrectnessColumnDerivesFromBdnsDisplayInfo()
    {
        Assert.Equal("WorkMilliseconds=300", PostgreSqlConnectionGovernanceBenchmarks.ParameterKeyFor(300));
    }

    [Fact]
    public void SlowCallStorm_QueueWaitOutrunsNpgsqlsDefaultFifteenSecondPoolTimeout()
    {
        // Callers all arrive at once and drain through ~20 concurrent connections; with the batch big
        // enough that the last waits longer than the provider's default pool timeout, a pool that
        // merely queues fails the tail, and an admission gate with a long enough wait does not.
        const int npgsqlDefaultPoolTimeoutMs = 15_000;
        const int concurrentConnections = 20;
        var work = PostgreSqlConnectionGovernanceBenchmarks.SlowWorkMilliseconds;
        var ops = PostgreSqlConnectionGovernanceBenchmarks.OperationsFor(work);

        Assert.True(work > 0);
        Assert.True((long)ops * work / concurrentConnections > npgsqlDefaultPoolTimeoutMs,
            $"{ops} ops x {work} ms over {concurrentConnections} connections drains in " +
            $"{(long)ops * work / concurrentConnections} ms, inside the {npgsqlDefaultPoolTimeoutMs} ms timeout");
    }

    [Fact]
    public void InstantCalls_KeepTheOriginalBatchSize()
    {
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormOperationsPerRun,
            PostgreSqlConnectionGovernanceBenchmarks.OperationsFor(0));
    }

    // StormGate in this benchmark waits up to 30 s for a permit; pengdows.crud's PoolAcquireTimeout
    // defaults to 5 s, so in a storm whose tail waits ~22 s the default fast-fails by design. To compare
    // like with like there is also a clamped arm whose acquire timeout matches StormGate's.
    [Fact]
    public void StormBenchmark_HasAClampedPengdowsArmWithStormGatesAcquireTimeout()
    {
        var arm = BenchmarkMethods()
            .SingleOrDefault(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>() is
                { Framework: "PengdowsClampedPatient", Scenario: "Governed" });

        Assert.NotNull(arm);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormGateAcquireTimeout,
            PostgreSqlConnectionGovernanceBenchmarks.PatientPoolAcquireTimeout);
    }
}
