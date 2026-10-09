using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
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
    public void WhenOn_AndNothingRequested_EachRoleIsSizedToTheServersUsableLimit()
    {
        using var context = Create(c => c.ClampPoolsToServerConnectionLimit = true);

        Assert.Equal((22, 22), Slots(context));
    }

    [Fact]
    public void Headroom_IsTakenOffTheServersUsableLimit()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.ResourceConnectionHeadroom = 2;
        });

        Assert.Equal((20, 20), Slots(context));
    }

    [Fact]
    public void AnExplicitRequestBelowTheLimit_IsKept_PerRole()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 10;
        });

        Assert.Equal((10, 22), Slots(context));
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

        Assert.Equal((22, 22), Slots(context));
    }

    [Fact]
    public void SameServerPools_ShareOneServerWideAdmissionGate()
    {
        using var context = Create(c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
        });

        var reader = (PoolGovernor)typeof(DatabaseContext)
            .GetField("_readerGovernor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;
        var writer = (PoolGovernor)typeof(DatabaseContext)
            .GetField("_writerGovernor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;
        var sharedField = typeof(PoolGovernor).GetField("_sharedConcurrencyGate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(sharedField);
        var shared = sharedField!.GetValue(reader);
        Assert.NotNull(shared);
        Assert.Same(shared, sharedField.GetValue(writer));
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

        Assert.Equal((22, 22), Slots(context));
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
