using System;
using System.Collections.Generic;
using System.Globalization;

namespace pengdows.crud.types.valueobjects;

public readonly struct Range<T> : IEquatable<Range<T>> where T : struct
{
    public Range(T? lower, T? upper, bool isLowerInclusive = true, bool isUpperInclusive = false)
    {
        Lower = lower;
        Upper = upper;
        IsLowerInclusive = isLowerInclusive;
        IsUpperInclusive = isUpperInclusive;
    }

    public T? Lower { get; }
    public T? Upper { get; }
    public bool IsLowerInclusive { get; }
    public bool IsUpperInclusive { get; }

    public bool HasLowerBound => Lower is not null;
    public bool HasUpperBound => Upper is not null;
    public bool IsEmpty => !HasLowerBound && !HasUpperBound;

    public static Range<T> Empty => default;

    /// <summary>
    /// Parse a canonical range string like "[1,5)" or "(,10]".
    /// </summary>
    public static Range<T> Parse(string rangeText)
    {
        if (string.IsNullOrWhiteSpace(rangeText))
            throw new ArgumentException("Range text cannot be null or empty", nameof(rangeText));

        rangeText = rangeText.Trim();

        if (rangeText.Length < 3)
            throw new FormatException($"Invalid range format: {rangeText}");

        bool startInclusive = rangeText[0] == '[';
        bool endInclusive = rangeText[^1] == ']';

        if (!startInclusive && rangeText[0] != '(')
            throw new FormatException($"Range must start with '[' or '(': {rangeText}");

        if (!endInclusive && rangeText[^1] != ')')
            throw new FormatException($"Range must end with ']' or ')': {rangeText}");

        var inner = rangeText[1..^1];
        var commaIndex = inner.IndexOf(',');

        if (commaIndex == -1)
            throw new FormatException($"Range must contain comma separator: {rangeText}");

        var startText = inner[..commaIndex].Trim();
        var endText = inner[(commaIndex + 1)..].Trim();

        T? start = string.IsNullOrEmpty(startText) ? default : ParseValue(startText);
        T? end = string.IsNullOrEmpty(endText) ? default : ParseValue(endText);

        return new Range<T>(start, end, startInclusive, endInclusive);
    }

    private static T ParseValue(string text)
    {
        if (typeof(T) == typeof(int))
            return (T)(object)int.Parse(text, CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(long))
            return (T)(object)long.Parse(text, CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(decimal))
            return (T)(object)decimal.Parse(text, CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(double))
            return (T)(object)double.Parse(text, CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(DateTime))
            return (T)(object)DateTime.Parse(text, CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(DateTimeOffset))
            return (T)(object)DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);

        // For other types, try Convert.ChangeType
        return (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
    }

    public override string ToString()
    {
        var lowerText = HasLowerBound
            ? Convert.ToString(Lower, System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var upperText = HasUpperBound
            ? Convert.ToString(Upper, System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var lowerBrace = IsLowerInclusive ? "[" : "(";
        var upperBrace = IsUpperInclusive ? "]" : ")";
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}{1}, {2}{3}",
            lowerBrace,
            lowerText,
            upperText,
            upperBrace);
    }

    public bool Equals(Range<T> other)
    {
        var comparer = EqualityComparer<T?>.Default;
        return comparer.Equals(Lower, other.Lower)
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
        return HashCode.Combine(Lower, Upper, IsLowerInclusive, IsUpperInclusive);
    }
}
