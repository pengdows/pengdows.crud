using CrudBenchmarks;
using pengdows.crud.configuration;

namespace CrudBenchmarks.Tests;

// Every pengdows.crud arm of the connection-storm benchmarks runs with an explicit pool ceiling of 20 per
// role, for now. The Dapper and EF arms are deliberately left on the provider default: they are what is
// being compared against, not something to help.
public class PengdowsArmCeilingTests
{
    private const string Connection = "Host=localhost;Database=d";

    private static void AssertCeiling(DatabaseContextConfiguration configuration)
    {
        Assert.Equal(20, configuration.MaxConcurrentReads);
        Assert.Equal(20, configuration.MaxConcurrentWrites);
    }

    [Fact]
    public void ThePostgreSqlArms_AllUseAnExplicitCeilingOf20_AndKeepTheirOwnSettings()
    {
        Assert.Equal(20, PostgreSqlConnectionGovernanceBenchmarks.PengdowsCeiling);

        var plain = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(Connection);
        var clamped = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(Connection, clamp: true);
        var patient = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(
            Connection, clamp: true,
            poolAcquireTimeout: PostgreSqlConnectionGovernanceBenchmarks.PatientPoolAcquireTimeout);

        AssertCeiling(plain);
        AssertCeiling(clamped);
        AssertCeiling(patient);
        Assert.False(plain.ClampPoolsToServerConnectionLimit);
        Assert.True(clamped.ClampPoolsToServerConnectionLimit);
        Assert.True(patient.ClampPoolsToServerConnectionLimit);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.PatientPoolAcquireTimeout, patient.PoolAcquireTimeout);
        Assert.Equal("Npgsql", plain.ProviderName);
    }

    [Fact]
    public void TheSqlServerArms_AllUseAnExplicitCeilingOf20_AndKeepTheirOwnSettings()
    {
        Assert.Equal(20, SqlServerConnectionGovernanceBenchmarks.PengdowsCeiling);

        var plain = SqlServerConnectionGovernanceBenchmarks.PengdowsConfiguration(Connection);
        var clamped = SqlServerConnectionGovernanceBenchmarks.PengdowsConfiguration(Connection, clamp: true);
        var headroom = SqlServerConnectionGovernanceBenchmarks.PengdowsConfiguration(
            Connection, clamp: true, headroom: SqlServerConnectionGovernanceBenchmarks.Headroom);

        AssertCeiling(plain);
        AssertCeiling(clamped);
        AssertCeiling(headroom);
        Assert.False(plain.ClampPoolsToServerConnectionLimit);
        Assert.True(clamped.ClampPoolsToServerConnectionLimit);
        Assert.Equal(0, clamped.ResourceConnectionHeadroom);
        Assert.Equal(SqlServerConnectionGovernanceBenchmarks.Headroom, headroom.ResourceConnectionHeadroom);
        Assert.Equal("Microsoft.Data.SqlClient", plain.ProviderName);
    }
}
