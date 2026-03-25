using System;
using System.Diagnostics;
using System.Reflection;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies ConvertTimeoutToStopwatchTicks via independent assertions — not by replicating the formula.
/// Tests: absolute correctness for whole-second values, proportionality, monotonicity,
/// Math.Max(1,...) guard, and round-trip accuracy.
/// </summary>
public class PoolGovernorTimeoutConversionTests
{
    private static readonly MethodInfo s_convert = typeof(PoolGovernor)
        .GetMethod("ConvertTimeoutToStopwatchTicks",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static long Convert(TimeSpan ts) =>
        (long)s_convert.Invoke(null, new object[] { ts })!;

    // ── Absolute correctness for whole-second multiples ──────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    public void Convert_WholeSeconds_IsExactlyNTimesFrequency(int seconds)
    {
        // 1s must produce exactly Stopwatch.Frequency ticks — no rounding error allowed
        var result = Convert(TimeSpan.FromSeconds(seconds));
        Assert.Equal((long)seconds * Stopwatch.Frequency, result);
    }

    // ── Proportionality: result should be within 1 Stopwatch-tick of the ideal ──

    [Theory]
    [InlineData(500)]    // 500 ms
    [InlineData(250)]    // 250 ms
    [InlineData(1500)]   // 1.5 s
    [InlineData(2700)]   // 2.7 s
    [InlineData(100)]    // 100 ms
    [InlineData(1)]      // 1 ms
    public void Convert_ArbitraryMilliseconds_IsWithinOneStopwatchTickOfIdeal(int milliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        var result = Convert(timeout);

        // Ideal (real-valued): timeout.TotalSeconds * Frequency
        var ideal = timeout.TotalSeconds * Stopwatch.Frequency;
        Assert.InRange(result, (long)Math.Floor(ideal) - 1, (long)Math.Ceiling(ideal) + 1);
    }

    // ── Round-trip: converting back to TimeSpan should be within 1 µs ──────

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2500)]
    public void Convert_RoundTrip_IsWithinOneMicrosecond(int milliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        var ticks = Convert(timeout);

        var roundTripped = TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
        var error = (roundTripped - timeout).Duration();
        Assert.True(error <= TimeSpan.FromMicroseconds(1),
            $"Round-trip error {error.TotalMicroseconds:F3} µs exceeds 1 µs for {milliseconds} ms input");
    }

    // ── Monotonicity: longer timeout → more Stopwatch ticks ─────────────

    [Fact]
    public void Convert_LongerTimeout_ProducesMoreTicks()
    {
        Assert.True(Convert(TimeSpan.FromSeconds(2)) > Convert(TimeSpan.FromSeconds(1)));
        Assert.True(Convert(TimeSpan.FromMilliseconds(500)) > Convert(TimeSpan.FromMilliseconds(100)));
        Assert.True(Convert(TimeSpan.FromMinutes(1)) > Convert(TimeSpan.FromSeconds(30)));
    }

    // ── Math.Max(1,...) guard ─────────────────────────────────────────────

    [Fact]
    public void Convert_ZeroTimeout_ReturnsOne()
    {
        Assert.Equal(1L, Convert(TimeSpan.Zero));
    }

    [Fact]
    public void Convert_NegativeTimeout_ReturnsOne()
    {
        // Negative timeouts are guarded — must not return 0 or negative
        Assert.Equal(1L, Convert(TimeSpan.FromMilliseconds(-100)));
    }

    [Fact]
    public void Convert_OneTick_ReturnsAtLeastOneAndAtMostOneSecond()
    {
        // 1 tick = 100 ns — must not return 0 and must not exceed 1 second of Stopwatch ticks
        var result = Convert(TimeSpan.FromTicks(1));
        Assert.InRange(result, 1L, Stopwatch.Frequency);
    }

    // ── Large values don't overflow or produce absurd results ────────────

    [Theory]
    [InlineData(60)]     // 1 minute
    [InlineData(3600)]   // 1 hour
    public void Convert_LargeWholeSecondTimeout_IsExact(int seconds)
    {
        // Large whole-second inputs must produce exactly n * Frequency — no overflow, no truncation
        Assert.Equal((long)seconds * Stopwatch.Frequency, Convert(TimeSpan.FromSeconds(seconds)));
    }
}
