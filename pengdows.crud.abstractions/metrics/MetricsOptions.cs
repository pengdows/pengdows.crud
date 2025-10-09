using System;

namespace pengdows.crud.metrics;

/// <summary>
/// Configuration for DatabaseContext metrics collection.
/// </summary>
public sealed class MetricsOptions
{
    private TimeSpan _longConnectionThreshold = TimeSpan.FromSeconds(30);
    private int _percentileWindowSize = 2048;

    /// <summary>
    /// Threshold that classifies a connection as long-lived.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan LongConnectionThreshold
    {
        get => _longConnectionThreshold;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Threshold must be positive.");
            }

            _longConnectionThreshold = value;
        }
    }

    /// <summary>
    /// Enables approximate percentile tracking for command duration.
    /// </summary>
    public bool EnableApproxPercentiles { get; init; }

    /// <summary>
    /// Sliding window size for percentile approximation. Must be a power of two.
    /// </summary>
    public int PercentileWindowSize
    {
        get => _percentileWindowSize;
        init
        {
            if (value < 2 || (value & (value - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Window size must be a power of two >= 2.");
            }

            _percentileWindowSize = value;
        }
    }

    /// <summary>
    /// Returns a fresh copy of the default options.
    /// </summary>
    public static MetricsOptions Default => new();
}
