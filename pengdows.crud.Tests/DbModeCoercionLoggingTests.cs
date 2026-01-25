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
    public void SqliteSharedMemory_BestMode_AutoSelectsSingleWriter_WithInfo()
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
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void DuckDbFile_BestMode_AutoSelectsSingleWriter_WithInfo()
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
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void DuckDbFile_StandardMode_CoercesToSingleWriter_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.duckdb;EmulatedProduct=DuckDB",
            ProviderName = SupportedDatabase.DuckDB.ToString(),
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.DuckDB), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqliteSharedMemory_StandardMode_CoercesToSingleWriter_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:shared?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqliteFile_BestMode_AutoSelectsSingleWriter_WithInfo()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqlServer_BestMode_AutoSelectsStandard_WithInfo()
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
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqliteFile_StandardMode_CoercesToSingleWriter_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), lf);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqlServerLocalDb_BestMode_AutoSelectsKeepAlive_WithInfo()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer), lf);
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqlServerLocalDb_StandardMode_CoercesToKeepAlive_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer), lf);
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqlServer_KeepAliveMode_CanBeForced_NoCoercion()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.KeepAlive // Explicit force for testing
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer), lf);

        // Users can force KeepAlive on full servers for testing
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);

        // No warning should be logged for explicit mode choices
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void FirebirdEmbedded_BestMode_AutoSelectsSingleConnection_WithInfo()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Database=C:/data/test.fdb;ServerType=Embedded;EmulatedProduct=Firebird",
            ProviderName = SupportedDatabase.Firebird.ToString(),
            DbMode = DbMode.Best
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Firebird), lf);
        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("DbMode auto-selection"));
        Assert.DoesNotContain(provider.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void FirebirdEmbedded_StandardMode_CoercesToSingleConnection_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Database=C:/data/test.fdb;ServerType=Embedded;EmulatedProduct=Firebird",
            ProviderName = SupportedDatabase.Firebird.ToString(),
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Firebird), lf);
        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }

    [Fact]
    public void SqliteIsolatedMemory_KeepAliveMode_CoercesToSingleConnection_WithWarning()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.KeepAlive
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), lf);
        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
        Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DbMode override"));
    }
}