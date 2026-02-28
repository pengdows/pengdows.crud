using System;

namespace pengdows.crud.metrics;

/// <summary>
/// Configuration for DatabaseContext metrics collection.
/// </summary>
public interface IMetricsOptions
{
    /// <summary>
    /// Threshold that classifies a connection as long-lived.
    /// Defaults to 30 seconds.
    /// </summary>
    TimeSpan LongConnectionThreshold { get; }

    /// <summary>
    /// Enables approximate percentile tracking for command duration.
    /// </summary>
    bool EnableApproxPercentiles { get; }

    /// <summary>
    /// Sliding window size for percentile approximation. Must be a power of two.
    /// </summary>
    int PercentileWindowSize { get; }

    /// <summary>
    /// Commands that exceed this duration are counted as slow.
    /// Defaults to 1 second. Must be positive.
    /// </summary>
    TimeSpan SlowCommandThreshold { get; }
}