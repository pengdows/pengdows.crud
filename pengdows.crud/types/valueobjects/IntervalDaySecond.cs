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
// - Parse(): ISO 8601, SQL/Oracle/Informix and word forms, strict (DRY-012).
// - Thread-safe and immutable.
// =============================================================================

using System.Globalization;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Parses ISO-8601 (<c>P1DT2H3M4.5S</c>), SQL/Oracle/Informix (<c>+000000001 02:03:04.123456</c>,
    /// <c>02:03:04</c>) or word (<c>1 day 02:03:04</c>, <see cref="ToString"/>'s form) text, ignoring
    /// case, exactly to the tick (further digits are truncated). Blank text is a zero interval; any
    /// other text throws <see cref="FormatException"/>.
    /// </summary>
    public static IntervalDaySecond Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalDaySecond(0, TimeSpan.Zero);
        }

        return TryParseText(text, out var value)
            ? value
            : throw new FormatException("The text is not an INTERVAL DAY TO SECOND.");
    }

    // DRY-012: the one strict parser (the converter reads with it too). Blank is not an interval here.
    private static readonly Regex IsoForm = new(
        @"^(?<neg>-)?P?(?=[+-]?\d|T)(?:(?<d>[+-]?\d+)D)?(?<t>T(?:(?<h>[+-]?\d+)H)?(?:(?<mi>[+-]?\d+)M)?(?:(?<s>[+-]?\d+)(?:\.(?<f>\d+))?S)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ClockForm = new(
        @"^(?<neg>[+-])?(?:(?<d>\d+)\s+)?(?<h>\d+):(?<mi>\d{1,2})(?::(?<s>\d{1,2})(?:\.(?<f>\d+))?)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WordForm = new(
        @"^(?<d>[+-]?\d+)\s*days?(?:\s+(?<clockneg>[+-])?(?<h>\d+):(?<mi>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d+))?)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool TryParseText(string? text, out IntervalDaySecond value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        try
        {
            var iso = IsoForm.Match(trimmed);
            if (iso.Success)
            {
                var hasTime = iso.Groups["h"].Success || iso.Groups["mi"].Success || iso.Groups["s"].Success;
                if ((iso.Groups["t"].Success && !hasTime) || (!hasTime && !iso.Groups["d"].Success))
                {
                    return false;
                }

                var ticks = checked(Component(iso.Groups["d"]) * TimeSpan.TicksPerDay +
                                    Component(iso.Groups["h"]) * TimeSpan.TicksPerHour +
                                    Component(iso.Groups["mi"]) * TimeSpan.TicksPerMinute +
                                    SecondsTicks(iso.Groups["s"], iso.Groups["f"]));
                value = FromTimeSpan(TimeSpan.FromTicks(iso.Groups["neg"].Success ? -ticks : ticks));
                return true;
            }

            var clock = ClockForm.Match(trimmed);
            var words = clock.Success ? Match.Empty : WordForm.Match(trimmed);
            var match = clock.Success ? clock : words;
            if (!match.Success)
            {
                return false;
            }

            var minutes = Component(match.Groups["mi"]);
            var seconds = Component(match.Groups["s"]);
            if (minutes > 59 || seconds > 59 || (match.Groups["d"].Success && match.Groups["h"].Success && Component(match.Groups["h"]) > 23))
            {
                return false;
            }

            var clockTicks = checked(Component(match.Groups["h"]) * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute +
                                     SecondsTicks(match.Groups["s"], match.Groups["f"]));
            var days = Component(match.Groups["d"]);
            long total;
            if (clock.Success)
            {
                total = checked(days * TimeSpan.TicksPerDay + clockTicks);
                total = match.Groups["neg"].Value == "-" ? -total : total;
            }
            else
            {
                total = checked(days * TimeSpan.TicksPerDay + (match.Groups["clockneg"].Value == "-" ? -clockTicks : clockTicks));
            }

            value = FromTimeSpan(TimeSpan.FromTicks(total));
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static long Component(Group group) =>
        group.Success ? long.Parse(group.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;

    // Whole seconds plus up to 7 fraction digits as ticks; further digits are truncated, never rounded.
    private static long SecondsTicks(Group seconds, Group fraction)
    {
        var whole = Component(seconds);
        if (!fraction.Success)
        {
            return checked(whole * TimeSpan.TicksPerSecond);
        }

        var digits = fraction.Value.Length > 7 ? fraction.Value[..7] : fraction.Value.PadRight(7, '0');
        var ticks = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return checked(whole * TimeSpan.TicksPerSecond + (seconds.Value.StartsWith('-') ? -ticks : ticks));
    }
}
