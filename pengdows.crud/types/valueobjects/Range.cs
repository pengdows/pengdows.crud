using System;
using System.Collections.Generic;

namespace pengdows.crud.types.valueobjects;

public readonly struct Range<T> : IEquatable<Range<T>>
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

    public static Range<T> Empty { get; } = new(default, default, false, false);

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
