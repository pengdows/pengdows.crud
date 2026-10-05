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

using System.Globalization;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Parses ISO-8601 (<c>P1Y2M</c>), SQL/Oracle/Informix (<c>+0001-02</c>, <c>1-2</c>) or word
    /// (<c>1 year 2 mons</c>, <see cref="ToString"/>'s <c>1 years 2 months</c>) text, ignoring case.
    /// Blank text is a zero interval; any other text throws <see cref="FormatException"/>.
    /// </summary>
    public static IntervalYearMonth Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalYearMonth(0, 0);
        }

        return TryParseText(text, out var value)
            ? value
            : throw new FormatException("The text is not an INTERVAL YEAR TO MONTH.");
    }

    // DRY-012: the one strict parser (the converter reads with it too). Blank is not an interval here.
    private static readonly Regex IsoForm = new(@"^(?<neg>-)?P?(?=[+-]?\d+[YM])(?:(?<y>[+-]?\d+)Y)?(?:(?<m>[+-]?\d+)M)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SqlForm = new(@"^(?<neg>[+-])?(?<y>\d+)-(?<m>\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WordForm = new(
        @"^(?=[+-]?\d)(?:(?<y>[+-]?\d+)\s*years?)?\s*(?:(?<m>[+-]?\d+)\s*(?:months?|mons?))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool TryParseText(string? text, out IntervalYearMonth value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var match = IsoForm.Match(trimmed);
        var sql = false;
        if (!match.Success)
        {
            match = SqlForm.Match(trimmed);
            sql = match.Success;
        }

        if (!match.Success)
        {
            match = WordForm.Match(trimmed);
        }

        if (!match.Success || (!match.Groups["y"].Success && !match.Groups["m"].Success) ||
            !TryComponent(match.Groups["y"], out var years) || !TryComponent(match.Groups["m"], out var months) ||
            (sql && months > 11))
        {
            return false;
        }

        if (match.Groups["neg"].Value == "-")
        {
            years = -years;
            months = -months;
        }

        try
        {
            value = new IntervalYearMonth(years, months);
            _ = value.TotalMonths;
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryComponent(Group group, out int number)
    {
        number = 0;
        return !group.Success ||
               int.TryParse(group.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
    }
}
