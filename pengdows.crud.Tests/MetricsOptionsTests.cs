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

    [Fact]
    public void SlowCommandThreshold_DefaultIsOneSecond()
    {
        var options = MetricsOptions.Default;
        Assert.Equal(TimeSpan.FromSeconds(1), options.SlowCommandThreshold);
    }

    [Fact]
    public void SlowCommandThreshold_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new MetricsOptions
            {
                SlowCommandThreshold = TimeSpan.Zero
            };
        });
    }

    [Fact]
    public void SlowCommandThreshold_NegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new MetricsOptions
            {
                SlowCommandThreshold = TimeSpan.FromSeconds(-1)
            };
        });
    }
}