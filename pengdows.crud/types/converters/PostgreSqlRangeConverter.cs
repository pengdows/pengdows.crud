using System;
using System.ComponentModel;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

internal sealed class PostgreSqlRangeConverter<T> : AdvancedTypeConverter<Range<T>>
{
    protected override object? ConvertToProvider(Range<T> value, SupportedDatabase provider)
    {
        if (provider != SupportedDatabase.PostgreSql && provider != SupportedDatabase.CockroachDb)
        {
            return value;
        }

        return FormatRange(value);
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Range<T> result)
    {
        try
        {
            if (value is Range<T> existing)
            {
                result = existing;
                return true;
            }

            if (value is string text)
            {
                result = Parse(text);
                return true;
            }

            var type = value.GetType();
            if (type.FullName?.StartsWith("NpgsqlTypes.NpgsqlRange", StringComparison.Ordinal) == true)
            {
                var lowerProp = type.GetProperty("LowerBound");
                var upperProp = type.GetProperty("UpperBound");
                var lowerInclusiveProp = type.GetProperty("LowerBoundIsInclusive");
                var upperInclusiveProp = type.GetProperty("UpperBoundIsInclusive");
                var lowerInfiniteProp = type.GetProperty("LowerBoundInfinite");
                var upperInfiniteProp = type.GetProperty("UpperBoundInfinite");

                var lower = lowerInfiniteProp != null && (bool)lowerInfiniteProp.GetValue(value)! ? default : (T?)lowerProp?.GetValue(value);
                var upper = upperInfiniteProp != null && (bool)upperInfiniteProp.GetValue(value)! ? default : (T?)upperProp?.GetValue(value);
                var lowerInclusive = (bool?)(lowerInclusiveProp?.GetValue(value)) ?? true;
                var upperInclusive = (bool?)(upperInclusiveProp?.GetValue(value)) ?? false;
                result = new Range<T>(lower, upper, lowerInclusive, upperInclusive);
                return true;
            }

            if (value is Tuple<T?, T?> tuple)
            {
                result = new Range<T>(tuple.Item1, tuple.Item2);
                return true;
            }

            result = default!;
            return false;
        }
        catch
        {
            result = default!;
            return false;
        }
    }

    private static string FormatRange(Range<T> range)
    {
        var lowerBrace = range.IsLowerInclusive ? '[' : '(';
        var upperBrace = range.IsUpperInclusive ? ']' : ')';
        var lower = range.HasLowerBound ? FormatValue(range.Lower) : string.Empty;
        var upper = range.HasUpperBound ? FormatValue(range.Upper) : string.Empty;
        return string.Concat(
            lowerBrace.ToString(),
            lower,
            ",",
            upper,
            upperBrace.ToString());
    }

    private static Range<T> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
        {
            return Range<T>.Empty;
        }

        var lowerInclusive = text[0] == '[';
        var upperInclusive = text[^1] == ']';
        var inner = text.Substring(1, text.Length - 2);
        var parts = inner.Split(',', 2);

        var lower = ParseValue(parts[0]);
        var upper = parts.Length > 1 ? ParseValue(parts[1]) : default;

        return new Range<T>(lower, upper, lowerInclusive, upperInclusive);
    }

    private static string FormatValue(T? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static T? ParseValue(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return default;
        }

        var converter = TypeDescriptor.GetConverter(typeof(T));
        return (T?)converter.ConvertFromString(null, CultureInfo.InvariantCulture, text);
    }
}
