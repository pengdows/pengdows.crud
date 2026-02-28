// =============================================================================
// FILE: IntervalYearMonth.cs
// PURPOSE: Immutable value object for Oracle INTERVAL YEAR TO MONTH type.
//
// AI SUMMARY:
// - Represents an interval with only years and months components.
// - Readonly struct implementing IEquatable<IntervalYearMonth>.
// - Properties:
//   * Years: int - number of years
//   * Months: int - number of months (0-11 typically)
//   * TotalMonths: int - computed as Years*12 + Months
// - FromTotalMonths(): Creates from total months count.
// - Parse(): Parses ISO 8601 format (P{years}Y{months}M).
// - Use case: Date arithmetic where month boundaries matter.
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing an Oracle INTERVAL YEAR TO MONTH.
/// </summary>
/// <remarks>
/// Represents a duration in years and months only, without day/time components.
/// Useful for date arithmetic where month boundaries are significant.
/// </remarks>
public readonly struct IntervalYearMonth : IEquatable<IntervalYearMonth>
{
    public IntervalYearMonth(int years, int months)
    {
        Years = years;
        Months = months;
    }

    public int Years { get; }
    public int Months { get; }

    public int TotalMonths => checked(Years * 12 + Months);

    public override string ToString()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} years {1} months", Years, Months);
    }

    public bool Equals(IntervalYearMonth other)
    {
        return Years == other.Years && Months == other.Months;
    }

    public override bool Equals(object? obj)
    {
        return obj is IntervalYearMonth other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Years, Months);
    }

    public static IntervalYearMonth FromTotalMonths(int totalMonths)
    {
        var years = totalMonths / 12;
        var months = totalMonths % 12;
        return new IntervalYearMonth(years, months);
    }

    public static IntervalYearMonth Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalYearMonth(0, 0);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var years = 0;
        var months = 0;
        var buffer = string.Empty;

        foreach (var c in trimmed)
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

            switch (c)
            {
                case 'Y' or 'y':
                    years = int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'M' or 'm':
                    months = int.Parse(buffer, System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }

            buffer = string.Empty;
        }

        return new IntervalYearMonth(years, months);
    }
}