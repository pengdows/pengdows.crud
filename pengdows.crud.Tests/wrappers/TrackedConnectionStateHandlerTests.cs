using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

/// <summary>
/// REL-005: a TrackedConnection subscribed three StateChange handlers to the connection it wraps
/// (the context's, metrics', and its own driver-report flag, a fresh method-group delegate each time),
/// so every checkout built and tore down a multicast delegate chain. One handler now dispatches to
/// all three, in the same order.
/// </summary>
public sealed class TrackedConnectionStateHandlerTests
{
    [Fact]
    public void WrappedConnection_HasOneStateChangeSubscriber_AndEveryHandlerStillRuns()
    {
        var inner = new fakeDbConnection();
        var contextEvents = 0;
        var metrics = new MetricsCollector(MetricsOptions.Default);
        using var tracked = new TrackedConnection(inner, (_, _) => contextEvents++, metricsCollector: metrics);

        Assert.Equal(1, inner.StateChangeSubscriberCount);

        tracked.Open();
        tracked.Close();

        Assert.Equal(2, contextEvents);
        Assert.Equal(1, metrics.CreateSnapshot().ConnectionsOpened);
    }

    [Fact]
    public void Dispose_RemovesTheSubscriber()
    {
        var inner = new fakeDbConnection();
        var tracked = new TrackedConnection(inner, (_, _) => { }, metricsCollector: new MetricsCollector(MetricsOptions.Default));

        tracked.Dispose();

        Assert.Equal(0, inner.StateChangeSubscriberCount);
    }
}
