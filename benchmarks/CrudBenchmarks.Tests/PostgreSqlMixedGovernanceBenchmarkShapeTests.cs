using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The mixed read/write storm is the benchmark the pool-protection argument was missing: every earlier arm only reads, so
// none of them can show the failure the clamp prevents (the reader and writer pools together refused by the server) or
// what a per-server budget buys over a static division. The arms must be configured exactly as they claim, and the
// mix must really be the ratio the parameter says.
public class PostgreSqlMixedGovernanceBenchmarkShapeTests
{
    private const string Connection = "Host=localhost;Database=d;Username=u;Password=p";

    [Fact]
    public void TheClass_IsOptIn_AndRunsAtFiftyAndNinetyPercentReads()
    {
        var type = typeof(PostgreSqlMixedGovernanceBenchmarks);

        Assert.NotNull(type.GetCustomAttribute<OptInBenchmarkAttribute>());
        var readPercent = type.GetProperty(nameof(PostgreSqlMixedGovernanceBenchmarks.ReadPercent))!
            .GetCustomAttribute<ParamsAttribute>()!;
        Assert.Equal(new object[] { 50, 90 }, readPercent.Values);
    }

    [Fact]
    public void TheStorm_RunsJustAboveTheServersLimit_AndFortyTimesOverIt()
    {
        var callers = typeof(PostgreSqlMixedGovernanceBenchmarks)
            .GetProperty(nameof(PostgreSqlMixedGovernanceBenchmarks.Callers))!
            .GetCustomAttribute<ParamsAttribute>()!;

        Assert.Equal(new object[] { 50, 1000 }, callers.Values);
    }

    // The arms the pool-protection argument compares against: Dapper and EF with nothing in front of the provider pool,
    // and the same two behind StormGate (20 permits, 30 s), on the same mixed read/write load.
    [Theory]
    [InlineData("Pengdows_Governed")]
    [InlineData("Pengdows_Governed11")]
    [InlineData("Pengdows_Clamped")]
    [InlineData("Dapper_Uncontrolled")]
    [InlineData("Dapper_StormGate")]
    [InlineData("EF_Uncontrolled")]
    [InlineData("EF_StormGate")]
    public void EveryArm_IsABenchmark(string method)
    {
        var info = typeof(PostgreSqlMixedGovernanceBenchmarks).GetMethod(method);

        Assert.NotNull(info);
        Assert.NotNull(info!.GetCustomAttribute<BenchmarkAttribute>());
    }

    [Theory]
    [InlineData(50, 750)]
    [InlineData(90, 1350)]
    public void TheMix_IsExactlyTheRequestedShare(int readPercent, int expectedReads)
    {
        var reads = Enumerable.Range(0, PostgreSqlMixedGovernanceBenchmarks.Operations)
            .Count(i => PostgreSqlMixedGovernanceBenchmarks.IsRead(i, readPercent));

        Assert.Equal(1500, PostgreSqlMixedGovernanceBenchmarks.Operations);
        Assert.Equal(expectedReads, reads);
    }

    [Fact]
    public void TheWriteStatement_HoldsItsConnectionForTheSameTimeAsTheRead()
    {
        Assert.Equal("SELECT pg_sleep(0.3)", PostgreSqlMixedGovernanceBenchmarks.WriteSql);
        Assert.Contains("pg_sleep(0.3)", PostgreSqlConnectionGovernanceBenchmarks.QuerySqlFor(300));
    }

    [Fact]
    public void Governed_IsTheDefaultCeilingOf20PerRole_WithNoClamp_AndStormGatesWait()
    {
        var governed = PostgreSqlMixedGovernanceBenchmarks.MixedConfiguration(Connection, MixedArm.Governed);

        Assert.Equal(20, governed.MaxConcurrentReads);
        Assert.Equal(20, governed.MaxConcurrentWrites);
        Assert.False(governed.ClampPoolsToServerConnectionLimit);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormGateAcquireTimeout, governed.PoolAcquireTimeout);
    }

    [Fact]
    public void Governed11_IsTheOldStaticDivisionByHand_NoClamp()
    {
        var governed11 = PostgreSqlMixedGovernanceBenchmarks.MixedConfiguration(Connection, MixedArm.Governed11);

        Assert.Equal(11, governed11.MaxConcurrentReads);
        Assert.Equal(11, governed11.MaxConcurrentWrites);
        Assert.False(governed11.ClampPoolsToServerConnectionLimit);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormGateAcquireTimeout, governed11.PoolAcquireTimeout);
    }

    [Fact]
    public void Clamped_IsTheSameSizesAsGoverned_WithOnlyTheClampTurnedOn()
    {
        var governed = PostgreSqlMixedGovernanceBenchmarks.MixedConfiguration(Connection, MixedArm.Governed);
        var clamped = PostgreSqlMixedGovernanceBenchmarks.MixedConfiguration(Connection, MixedArm.Clamped);

        Assert.True(clamped.ClampPoolsToServerConnectionLimit);
        Assert.Equal(governed.MaxConcurrentReads, clamped.MaxConcurrentReads);
        Assert.Equal(governed.MaxConcurrentWrites, clamped.MaxConcurrentWrites);
        Assert.Equal(governed.PoolAcquireTimeout, clamped.PoolAcquireTimeout);
    }
}
