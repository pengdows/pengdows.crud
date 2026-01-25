using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.metrics;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class TrackedWrapperTests
{
    [Fact]
    public void TrackedConnection_Ephemeral_GetLock_IsNoOp()
    {
        using var tracked = new TrackedConnection(new fakeDbConnection());
        using var locker = tracked.GetLock();
        Assert.IsType<NoOpAsyncLocker>(locker);
    }

    [Fact]
    public void TrackedConnection_Shared_GetLock_IsRealLocker()
    {
        using var tracked = new TrackedConnection(new fakeDbConnection(), isSharedConnection: true);
        using var locker = tracked.GetLock();
        Assert.IsType<RealAsyncLocker>(locker);
    }

    [Fact]
    public void TrackedConnection_OpenClose_RecordsMetrics()
    {
        var metrics = new MetricsCollector(MetricsOptions.Default);
        using var tracked = new TrackedConnection(new fakeDbConnection(), metricsCollector: metrics);
        tracked.Open();
        tracked.Close();
        var snapshot = metrics.CreateSnapshot();
        Assert.Equal(1, snapshot.ConnectionsOpened);
        Assert.Equal(1, snapshot.ConnectionsClosed);
        Assert.True(tracked.WasOpened);
    }

    [Fact]
    public async Task TrackedConnection_OpenAsync_GetSchema_ReturnsTable()
    {
        var fake = new fakeDbConnection();
        fake.EmulatedProduct = SupportedDatabase.Unknown;
        await using var tracked = new TrackedConnection(fake);
        await tracked.OpenAsync();
        var schema = tracked.GetSchema();
        Assert.NotNull(schema);
    }

    [Fact]
    public void TrackedReader_ReadAndDispose_RecordsMetricsAndThrowsOnNextResult()
    {
        var metrics = new MetricsCollector(MetricsOptions.Default);
        using var connection = new TrackedConnection(new fakeDbConnection(), metricsCollector: metrics);
        var rows = new[]
        {
            new Dictionary<string, object> { ["value"] = 42 }
        };

        using var trackedReader = new TrackedReader(
            new fakeDbDataReader(rows),
            connection,
            NoOpAsyncLocker.Instance,
            true,
            metricsCollector: metrics);

        Assert.True(trackedReader.Read());
        Assert.False(trackedReader.Read());
        Assert.Throws<NotSupportedException>(() => trackedReader.NextResult());
        trackedReader.Close();
        trackedReader.Dispose();

        var snapshot = metrics.CreateSnapshot();
        Assert.Equal(1, snapshot.RowsReadTotal);
    }
}