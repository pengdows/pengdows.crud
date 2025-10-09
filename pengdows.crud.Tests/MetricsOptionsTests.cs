using System;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

public class MetricsOptionsTests
{
    [Fact]
    public void DefaultOptions_AreValid()
    {
        var options = MetricsOptions.Default;
        Assert.True(options.LongConnectionThreshold > TimeSpan.Zero);
        Assert.False(options.EnableApproxPercentiles);
        Assert.True(options.PercentileWindowSize >= 2);
    }

    [Fact]
    public void LongConnectionThreshold_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new MetricsOptions
            {
                LongConnectionThreshold = TimeSpan.Zero
            };
        });
    }

    [Fact]
    public void PercentileWindowSize_MustBePowerOfTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new MetricsOptions
            {
                PercentileWindowSize = 3
            };
        });
    }
}

