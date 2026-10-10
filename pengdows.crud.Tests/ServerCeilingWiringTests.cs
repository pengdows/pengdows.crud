using System;
using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// With ClampPoolsToServerConnectionLimit on, a context reads the server's connection limit while it
/// initializes and sizes each role's governor (and provider pool) no higher than it. Against a
/// PostgreSQL that allows 25 connections, 3 of them held back for superusers, an ordinary client may
/// use 22 — not the provider default of 100, which is what failed in the storm tests.
/// </summary>
public sealed class ServerCeilingWiringTests
{
    private const string ConnectionString = "Host=db1;Database=d;Username=u;Password=p";

    private static DatabaseContext Create(
        Action<DatabaseContextConfiguration> configure,
        string maxConnections = "25",
        SupportedDatabase product = SupportedDatabase.PostgreSql)
    {
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        connection.ScalarResultsByCommand["SHOW max_connections"] = maxConnections;
        connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };
        configure(config);
        return new DatabaseContext(config, factory);
    }

    private static (int Reader, int Writer) Slots(DatabaseContext context) =>
        (context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots,
            context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);

    [Fact]
    public void WhenOff_PoolsKeepTheProviderDefault_EvenAgainstASmallServer()
    {
        using var context = Create(_ => { });

        Assert.Equal((100, 100), Slots(context));
    }

    [Fact]
    public void WhenOn_AndNothingRequested_TheServersUsableLimitIsSplitBetweenTheRoles()
    {
        using var context = Create(c => c.ClampPoolsToServerConnectionLimit = true);

        // Each role asks for the provider default (100) against 22 usable connections: the two pools
        // together may hold 22, so each governor is sized to its pool's half.
        Assert.Equal((11, 11), Slots(context));
    }

    [Fact]
    public void Headroom_IsTakenOffTheServersUsableLimit()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.ResourceConnectionHeadroom = 2;
        });

        Assert.Equal((10, 10), Slots(context));
    }

    [Fact]
    public void ExplicitRequestsThatTogetherFitTheLimit_AreKeptPerRole()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 5;
            c.MaxConcurrentWrites = 10;
        });

        Assert.Equal((5, 10), Slots(context));
    }

    [Fact]
    public void AnExplicitReadRequestBesideADefaultWritePool_IsSharedOutProportionally()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 10;
        });

        // 10 reads against the writer's default (itself clamped to 22): 32 asked of 22, so 10/32 and 22/32.
        Assert.Equal((6, 16), Slots(context));
    }

    [Fact]
    public void AnExplicitRequestAboveTheLimit_IsClamped()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 50;
            c.MaxConcurrentWrites = 50;
        });

        Assert.Equal((11, 11), Slots(context));
    }

    [Fact]
    public void TheTwoRolesTogether_CannotHoldMoreConnectionsThanTheServerAllows()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
            c.PoolAcquireTimeout = TimeSpan.FromMilliseconds(300);
        });
        var held = new List<ITrackedConnection>();

        try
        {
            // The server allows 22; the two role pools split it, 11 each, and every extra caller waits
            // under PoolAcquireTimeout instead of reaching a server that would refuse it.
            for (var i = 0; i < 11; i++)
            {
                held.Add(context.GetConnection(ExecutionType.Write));
                held.Add(context.GetConnection(ExecutionType.Read));
            }

            Assert.Throws<PoolSaturatedException>(() => context.GetConnection(ExecutionType.Read));
            Assert.Throws<PoolSaturatedException>(() => context.GetConnection(ExecutionType.Write));
        }
        finally
        {
            foreach (var connection in held)
            {
                connection.Dispose();
            }
        }
    }

    [Fact]
    public void HeadroomThatConsumesTheWholeLimit_IsRejectedWhenTheContextStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.ResourceConnectionHeadroom = 22;
        }));
    }

    [Fact]
    public void AnUnreadableServerLimit_LeavesThePoolsAlone()
    {
        using var context = Create(c => c.ClampPoolsToServerConnectionLimit = true, maxConnections: "n/a");

        Assert.Equal((100, 100), Slots(context));
    }

    // ── a read replica is a different server: it has its own limit and must not inherit the primary's ──

    private static DatabaseContext CreateWithReplica(
        string primaryMax, string? replicaMax, string replicaConnectionString = "Host=replica;Database=d;Username=u;Password=p",
        Action<DatabaseContextConfiguration>? configure = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        var primary = new fakeDbConnection();
        primary.ScalarResultsByCommand["SHOW max_connections"] = primaryMax;
        primary.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        primary.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(primary);

        var replica = new fakeDbConnection();
        replica.ScalarResultsByCommand["SHOW max_connections"] = replicaMax ?? "n/a";
        replica.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        replica.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(replica);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            ReadOnlyConnectionString = replicaConnectionString,
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        };
        configure?.Invoke(config);
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public void AReplicaWithALargerLimit_IsNotClampedToThePrimarysSmallerOne()
    {
        using var context = CreateWithReplica(primaryMax: "25", replicaMax: "100");

        // writer: primary's 25 - 3 reserved = 22; reader: the replica's own 100 - 3 = 97.
        Assert.Equal((97, 22), Slots(context));
    }

    [Fact]
    public void AReplicaWithASmallerLimit_IsClampedToItsOwn_NotThePrimarys()
    {
        using var context = CreateWithReplica(primaryMax: "100", replicaMax: "25");

        Assert.Equal((22, 97), Slots(context));
    }

    [Fact]
    public void AReplicaWhoseLimitCannotBeRead_IsLeftAlone_NotGivenThePrimarysNumber()
    {
        using var context = CreateWithReplica(primaryMax: "25", replicaMax: null);

        Assert.Equal((100, 22), Slots(context));
    }

    [Fact]
    public void AReadConnectionStringOnTheSameServer_IsClampedByThatServersOneLimit()
    {
        // Same host and port, different application name: one server, one limit, no second probe needed.
        using var context = CreateWithReplica(
            primaryMax: "25", replicaMax: "999",
            replicaConnectionString: "Host=db1;Database=d;Username=u;Password=p;Application Name=ro");

        Assert.Equal((11, 11), Slots(context));
    }

    [Fact]
    public void TheSharedBudgetIsSplitAcrossTheProviderPools_AndStampedOnEachRolesConnectionString()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
        });

        // The server allows 22 and the roles ask for 40, so the provider pools split the 22 (11 + 11)
        // while each governor still admits up to its own slot count.
        using var write = context.GetConnection(ExecutionType.Write);
        using var read = context.GetConnection(ExecutionType.Read);

        Assert.Contains("Maximum Pool Size=11", write.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Maximum Pool Size=11", read.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReplicaProbeConnection_IsDisposedOnceTheLimitHasBeenRead()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var primary = new fakeDbConnection();
        primary.ScalarResultsByCommand["SHOW max_connections"] = "25";
        primary.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        primary.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(primary);

        var replica = new fakeDbConnection();
        replica.ScalarResultsByCommand["SHOW max_connections"] = "100";
        replica.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        replica.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(replica);

        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            ReadOnlyConnectionString = "Host=replica;Database=d;Username=u;Password=p",
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        }, factory);

        Assert.Equal((97, 22), Slots(context));
        Assert.True(replica.DisposeCount >= 1, "The replica probe connection was left open.");
    }

    [Fact]
    public void AReadConnectionStringThatSpellsOutTheDialectsDefaultPort_IsStillTheSameServer()
    {
        // PostgreSQL's default port is 5432: "Host=db1" and "Host=db1;Port=5432" are one server, so the
        // reader must not be probed as a replica and handed the 999 that fake replica would report.
        using var context = CreateWithReplica(
            primaryMax: "25", replicaMax: "999",
            replicaConnectionString: "Host=db1;Port=5432;Database=d;Username=u;Password=p;Application Name=ro");

        Assert.Equal((11, 11), Slots(context));
    }

    [Fact]
    public void AReadConnectionStringOnTheSameHostButADifferentPort_IsAReplica()
    {
        using var context = CreateWithReplica(
            primaryMax: "25", replicaMax: "100",
            replicaConnectionString: "Host=db1;Port=5433;Database=d;Username=u;Password=p");

        Assert.Equal((97, 22), Slots(context));
    }

    [Fact]
    public void Headroom_IsTakenOffEachServersOwnLimit()
    {
        using var context = CreateWithReplica(
            primaryMax: "100", replicaMax: "25", configure: c => c.ResourceConnectionHeadroom = 5);

        // reader: replica 25 - 3 reserved - 5 headroom = 17; writer: primary 100 - 3 - 5 = 92.
        Assert.Equal((17, 92), Slots(context));
    }

    [Fact]
    public void HeadroomThatConsumesTheReplicasWholeLimit_IsRejected_EvenWhenThePrimaryCouldAffordIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateWithReplica(primaryMax: "500", replicaMax: "25", configure: c => c.ResourceConnectionHeadroom = 22));
    }

    [Fact]
    public void AReplicaThatCannotBeReached_DoesNotStopTheContextStarting_AndIsLeftAlone()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var primary = new fakeDbConnection();
        primary.ScalarResultsByCommand["SHOW max_connections"] = "25";
        primary.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        primary.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(primary);

        var replica = new fakeDbConnection();
        replica.SetFailOnOpen();
        factory.Connections.Add(replica);

        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            ReadOnlyConnectionString = "Host=replica;Database=d;Username=u;Password=p",
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        }, factory);

        Assert.Equal((100, 22), Slots(context));
    }
}
