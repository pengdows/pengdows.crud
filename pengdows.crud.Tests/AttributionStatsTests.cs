#region

using pengdows.crud.metrics;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class AttributionStatsTests
{
    [Fact]
    public void GetSnapshot_IncludesEveryCounter()
    {
        var stats = new AttributionStats();
        stats.RecordReadRequest();
        stats.RecordWriteRequest();
        stats.RecordReadGovernorWait();
        stats.RecordWriteGovernorWait();
        stats.RecordReadGovernorTimeout();
        stats.RecordWriteGovernorTimeout();
        stats.RecordReadModeWait();
        stats.RecordWriteModeWait();

        var snapshot = stats.GetSnapshot();

        Assert.Equal(1, snapshot.ReadRequests);
        Assert.Equal(1, snapshot.WriteRequests);
        Assert.Equal(1, snapshot.ReadGovernorWaits);
        Assert.Equal(1, snapshot.WriteGovernorWaits);
        Assert.Equal(1, snapshot.ReadGovernorTimeouts);
        Assert.Equal(1, snapshot.WriteGovernorTimeouts);
        Assert.Equal(1, snapshot.ReadModeWaits);
        Assert.Equal(1, snapshot.WriteModeWaits);
    }
}
