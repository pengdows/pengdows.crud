using System;
using System.Diagnostics;
using System.Reflection;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies ConvertTimeoutToStopwatchTicks converts TimeSpan values to Stopwatch ticks correctly.
/// Exercises the sub-second precision path and the Math.Max(1,...) guard.
/// </summary>
public class PoolGovernorTimeoutConversionTests
{
    private static readonly MethodInfo s_convert = typeof(PoolGovernor)
        .GetMethod("ConvertTimeoutToStopwatchTicks",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static long Convert(TimeSpan ts) =>
        (long)s_convert.Invoke(null, new object[] { ts })!;

    [Theory]
    [InlineData(1000)]   // 1 s
    [InlineData(500)]    // 500 ms — sub-second, exercises remainder path
    [InlineData(1500)]   // 1.5 s — spans both terms
    [InlineData(100)]    // 100 ms
    [InlineData(1)]      // 1 ms — very small, must not be 0 after Math.Max
    public void Convert_VariousMilliseconds_MatchesExpectedStopwatchTicks(int milliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        var result = Convert(timeout);

        // Expected: timeout expressed in Stopwatch ticks, rounded down, minimum 1
        var expected = Math.Max(1L, (long)(timeout.TotalSeconds * Stopwatch.Frequency));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_ZeroTimeout_ReturnsOne()
    {
        // Math.Max(1, 0) guard must fire
        Assert.Equal(1L, Convert(TimeSpan.Zero));
    }

    [Fact]
    public void Convert_SubMillisecondTimeout_ReturnsAtLeastOne()
    {
        // Even a 1-tick TimeSpan must not return 0
        var oneTick = TimeSpan.FromTicks(1);
        Assert.True(Convert(oneTick) >= 1);
    }
}
