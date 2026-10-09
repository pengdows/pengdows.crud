using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The SQLite write-storm benchmark's headline depends on busy_timeout: at 10 ms Dapper/EF lose most
// transactions, at 5,000 ms they mostly complete but pay ~4x latency (2026-08-27 paired run, done by
// hand-editing a const). It must be a real parameter so both rows come from one run, and pengdows
// must be measured at its defaults too, not only with the benchmark's own queue/timeout overrides.
public class SQLiteWriteContentionBenchmarkShapeTests
{
    [Fact]
    public void BusyTimeoutMs_IsAParam_CoveringTheStressAndTheSaneSetting()
    {
        var prop = typeof(SQLiteWriteContentionBenchmarks).GetProperty("BusyTimeoutMs");
        Assert.NotNull(prop);

        var values = prop!.GetCustomAttribute<ParamsAttribute>()?.Values;
        Assert.NotNull(values);
        Assert.Contains(10, values!);
        Assert.Contains(5000, values!);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(1500, 2)]
    [InlineData(5000, 6)]
    public void CommandTimeout_ScalesWithBusyTimeout_SoTheDriversOwnRetryLoopGetsTheSamePatience(
        int busyTimeoutMs, int expectedSeconds)
    {
        // Microsoft.Data.Sqlite's busy retry loop is bounded by CommandTimeout, not the PRAGMA.
        Assert.Equal(expectedSeconds, SQLiteWriteContentionBenchmarks.CommandTimeoutSecondsFor(busyTimeoutMs));
    }

    [Fact]
    public void ParameterKey_MatchesTheKeyTheCorrectnessColumnDerivesFromBdnsDisplayInfo()
    {
        // CorrectnessColumn reads the text between '[' and ']' in the case's display info.
        Assert.Equal("BusyTimeoutMs=5000", SQLiteWriteContentionBenchmarks.ParameterKeyFor(5000));
    }

    [Fact]
    public void HasAPengdowsArmAtDefaultConfiguration_RecordedUnderItsOwnIdentity()
    {
        var arm = typeof(SQLiteWriteContentionBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .SingleOrDefault(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>() is
                { Framework: "PengdowsDefaults", Scenario: "WriteStorm" });

        Assert.NotNull(arm);
    }
}
