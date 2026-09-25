// =============================================================================
// FILE: Range.cs
// PURPOSE: Generic value object for PostgreSQL range types.
//
// AI SUMMARY:
// - Represents a range of values with inclusive/exclusive bounds.
// - Generic readonly struct Range<T> where T : struct.
// - Properties:
//   * Lower, Upper: T? - nullable bounds (null = infinite)
//   * IsLowerInclusive, IsUpperInclusive: bool - bound inclusion
//   * HasLowerBound, HasUpperBound, IsEmpty: convenience properties
// - Static Empty: PostgreSQL's empty range, distinct from the unbounded (,) range (which means
//   "all values"). IsEmpty is true for both on 2.0.x.
// - Parse(): Parses PostgreSQL bracket notation (e.g., "[1,10)", "(,100]").
// - ToString(): Returns bracket notation with invariant culture.
// - ParseValue(): Handles int, long, decimal, double, DateTime, DateTimeOffset.
// - Common PostgreSQL types: int4range, int8range, numrange, daterange, tsrange.
// - Thread-safe and immutable.
// =============================================================================

using System.Globalization;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Generic value object representing a range with inclusive/exclusive bounds.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
/// <remarks>
/// Maps to PostgreSQL range types (int4range, daterange, tsrange, etc.).
/// Uses bracket notation: [inclusive, exclusive) or (exclusive, inclusive].
/// </remarks>
public readonly struct Range<T> : IEquatable<Range<T>> where T : struct
{
    private readonly bool _isEmptyRange;

    public Range(T? lower, T? upper, bool isLowerInclusive = true, bool isUpperInclusive = false)
    {
        Lower = lower;
        Upper = upper;
        IsLowerInclusive = isLowerInclusive;
        IsUpperInclusive = isUpperInclusive;
        _isEmptyRange = false;
    }

    private Range(bool isEmptyRange)
    {
        Lower = null;
        Upper = null;
        IsLowerInclusive = false;
        IsUpperInclusive = false;
        _isEmptyRange = isEmptyRange;
    }

    public T? Lower { get; }
    public T? Upper { get; }
    public bool IsLowerInclusive { get; }
    public bool IsUpperInclusive { get; }

    public bool HasLowerBound => Lower is not null;
    public bool HasUpperBound => Upper is not null;
    /// <summary>
    /// True for <see cref="Empty"/>, and also for a range with neither bound (the unbounded
    /// <c>(,)</c> range, which PostgreSQL treats as "all values"); use
    /// <see cref="IsEmptyRange"/> to tell them apart.
    /// </summary>
    public bool IsEmpty => _isEmptyRange || (!HasLowerBound && !HasUpperBound);

    /// <summary>
    /// True only for <see cref="Empty"/>: the range that contains no values (PostgreSQL <c>empty</c>).
    /// </summary>
    public bool IsEmptyRange => _isEmptyRange;

    /// <summary>
    /// The empty range (PostgreSQL <c>empty</c>). Not the same value as <c>default</c>, which is
    /// the unbounded range.
    /// </summary>
    public static Range<T> Empty { get; } = new(isEmptyRange: true);

    /// <summary>
    /// Parse a canonical range string like "[1,5)" or "(,10]".
    /// </summary>
    public static Range<T> Parse(string rangeText)
    {
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            throw new ArgumentException("Range text cannot be null or empty", nameof(rangeText));
        }

        rangeText = rangeText.Trim();

        // PostgreSQL's canonical text for an empty range.
        if (rangeText.Equals("empty", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        if (rangeText.Length < 3)
        {
            throw new FormatException($"Invalid range format: {rangeText}");
        }

        var startInclusive = rangeText[0] == '[';
        var endInclusive = rangeText[^1] == ']';

        if (!startInclusive && rangeText[0] != '(')
        {
            throw new FormatException($"Range must start with '[' or '(': {rangeText}");
        }

        if (!endInclusive && rangeText[^1] != ')')
        {
            throw new FormatException($"Range must end with ']' or ')': {rangeText}");
        }

        var inner = rangeText[1..^1];
        var commaIndex = inner.IndexOf(',');

        if (commaIndex == -1)
        {
            throw new FormatException($"Range must contain comma separator: {rangeText}");
        }

        var startText = inner[..commaIndex].Trim();
        var endText = inner[(commaIndex + 1)..].Trim();

        T? start = string.IsNullOrEmpty(startText) ? default : ParseValue(startText);
        T? end = string.IsNullOrEmpty(endText) ? default : ParseValue(endText);

        return new Range<T>(start, end, startInclusive, endInclusive);
    }

    private static T ParseValue(string text)
    {
        if (typeof(T) == typeof(int))
        {
            return (T)(object)int.Parse(text, CultureInfo.InvariantCulture);
        }

        if (typeof(T) == typeof(long))
        {
            return (T)(object)long.Parse(text, CultureInfo.InvariantCulture);
        }

        if (typeof(T) == typeof(decimal))
        {
            return (T)(object)decimal.Parse(text, CultureInfo.InvariantCulture);
        }

        if (typeof(T) == typeof(double))
        {
            return (T)(object)double.Parse(text, CultureInfo.InvariantCulture);
        }

        if (typeof(T) == typeof(DateTime))
        {
            return (T)(object)DateTime.Parse(text, CultureInfo.InvariantCulture);
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            return (T)(object)DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        }

        // For other types, try TypeCoercionHelper
        return (T)TypeCoercionHelper.ConvertWithCache(text, typeof(T));
    }

    public override string ToString()
    {
        if (_isEmptyRange)
        {
            return "empty";
        }

        var lowerText = HasLowerBound
            ? Convert.ToString(Lower, CultureInfo.InvariantCulture)
            : string.Empty;
        var upperText = HasUpperBound
            ? Convert.ToString(Upper, CultureInfo.InvariantCulture)
            : string.Empty;
        var lowerBrace = IsLowerInclusive ? "[" : "(";
        var upperBrace = IsUpperInclusive ? "]" : ")";
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}, {2}{3}",
            lowerBrace,
            lowerText,
            upperText,
            upperBrace);
    }

    public bool Equals(Range<T> other)
    {
        var comparer = EqualityComparer<T?>.Default;
        return _isEmptyRange == other._isEmptyRange
               && comparer.Equals(Lower, other.Lower)
               && comparer.Equals(Upper, other.Upper)
               && IsLowerInclusive == other.IsLowerInclusive
               && IsUpperInclusive == other.IsUpperInclusive;
    }

    public override bool Equals(object? obj)
    {
        return obj is Range<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_isEmptyRange, Lower, Upper, IsLowerInclusive, IsUpperInclusive);
    }
}
