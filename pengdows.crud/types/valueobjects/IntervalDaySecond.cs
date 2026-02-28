// =============================================================================
// FILE: IntervalDaySecond.cs
// PURPOSE: Immutable value object for Oracle INTERVAL DAY TO SECOND type.
//
// AI SUMMARY:
// - Represents an interval with days and sub-day time components.
// - Readonly struct implementing IEquatable<IntervalDaySecond>.
// - Properties:
//   * Days: int - number of days
//   * Time: TimeSpan - hours, minutes, seconds, milliseconds
//   * TotalTime: TimeSpan - combined Days as TimeSpan + Time
// - FromTimeSpan(): Splits TimeSpan into Days + residual Time.
// - Parse(): Parses ISO 8601 format (P{days}DT{hours}H{mins}M{secs}S).
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing an Oracle INTERVAL DAY TO SECOND.
/// </summary>
/// <remarks>
/// Represents a duration in days, hours, minutes, seconds, and fractional seconds.
/// No month/year component - use <see cref="IntervalYearMonth"/> for those.
/// </remarks>
public readonly struct IntervalDaySecond : IEquatable<IntervalDaySecond>
{
    public IntervalDaySecond(int days, TimeSpan time)
    {
        Days = days;
        Time = time;
    }

    public int Days { get; }
    public TimeSpan Time { get; }

    public TimeSpan TotalTime => TimeSpan.FromDays(Days) + Time;

    public override string ToString()
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} days {1:c}",
            Days,
            Time);
    }

    public bool Equals(IntervalDaySecond other)
    {
        return Days == other.Days && Time.Equals(other.Time);
    }

    public override bool Equals(object? obj)
    {
        return obj is IntervalDaySecond other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Days, Time);
    }

    public static IntervalDaySecond FromTimeSpan(TimeSpan value)
    {
        var days = (int)value.TotalDays;
        var residual = value - TimeSpan.FromDays(days);
        return new IntervalDaySecond(days, residual);
    }

    public static IntervalDaySecond Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalDaySecond(0, TimeSpan.Zero);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var timeIndex = trimmed.IndexOf('T');
        var datePart = trimmed;
        string? timePart = null;
        if (timeIndex >= 0)
        {
            datePart = trimmed.Substring(0, timeIndex);
            timePart = trimmed.Substring(timeIndex + 1);
        }

        var days = 0;
        if (!string.IsNullOrEmpty(datePart))
        {
            var buffer = string.Empty;
            foreach (var c in datePart)
            {
                if (char.IsDigit(c) || c == '-' || c == '+')
                {
                    buffer += c;
                    continue;
                }

                if (buffer.Length == 0)
                {
                    continue;
                }

                if (c == 'D' || c == 'd')
                {
                    days = int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                }

                buffer = string.Empty;
            }
        }

        var hours = 0;
        var minutes = 0;
        var seconds = 0.0;

        if (!string.IsNullOrEmpty(timePart))
        {
            var buffer = string.Empty;
            foreach (var c in timePart)
            {
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
                {
                    buffer += c;
                    continue;
                }

                if (buffer.Length == 0)
                {
                    continue;
                }

                switch (c)
                {
                    case 'H' or 'h':
                        hours = int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case 'M' or 'm':
                        minutes = int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case 'S' or 's':
                        seconds = double.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                }

                buffer = string.Empty;
            }
        }

        var timeSpan = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return new IntervalDaySecond(days, timeSpan);
    }
}