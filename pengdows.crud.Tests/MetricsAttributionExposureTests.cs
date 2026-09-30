using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// BP-303 (backported 2026-09-30): DatabaseMetrics exposes the contention attribution the context
/// already collected internally — requests admitted per role, pool waits and timeouts, mode-lock
/// waits and timeouts — as init properties (additive; the positional shape is unchanged).
/// </summary>
public sealed class MetricsAttributionExposureTests
{
    // SingleConnection runs without a pool governor; its requests count too.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, "Data Source=metrics;EmulatedProduct=PostgreSql", DbMode.Standard)]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=:memory:;EmulatedProduct=Sqlite", DbMode.SingleConnection)]
    public async Task Metrics_ReportRequestsAdmittedPerRole(SupportedDatabase database, string connectionString, DbMode mode)
    {
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = mode,
            EnableMetrics = true
        }, new fakeDbFactory(database));
        Assert.Equal(mode, context.ConnectionMode);
        var before = context.Metrics;

        await using (var read = context.CreateSqlContainer("SELECT 1"))
        {
            await read.ExecuteScalarOrNullAsync<int>(ExecutionType.Read);
        }

        await using (var write = context.CreateSqlContainer("UPDATE t SET a = 1"))
        {
            await write.ExecuteNonQueryAsync();
        }

        var after = context.Metrics;
        Assert.True(after.ReadRequests > before.ReadRequests);
        Assert.True(after.WriteRequests > before.WriteRequests);
        Assert.Equal(0, after.ReadPoolTimeouts);
        Assert.Equal(0, after.WritePoolTimeouts);
        Assert.Equal(0, after.ModeTimeouts);
    }
}
