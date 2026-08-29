using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PreventDatabaseUnloadTests
{
    [Fact]
    public void KeepAlive_IsCompatibilityAlias()
    {
        Assert.Equal(DbMode.PreventDatabaseUnload, DbMode.KeepAlive);
        Assert.Equal((int)DbMode.PreventDatabaseUnload, 1);
    }

    [Fact]
    public void ReadOnlySinglePool_HasOneSentinelAndConsumesOneReaderPermit()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            MaxConcurrentReads = 3
        };

        using var context = new DatabaseContext(config, factory);

        var sentinels = context.GetSentinelSnapshot();
        Assert.Single(sentinels);
        Assert.Equal(ExecutionType.Read, sentinels[0].ExecutionType);
        Assert.Equal(1, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
    }

    [Fact]
    public void SeparateReadWritePools_HaveOneSentinelEach()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=primary;Database=test;EmulatedProduct=SqlServer",
            ReadOnlyConnectionString = "Server=replica;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            MaxConcurrentReads = 3,
            MaxConcurrentWrites = 4
        };

        using var context = new DatabaseContext(config, factory);

        var sentinels = context.GetSentinelSnapshot();
        Assert.Equal(2, sentinels.Count);
        Assert.Single(sentinels, s => s.ExecutionType == ExecutionType.Read);
        Assert.Single(sentinels, s => s.ExecutionType == ExecutionType.Write);
        Assert.Equal(1, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
        Assert.Equal(1, context.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
    }

    [Fact]
    public void BrokenSentinel_IsReplacedThroughNormalConnectionPath()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, factory);
        var original = context.GetSentinelSnapshot()[0].Connection;
        var underlying = (fakeDbConnection)((IInternalConnectionWrapper)original).UnderlyingConnection;
        underlying.BreakConnection();

        using var work = context.GetConnection(ExecutionType.Read);

        var replacement = context.GetSentinelSnapshot()[0].Connection;
        Assert.NotSame(original, replacement);
        Assert.Equal(ConnectionState.Open, replacement.State);
        Assert.True(((TrackedConnection)original).IsDisposed);
        Assert.True(factory.CreatedConnections.Count >= 3);
    }

    [Fact]
    public void Sentinel_RemainsPassiveAndIsNeverHandedToApplicationWork()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, factory);
        var sentinel = context.GetSentinelSnapshot()[0].Connection;
        var underlying = (fakeDbConnection)((IInternalConnectionWrapper)sentinel).UnderlyingConnection;
        var commandCount = underlying.CreatedCommands.Count;
        var executedCount = underlying.ExecutedNonQueryTexts.Count + underlying.ExecutedReaderTexts.Count;

        using var work = context.GetConnection(ExecutionType.Read);

        Assert.NotSame(sentinel, work);
        Assert.Equal(commandCount, underlying.CreatedCommands.Count);
        Assert.Equal(executedCount, underlying.ExecutedNonQueryTexts.Count + underlying.ExecutedReaderTexts.Count);
    }

    [Fact]
    public void DisposingContext_ClosesEverySentinel()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=primary;Database=test;EmulatedProduct=SqlServer",
            ReadOnlyConnectionString = "Server=replica;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload
        };

        var context = new DatabaseContext(config, factory);
        var sentinels = context.GetSentinelSnapshot()
            .Select(s => (fakeDbConnection)((IInternalConnectionWrapper)s.Connection).UnderlyingConnection)
            .ToArray();

        context.Dispose();

        Assert.All(sentinels, connection => Assert.Equal(ConnectionState.Closed, connection.State));
    }

    [Fact]
    public void PreventDatabaseUnload_RaisesPoolBelowTwo()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            MaxConcurrentReads = 1,
            MaxConcurrentWrites = 1
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(2, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);
        Assert.Equal(2, context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);
        var connectionString = new DbConnectionStringBuilder { ConnectionString = context.RawConnectionString };
        Assert.Equal(2, Convert.ToInt32(connectionString["Max Pool Size"]));
        Assert.Equal(2, Convert.ToInt32(connectionString["Min Pool Size"]));
    }

    [Fact]
    public void ReadOnlyPreventDatabaseUnload_DisablesWriterMinimumAndRetainsReaderMinimum()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.Equal(100, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);
        var connectionString = new DbConnectionStringBuilder { ConnectionString = context.RawReaderConnectionString };
        Assert.Equal(2, Convert.ToInt32(connectionString["Min Pool Size"]));
    }

    [Fact]
    public void Construction_DoesNotAttributeSentinelSlotAcquisitionAsApplicationRequests()
    {
        // AttachInitialSentinelSlotsIfNeeded() acquires a governor slot for each sentinel at
        // construction time, and the secondary read-sentinel path (separate read/write
        // connection strings) creates its sentinel through the same FactoryCreateConnection
        // overload used for dialect detection and TestConnect. Neither should be counted as an
        // application-issued read/write request — Metrics.ReadRequests/WriteRequests are
        // documented as requests the context admitted on behalf of the application, not
        // infrastructure bookkeeping performed before the application ever runs a query.
        // EnableMetrics=true is required: Metrics returns an all-zero snapshot whenever the
        // (opt-in) metrics collector is disabled, which would make this assertion pass
        // vacuously regardless of whether the underlying attribution counters are contaminated.
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=primary;Database=test;EmulatedProduct=SqlServer",
            ReadOnlyConnectionString = "Server=replica;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            MaxConcurrentReads = 3,
            MaxConcurrentWrites = 4,
            EnableMetrics = true
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(2, context.GetSentinelSnapshot().Count);
        Assert.Equal(0, context.Metrics.ReadRequests);
        Assert.Equal(0, context.Metrics.WriteRequests);
    }
}
