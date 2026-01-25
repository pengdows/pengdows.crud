using System.ComponentModel;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database range values and <see cref="Range{T}"/> value objects.
/// Supports PostgreSQL range types with inclusive/exclusive bounds and infinite ranges.
/// </summary>
/// <typeparam name="T">The element type of the range (int, long, DateOnly, DateTime, decimal, etc.).</typeparam>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>PostgreSQL:</strong> Maps to range types (int4range, int8range, daterange, tsrange, tstzrange, numrange). Format: "[1,10)" or "(,100]" for infinite.</description></item>
/// <item><description><strong>CockroachDB:</strong> PostgreSQL-compatible range types.</description></item>
/// <item><description><strong>Other databases:</strong> No native range types. Store as structured types or separate min/max columns.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>Range&lt;T&gt; → Range&lt;T&gt; (pass-through)</description></item>
/// <item><description>string → Range&lt;T&gt; (parses "[1,10)", "(,100]", "[5,]", "empty", etc.)</description></item>
/// <item><description>NpgsqlRange&lt;T&gt; → Range&lt;T&gt; (converts Npgsql provider-specific type via reflection)</description></item>
/// <item><description>Tuple&lt;T?, T?&gt; → Range&lt;T&gt; (simple min/max tuple)</description></item>
/// </list>
/// <para><strong>Bracket notation:</strong></para>
/// <list type="bullet">
/// <item><description>[lower,upper] - Both bounds inclusive</description></item>
/// <item><description>(lower,upper) - Both bounds exclusive</description></item>
/// <item><description>[lower,upper) - Lower inclusive, upper exclusive</description></item>
/// <item><description>(lower,upper] - Lower exclusive, upper inclusive</description></item>
/// <item><description>[lower,] - Lower bound only (upper infinite)</description></item>
/// <item><description>(,upper] - Upper bound only (lower infinite)</description></item>
/// <item><description>empty - Empty range (no values)</description></item>
/// </list>
/// <para><strong>Common PostgreSQL range types:</strong></para>
/// <list type="bullet">
/// <item><description>int4range - Range&lt;int&gt; (32-bit integer ranges)</description></item>
/// <item><description>int8range - Range&lt;long&gt; (64-bit integer ranges)</description></item>
/// <item><description>numrange - Range&lt;decimal&gt; (numeric ranges)</description></item>
/// <item><description>daterange - Range&lt;DateOnly&gt; (date ranges without time)</description></item>
/// <item><description>tsrange - Range&lt;DateTime&gt; (timestamp without timezone)</description></item>
/// <item><description>tstzrange - Range&lt;DateTimeOffset&gt; (timestamp with timezone)</description></item>
/// </list>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Range value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with range column
/// [Table("bookings")]
/// public class Booking
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("dates", DbType.Object)]
///     public Range&lt;DateOnly&gt; Dates { get; set; }
/// }
///
/// // Create with date range (inclusive start, exclusive end)
/// var booking = new Booking
/// {
///     Dates = new Range&lt;DateOnly&gt;(
///         new DateOnly(2025, 1, 1),
///         new DateOnly(2025, 1, 10),
///         lowerInclusive: true,
///         upperInclusive: false) // [2025-01-01, 2025-01-10)
/// };
/// await helper.CreateAsync(booking);
///
/// // Create with open-ended range (no upper bound)
/// var booking2 = new Booking
/// {
///     Dates = new Range&lt;DateOnly&gt;(
///         new DateOnly(2025, 6, 1),
///         null, // infinite upper bound
///         lowerInclusive: true,
///         upperInclusive: false) // [2025-06-01,)
/// };
/// await helper.CreateAsync(booking2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(booking.Id);
/// Console.WriteLine($"Has lower: {retrieved.Dates.HasLowerBound}");
/// Console.WriteLine($"Lower: {retrieved.Dates.Lower}");
/// Console.WriteLine($"Upper: {retrieved.Dates.Upper}");
/// Console.WriteLine($"Lower inclusive: {retrieved.Dates.IsLowerInclusive}");
/// </code>
/// </example>
internal sealed class PostgreSqlRangeConverter<T> : AdvancedTypeConverter<Range<T>> where T : struct
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

                var lower = lowerInfiniteProp != null && (bool)lowerInfiniteProp.GetValue(value)!
                    ? default
                    : (T?)lowerProp?.GetValue(value);
                var upper = upperInfiniteProp != null && (bool)upperInfiniteProp.GetValue(value)!
                    ? default
                    : (T?)upperProp?.GetValue(value);
                var lowerInclusive = (bool?)lowerInclusiveProp?.GetValue(value) ?? true;
                var upperInclusive = (bool?)upperInclusiveProp?.GetValue(value) ?? false;
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