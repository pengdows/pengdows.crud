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

using System.Text;

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
    /// Parse range text: PostgreSQL's canonical form ("[1,5)", "(,10]", quoted bounds such as
    /// ["2026-10-05 13:45:30","2026-10-06 00:00:00"), "empty") and <see cref="ToString"/>'s form.
    /// Each bound converts through <see cref="TypeCoercionHelper"/>. Blank text throws
    /// <see cref="ArgumentException"/>; any other text that isn't a range throws <see cref="FormatException"/>.
    /// </summary>
    public static Range<T> Parse(string rangeText)
    {
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            throw new ArgumentException("Range text cannot be null or empty", nameof(rangeText));
        }

        // The message leaves the text out: it may be a stored value (no payload in exceptions).
        return TryParseText(rangeText, out var range) ? range : throw new FormatException("The text is not a range.");
    }

    // DRY-013: the one range grammar (PostgreSqlRangeConverter reads with it too). Blank is no range.
    internal static bool TryParseText(string? text, out Range<T> range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("empty", StringComparison.OrdinalIgnoreCase))
        {
            range = Empty;
            return true;
        }

        if (trimmed.Length < 3 || trimmed[0] is not ('[' or '(') || trimmed[^1] is not (']' or ')'))
        {
            return false;
        }

        var inner = trimmed.AsSpan(1, trimmed.Length - 2);
        if (!TryReadBound(inner, out var lowerText, out var rest) || rest.Length == 0 || rest[0] != ',' ||
            !TryReadBound(rest[1..], out var upperText, out rest) || rest.Length != 0)
        {
            return false;
        }

        try
        {
            range = new Range<T>(ParseBound(lowerText), ParseBound(upperText), trimmed[0] == '[', trimmed[^1] == ']');
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    // Reads one bound up to the next top-level comma (or the end): blank is unbounded (null), a
    // quoted bound is unquoted ("" and backslash escapes, as PostgreSQL writes them).
    private static bool TryReadBound(ReadOnlySpan<char> text, out string? bound, out ReadOnlySpan<char> rest)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i < text.Length && text[i] == '"')
        {
            var value = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= text.Length)
                {
                    bound = null;
                    rest = default;
                    return false;
                }

                var c = text[i++];
                if (c == '\\' && i < text.Length)
                {
                    value.Append(text[i++]);
                }
                else if (c == '"')
                {
                    if (i < text.Length && text[i] == '"')
                    {
                        value.Append('"');
                        i++;
                        continue;
                    }

                    break;
                }
                else
                {
                    value.Append(c);
                }
            }

            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            bound = value.ToString();
            rest = text[i..];
            return rest.Length == 0 || rest[0] == ',';
        }

        var end = text[i..].IndexOf(',');
        var raw = end < 0 ? text[i..] : text.Slice(i, end);
        if (raw.IndexOfAny('"', '\\') >= 0)
        {
            bound = null;
            rest = default;
            return false;
        }

        var trimmed = raw.Trim();
        bound = trimmed.Length == 0 ? null : trimmed.ToString();
        rest = end < 0 ? ReadOnlySpan<char>.Empty : text[(i + end)..];
        return true;
    }

    private static T? ParseBound(string? text) =>
        text == null ? null : (T)TypeCoercionHelper.Coerce(text, typeof(string), typeof(T))!;

    /// <summary>
    /// PostgreSQL's canonical range text, each bound in ISO/invariant form to the tick and quoted where
    /// needed, which <see cref="TryParseText"/> reads back exactly. <see cref="ToString"/> is for display.
    /// </summary>
    internal string ToCanonicalText()
    {
        if (_isEmptyRange)
        {
            return "empty";
        }

        return string.Concat(IsLowerInclusive ? "[" : "(",
            HasLowerBound ? QuoteIfNeeded(FormatBound(Lower!.Value)) : string.Empty, ",",
            HasUpperBound ? QuoteIfNeeded(FormatBound(Upper!.Value)) : string.Empty,
            IsUpperInclusive ? "]" : ")");
    }

    private static string FormatBound(T value) => value switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture).Replace(".+", "+").Replace(".-", "-"),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string QuoteIfNeeded(string bound) =>
        bound.Length == 0 || bound.AsSpan().IndexOfAny("\"\\,()[] \t") >= 0
            ? "\"" + bound.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : bound;

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
