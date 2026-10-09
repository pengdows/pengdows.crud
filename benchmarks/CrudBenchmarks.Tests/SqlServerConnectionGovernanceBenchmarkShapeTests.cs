using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;
using Microsoft.Data.SqlClient;

namespace CrudBenchmarks.Tests;

// SQL Server counterpart of the PostgreSQL connection-storm benchmark: same five arms, same load,
// every client on the same pool ceiling. Written to see whether the Postgres result (nobody fails
// once the pool is capped below the server limit) is specific to Postgres.
public class SqlServerConnectionGovernanceBenchmarkShapeTests
{
    private static MethodInfo[] BenchmarkMethods() =>
        typeof(SqlServerConnectionGovernanceBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .ToArray();

    [Fact]
    public void HasTheSameArmsAsThePostgresStorm_PlusTheClampArms()
    {
        var ids = BenchmarkMethods()
            .Select(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>())
            .Where(a => a != null)
            .Select(a => (a!.Framework, a.Scenario))
            .ToHashSet();

        Assert.Equal(7, BenchmarkMethods().Length);
        Assert.Contains(("Pengdows", "Governed"), ids);
        Assert.Contains(("Dapper", "Uncontrolled"), ids);
        Assert.Contains(("EntityFramework", "Uncontrolled"), ids);
        Assert.Contains(("StormGate", "Governed"), ids);
        Assert.Contains(("StormGate", "GovernedEf"), ids);
    }

    // SQL Server's default is 32,767 user connections, far above any pool, so nothing can fail and a
    // clamp has nothing to read ("user connections" = 0 means unlimited). The storm only means
    // something against a server with a real cap, set the way SQL Server requires: sp_configure plus
    // a restart. The cap must sit below the default pool (100), as Postgres's 25 does.
    [Fact]
    public void ServerIsCappedBelowTheDefaultPool()
    {
        Assert.InRange(SqlServerConnectionGovernanceBenchmarks.ServerMaxConnections, 1, 99);
    }

    [Fact]
    public void HasAPengdowsArmWithTheServerCeilingClampOn()
    {
        var ids = BenchmarkMethods()
            .Select(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>())
            .Where(a => a != null)
            .Select(a => (a!.Framework, a.Scenario))
            .ToHashSet();

        Assert.Contains(("PengdowsClamped", "Governed"), ids);
    }

    // SQL Server has no admin reserve to leave slots free, so a clamp sized to exactly the server's
    // limit has no room for any connection the context itself keeps in the other role's pool.
    // ResourceConnectionHeadroom is the knob; this arm measures it.
    [Fact]
    public void HasAClampedArmWithHeadroom()
    {
        var ids = BenchmarkMethods()
            .Select(m => m.GetCustomAttribute<CorrectnessIdentityAttribute>())
            .Where(a => a != null)
            .Select(a => (a!.Framework, a.Scenario))
            .ToHashSet();

        Assert.Contains(("PengdowsClampedHeadroom", "Governed"), ids);
    }

    [Fact]
    public void ClientConnectionString_SetsNoExplicitPoolCeiling()
    {
        var cs = SqlServerConnectionGovernanceBenchmarks.BuildClientConnectionString(
            "Server=localhost,1433;Database=gov_test;User Id=sa;Password=x;TrustServerCertificate=True;");

        // A plain builder holds only the keys actually present (the provider builders report every
        // known key), so this fails if any pool-size key was set.
        var keys = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = cs }.Keys
            .Cast<string>();
        Assert.DoesNotContain(keys, k => k.Contains("pool size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StormLoad_MatchesThePostgresStorm()
    {
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormParallelism,
            SqlServerConnectionGovernanceBenchmarks.StormParallelism);
        Assert.Equal(PostgreSqlConnectionGovernanceBenchmarks.StormOperationsPerRun,
            SqlServerConnectionGovernanceBenchmarks.StormOperationsPerRun);
    }
}
