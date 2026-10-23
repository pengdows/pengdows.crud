using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

/// <summary>
/// Connection counts come from pool leases, so a TrackedConnection no longer subscribes to the wrapped connection's
/// StateChange at all (REL-005 had reduced it to one handler; drivers report the event inconsistently, and some refuse
/// the unsubscription once disposed). Open and close durations are still measured around the provider calls.
/// </summary>
public sealed class TrackedConnectionStateHandlerTests
{
    [Fact]
    public void WrappedConnection_HasNoStateChangeSubscriber()
    {
        var inner = new fakeDbConnection();
        var metrics = new MetricsCollector(MetricsOptions.Default);
        using var tracked = new TrackedConnection(inner, metricsCollector: metrics);

        tracked.Open();
        Assert.Equal(0, inner.StateChangeSubscriberCount);
        tracked.Close();

        var snapshot = metrics.CreateSnapshot();
        Assert.Equal(0, snapshot.ConnectionsOpened);
        Assert.Equal(0, snapshot.ConnectionsClosed);
    }

    [Fact]
    public void Dispose_LeavesNoSubscriber()
    {
        var inner = new fakeDbConnection();
        var tracked = new TrackedConnection(inner, metricsCollector: new MetricsCollector(MetricsOptions.Default));
        tracked.Open();

        tracked.Dispose();

        Assert.Equal(0, inner.StateChangeSubscriberCount);
    }
}
