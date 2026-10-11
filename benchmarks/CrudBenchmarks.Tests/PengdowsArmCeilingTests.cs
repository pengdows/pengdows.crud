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

    // The like-for-like arm for StormGate: the same ceiling of 20, the same wait for a slot, and no clamp, so
    // nothing but the governor itself differs.
    [Fact]
    public void ThePostgreSqlGovernedPatientArm_HasTheCeiling_StormGatesWait_AndNoClamp()
    {
        var governedPatient = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(
            Connection, clamp: false,
            poolAcquireTimeout: PostgreSqlConnectionGovernanceBenchmarks.PatientPoolAcquireTimeout);

        AssertCeiling(governedPatient);
        Assert.False(governedPatient.ClampPoolsToServerConnectionLimit);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormGateAcquireTimeout,
            governedPatient.PoolAcquireTimeout);
    }

    // The control arm: the clamp's share, configured by hand with no clamp. 22 usable connections on a server capped
    // at 25 (3 reserved), divided between two roles, is 11 each; the unit tests of the splitter pin the same figure.
    [Fact]
    public void ThePostgreSqlGoverned11Arm_IsSizedToTheClampsShare_WithTheDefaultWaitAndNoClamp()
    {
        Assert.Equal(11, PostgreSqlConnectionGovernanceBenchmarks.ClampedShare);

        var governed11 = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(
            Connection, reads: PostgreSqlConnectionGovernanceBenchmarks.ClampedShare,
            writes: PostgreSqlConnectionGovernanceBenchmarks.ClampedShare);
        var plain = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(Connection);

        Assert.Equal(11, governed11.MaxConcurrentReads);
        Assert.Equal(11, governed11.MaxConcurrentWrites);
        Assert.False(governed11.ClampPoolsToServerConnectionLimit);
        Assert.Equal(plain.PoolAcquireTimeout, governed11.PoolAcquireTimeout);
    }

    // The read-only arm: writes forbidden (a pool of 0 promotes the context to ReadOnly), clamp on, StormGate's wait.
    [Fact]
    public void ThePostgreSqlClampedReadOnlyArm_ForbidsWrites_KeepsTheReaderCeiling_AndTheClamp()
    {
        var readOnly = PostgreSqlConnectionGovernanceBenchmarks.PengdowsConfiguration(
            Connection, clamp: true, writes: 0,
            poolAcquireTimeout: PostgreSqlConnectionGovernanceBenchmarks.PatientPoolAcquireTimeout);

        Assert.Equal(0, readOnly.MaxConcurrentWrites);
        Assert.Equal(20, readOnly.MaxConcurrentReads);
        Assert.True(readOnly.ClampPoolsToServerConnectionLimit);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormGateAcquireTimeout, readOnly.PoolAcquireTimeout);
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
