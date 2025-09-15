#region

using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DbModeCoercionLoggingTests
{
    [Fact]
    public void SqliteSharedMemory_BestMode_CoercesToSingleWriter_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:shared?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void DuckDbFile_BestMode_CoercesToSingleWriter_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.duckdb;EmulatedProduct=DuckDB",
            ProviderName = SupportedDatabase.DuckDB.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.DuckDB), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqlServer_BestMode_PrefersStandard_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer), lf);
        Assert.Equal(DbMode.Standard, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }
}
