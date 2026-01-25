#region

using System;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class StrategyReflectionTests
{
    private static object GetStrategy(DatabaseContext ctx)
    {
        var f = typeof(DatabaseContext).GetField("_connectionStrategy", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        var strat = f!.GetValue(ctx);
        Assert.NotNull(strat);
        return strat!;
    }

    private static object? GetPersistent(DatabaseContext ctx)
    {
        var p = typeof(DatabaseContext).GetProperty("PersistentConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return p!.GetValue(ctx);
    }

    private static void InvokeRelease(object strategy, string method, object? conn)
    {
        var mi = strategy.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(mi);
        if (method.EndsWith("Async", StringComparison.Ordinal))
        {
            var vt = (ValueTask)mi!.Invoke(strategy, new[] { conn })!;
            vt.AsTask().GetAwaiter().GetResult();
        }
        else
        {
            mi!.Invoke(strategy, new[] { conn });
        }
    }

    private static DatabaseContext Create(DbMode mode, SupportedDatabase product = SupportedDatabase.SqlServer,
        string dataSource = "test")
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={dataSource};EmulatedProduct={product}",
            DbMode = mode,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(cfg, new fakeDbFactory(product));
    }

    [Fact]
    public void StandardStrategy_Release_Disposes()
    {
        using var ctx = Create(DbMode.Standard);
        var strat = GetStrategy(ctx);
        var c = ctx.GetConnection(ExecutionType.Write);
        c.Open();
        Assert.Equal(1, ctx.NumberOfOpenConnections);
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnection), c);
        Assert.Equal(0, ctx.NumberOfOpenConnections);
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnection), null);
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnectionAsync), null);
    }

    [Fact]
    public void SingleConnectionStrategy_Release_KeepsPersistent()
    {
        using var ctx = Create(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var strat = GetStrategy(ctx);
        var persistent = GetPersistent(ctx);
        var before = ctx.NumberOfOpenConnections;
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnection), persistent);
        Assert.Equal(before, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public void KeepAliveStrategy_Release_KeepsPersistent()
    {
        using var ctx = Create(DbMode.KeepAlive, SupportedDatabase.Sqlite, ":memory:");
        var strat = GetStrategy(ctx);
        var persistent = GetPersistent(ctx);
        var before = ctx.NumberOfOpenConnections;
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnectionAsync), persistent);
        Assert.Equal(before, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public void SingleWriterStrategy_Release_ReadDisposes_WriteKeeps()
    {
        using var ctx = Create(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var strat = GetStrategy(ctx);
        var persistent = GetPersistent(ctx);

        var read = ctx.GetConnection(ExecutionType.Read);
        read.Open();
        var peak = ctx.NumberOfOpenConnections;
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnection), read);
        Assert.Equal(peak - 1, ctx.NumberOfOpenConnections);

        var before = ctx.NumberOfOpenConnections;
        InvokeRelease(strat, nameof(IConnectionStrategy.ReleaseConnection), persistent);
        Assert.Equal(before, ctx.NumberOfOpenConnections);
    }
}