using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TotalConnectionsReused was never incremented (provider pool reuse is invisible to
/// pengdows.crud) and ConnectionPoolEfficiency was computed only from it, so both always read
/// 0. They were [Obsolete] on 2.0.x and are removed on 3.0.
/// </summary>
public class DatabaseContextRemovedMetricsTests
{
    [Theory]
    [InlineData("TotalConnectionsReused")]
    [InlineData("ConnectionPoolEfficiency")]
    public void UntrackedPoolReuseMetrics_AreRemoved(string propertyName)
    {
        Assert.Null(typeof(DatabaseContext).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance));
    }
}
