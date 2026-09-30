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

        var snapshot = stats.GetSnapshot();

        Assert.Equal(1, snapshot.ReadRequests);
        Assert.Equal(1, snapshot.WriteRequests);
    }
}
