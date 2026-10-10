using System.Diagnostics;

namespace pengdows.crud.infrastructure;

/// <summary>
/// The clock behind deadlines and the wait, hold and duration metrics. Everything that reads it converts ticks with
/// <see cref="Stopwatch.Frequency"/>, so a clock ticking at any other frequency would make every deadline and
/// duration wrong without an error; it is refused instead.
/// </summary>
internal static class MetricsClock
{
    internal static TimeProvider Resolve(TimeProvider? timeProvider)
    {
        var resolved = timeProvider ?? TimeProvider.System;
        if (resolved.TimestampFrequency != Stopwatch.Frequency)
        {
            throw new ArgumentException(
                $"The clock ticks at {resolved.TimestampFrequency} Hz; deadlines and metrics are converted at the Stopwatch frequency of {Stopwatch.Frequency} Hz.",
                nameof(timeProvider));
        }

        return resolved;
    }
}
