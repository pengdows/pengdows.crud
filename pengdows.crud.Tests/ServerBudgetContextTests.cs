using System;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// POOL-003 phase 1, at the context. With ClampPoolsToServerConnectionLimit on, each role keeps the size it asked for
/// (trimmed on its own to what the server can ever give) instead of being divided with the other role, and every
/// context that points at one server shares one budget for it. The budget belongs to the server endpoint, not to a
/// context, so contexts on different databases of one server count against the same limit.
/// </summary>
public sealed class ServerBudgetContextTests
{
    private static string UniqueHost() => "h" + Guid.NewGuid().ToString("N");

    private static string EndpointKey(string host)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = $"Host={host};Database=d;Username=u;Password=p"
        };
        Assert.True(ServerEndpoint.TryGetKey(builder, 5432, out var key));
        return key;
    }

    private static DatabaseContext Create(string host, string database = "d",
        Action<DatabaseContextConfiguration>? configure = null, string maxConnections = "25")
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        connection.ScalarResultsByCommand["SHOW max_connections"] = maxConnections;
        connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Host={host};Database={database};Username=u;Password=p",
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        };
        configure?.Invoke(config);
        return new DatabaseContext(config, factory);
    }

    private static (int Reader, int Writer) Slots(DatabaseContext context) =>
        (context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots,
            context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);

    [Fact]
    public void EachRoleKeepsItsConfiguredSize_WhenTheRolesTogetherExceedTheServer()
    {
        using var context = Create(UniqueHost(), configure: c =>
        {
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
        });

        // 20 + 20 > 22 usable, but the budget is shared at runtime, not divided in advance: the old 11 and 11 is gone.
        Assert.Equal((20, 20), Slots(context));
    }

    [Fact]
    public void WhenNothingIsRequested_EachRoleIsTrimmedToTheServersLimitOnItsOwn()
    {
        using var context = Create(UniqueHost());

        // The provider default of 100 per role against 22 usable: each is held to 22, not split to 11.
        Assert.Equal((22, 22), Slots(context));
    }

    [Fact]
    public void ARoleAskingForMoreThanTheServerAllows_IsTrimmedToTheServersLimit()
    {
        using var context = Create(UniqueHost(), configure: c =>
        {
            c.MaxConcurrentReads = 50;
            c.MaxConcurrentWrites = 4;
        });

        Assert.Equal((22, 4), Slots(context));
    }

    [Fact]
    public void TwoContextsOnOneServer_ShareOneBudget_EvenOnDifferentDatabases()
    {
        var host = UniqueHost();
        using var first = Create(host, database: "d1", configure: c => { c.MaxConcurrentReads = 20; c.MaxConcurrentWrites = 20; });
        using var second = Create(host, database: "d2", configure: c => { c.MaxConcurrentReads = 20; c.MaxConcurrentWrites = 20; });

        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(host), out var budget));
        Assert.Equal(22, budget.Limit);
        Assert.True(budget.Engaged);
        Assert.Equal(4, budget.Registrants); // a reader and a writer governor from each context
    }

    [Fact]
    public void ContextsOnDifferentServers_DoNotShareABudget()
    {
        var hostA = UniqueHost();
        var hostB = UniqueHost();
        using var a = Create(hostA, maxConnections: "25");
        using var b = Create(hostB, maxConnections: "60");

        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(hostA), out var budgetA));
        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(hostB), out var budgetB));
        Assert.NotSame(budgetA, budgetB);
        Assert.Equal(22, budgetA.Limit);
        Assert.Equal(57, budgetB.Limit);
    }

    [Fact]
    public void WhenTheConfiguredSizesFitTheServer_TheBudgetIsNeverEngaged()
    {
        var host = UniqueHost();
        using var context = Create(host, configure: c =>
        {
            c.MaxConcurrentReads = 5;
            c.MaxConcurrentWrites = 5;
        });

        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(host), out var budget));
        Assert.False(budget.Engaged);
    }

    [Fact]
    public void DisposingTheContexts_RemovesTheirBudget()
    {
        var host = UniqueHost();
        var first = Create(host);
        var second = Create(host);
        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(host), out _));

        first.Dispose();
        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(host), out var budget));
        Assert.Equal(2, budget.Registrants); // the second context's two governors

        second.Dispose();
        Assert.False(ServerBudgetRegistry.TryGet(EndpointKey(host), out _));
    }

    [Fact]
    public void AContextWithTheClampOff_RegistersNothing()
    {
        var host = UniqueHost();
        using var context = Create(host, configure: c => c.ClampPoolsToServerConnectionLimit = false);

        Assert.False(ServerBudgetRegistry.TryGet(EndpointKey(host), out _));
    }

    [Fact]
    public void AForbiddenWriterPool_RegistersNoGovernor_AndTheReaderKeepsItsFullRequest()
    {
        var host = UniqueHost();
        using var context = Create(host, configure: c =>
        {
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 0;
        });

        Assert.Equal((20, 0), Slots(context));
        Assert.True(ServerBudgetRegistry.TryGet(EndpointKey(host), out var budget));
        Assert.Equal(1, budget.Registrants);
    }
}
