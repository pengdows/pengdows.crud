// =============================================================================
// FILE: PostgreSqlRangeConverter.cs
// PURPOSE: Converter for PostgreSQL range types (int4range, tsrange, etc.).
//
// AI SUMMARY:
// - Generic converter for Range<T> value objects (T: int, long, DateTime, decimal, etc.).
// - Supports PostgreSQL range types with inclusive/exclusive bounds and infinite ranges.
// - Bracket notation: [lower,upper] inclusive, (lower,upper) exclusive, [lower,) open-ended.
// - Provider-specific:
//   * PostgreSQL/CockroachDB/YugabyteDB: Native range types (int4range, int8range, daterange, tsrange)
//   * Others: Raw Range<T> value
// - ConvertToProvider(): NpgsqlRange<T> for the PostgreSQL family when Npgsql is loaded
//   (Range<T>.Empty → NpgsqlRange<T>.Empty); otherwise bracket notation / "empty" text.
// - TryConvertFromProvider(): Handles Range<T>, string, NpgsqlRange<T>, Tuple<T?,T?>.
// - Text is read by Range<T>.TryParseText and written by Range<T>.ToCanonicalText (one grammar, DRY-013).
// - Common types: int4range, int8range, numrange, daterange, tsrange, tstzrange.
// - Thread-safe and immutable value objects.
// =============================================================================

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
/// <item><description>string → Range&lt;T&gt; (parses "[1,10)", "(,100]", "[5,]", etc., and the literal "empty")</description></item>
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
        if (!DatabaseTraits.For(provider).BindsNpgsqlValueTypes)
        {
            return value;
        }

        // Npgsql can't write range text into a range column (PostgreSQL rejects text there), so
        // send NpgsqlRange<T> when Npgsql is loaded, found by name since pengdows.crud takes no
        // Npgsql dependency. Range text is only the fallback.
        if (NpgsqlRangeType == null)
        {
            return value.ToCanonicalText();
        }

        if (value.IsEmptyRange)
        {
            // NpgsqlRange<T>.Empty is a static field.
            return NpgsqlEmptyRange ?? value.ToCanonicalText();
        }

        if (NpgsqlRangeCtor == null)
        {
            return value.ToCanonicalText();
        }

        return NpgsqlRangeCtor(
            ToProviderBound(value.Lower ?? default(T)),
            value.IsLowerInclusive,
            !value.HasLowerBound,
            ToProviderBound(value.Upper ?? default(T)),
            value.IsUpperInclusive,
            !value.HasUpperBound);
    }

    private static readonly Type? NpgsqlRangeType =
        Type.GetType("NpgsqlTypes.NpgsqlRange`1, Npgsql", throwOnError: false)?.MakeGenericType(typeof(T));

    // Resolved once: the constructor as a compiled delegate (no GetConstructor, Invoke or argument array per value),
    // and the static Empty member.
    private static readonly Func<T, bool, bool, T, bool, bool, object>? NpgsqlRangeCtor =
        NpgsqlRangeType == null ? null : ProviderMembers.Constructor<T, bool, bool, T, bool, bool>(NpgsqlRangeType);

    private static readonly object? NpgsqlEmptyRange = NpgsqlRangeType == null
        ? null
        : NpgsqlRangeType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
          ?? NpgsqlRangeType.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    // Accessors for a provider range type, compiled once per type. A range type whose element differs from T
    // (daterange as NpgsqlRange<DateTime>, tstzrange as UTC NpgsqlRange<DateTime>) is read boxed and coerced.
    private sealed class ProviderRange
    {
        public Func<object, bool>? IsEmpty;
        public Func<object, bool>? LowerInfinite;
        public Func<object, bool>? UpperInfinite;
        public Func<object, bool>? LowerInclusive;
        public Func<object, bool>? UpperInclusive;
        public Func<object, T>? LowerTyped;
        public Func<object, T>? UpperTyped;
        public Func<object, object?>? LowerBoxed;
        public Func<object, object?>? UpperBoxed;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ProviderRange> ProviderRanges = new();

    private static ProviderRange AccessorsFor(Type type) => ProviderRanges.GetOrAdd(type, static t =>
    {
        var elementIsT = t.IsGenericType && t.GetGenericArguments()[0] == typeof(T);
        return new ProviderRange
        {
            IsEmpty = ProviderMembers.Getter<bool>(t, "IsEmpty"),
            LowerInfinite = ProviderMembers.Getter<bool>(t, "LowerBoundInfinite"),
            UpperInfinite = ProviderMembers.Getter<bool>(t, "UpperBoundInfinite"),
            LowerInclusive = ProviderMembers.Getter<bool>(t, "LowerBoundIsInclusive"),
            UpperInclusive = ProviderMembers.Getter<bool>(t, "UpperBoundIsInclusive"),
            LowerTyped = elementIsT ? ProviderMembers.Getter<T>(t, "LowerBound") : ConvertedBound(t, "LowerBound"),
            UpperTyped = elementIsT ? ProviderMembers.Getter<T>(t, "UpperBound") : ConvertedBound(t, "UpperBound"),
            LowerBoxed = ProviderMembers.Getter<object?>(t, "LowerBound"),
            UpperBoxed = ProviderMembers.Getter<object?>(t, "UpperBound")
        };
    });

    // A bound whose element type differs from T but has a typed coercion to it (DateTime into DateTimeOffset, as
    // tstzrange needs) is converted without boxing the bound or the result. No typed coercion: null, and the bound
    // is read boxed and coerced.
    private static Func<object, T>? ConvertedBound(Type owner, string member)
    {
        var element = owner.IsGenericType ? owner.GetGenericArguments()[0] : null;
        var coercion = element == null ? null : TypedCoercions.Find(element, typeof(T));
        var property = owner.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        if (coercion == null || property == null || property.PropertyType != element)
        {
            return null;
        }

        var instance = System.Linq.Expressions.Expression.Parameter(typeof(object), "instance");
        var call = System.Linq.Expressions.Expression.Call(coercion,
            System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(instance, owner), property),
            System.Linq.Expressions.Expression.Constant(TypeCoercionOptions.Default, typeof(TypeCoercionOptions)));
        return System.Linq.Expressions.Expression.Lambda<Func<object, T>>(call, instance).Compile();
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

            // DRY-013: the one range grammar; blank or other text is no range.
            if (value is string text)
            {
                return Range<T>.TryParseText(text, out result);
            }

            var type = value.GetType();
            if (type.FullName?.StartsWith("NpgsqlTypes.NpgsqlRange", StringComparison.Ordinal) == true)
            {
                var accessors = AccessorsFor(type);
                if (accessors.IsEmpty?.Invoke(value) is true)
                {
                    result = Range<T>.Empty;
                    return true;
                }

                // Npgsql's element type can differ from the property's (daterange as NpgsqlRange<DateTime>,
                // tstzrange as UTC NpgsqlRange<DateTime>), so such a bound is coerced rather than unboxed.
                var lower = accessors.LowerInfinite?.Invoke(value) is true
                    ? default
                    : accessors.LowerTyped != null ? accessors.LowerTyped(value) : CoerceBound(accessors.LowerBoxed?.Invoke(value));
                var upper = accessors.UpperInfinite?.Invoke(value) is true
                    ? default
                    : accessors.UpperTyped != null ? accessors.UpperTyped(value) : CoerceBound(accessors.UpperBoxed?.Invoke(value));
                var lowerInclusive = accessors.LowerInclusive?.Invoke(value) ?? true;
                var upperInclusive = accessors.UpperInclusive?.Invoke(value) ?? false;
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

    private static T? CoerceBound(object? bound)
    {
        return bound switch
        {
            null => null,
            T typed => typed,
            _ => (T?)TypeCoercionHelper.Coerce(bound, bound.GetType(), typeof(T))
        };
    }

    // Npgsql writes a DateTimeOffset to timestamptz only at offset 0.
    private static T ToProviderBound(T bound)
    {
        return bound is DateTimeOffset dto ? (T)(object)dto.ToUniversalTime() : bound;
    }
}