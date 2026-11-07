// =============================================================================
// FILE: TypeCoercionHelper.cs
// PURPOSE: Static utilities for converting between .NET types and database
//          types, handling nulls, enums, JSON, dates, and provider quirks.
//
// AI SUMMARY:
// - Central type conversion logic used when reading from DataReader.
// - Coerce() method handles:
//   * Null/DBNull detection and propagation
//   * Enum parsing (from string or numeric, with configurable failure mode)
//   * JSON deserialization (from string or JsonDocument to typed objects)
//   * Date/time conversions (DateOnly, TimeOnly, DateTimeOffset)
//   * Guid from byte[] (for providers that return Guid as 16-byte array)
//   * Numeric type conversions (respecting precision/overflow)
// - pengdows.crud.types.coercion.ConverterResolution.ResolveCoercer() — plan-build-time factory that returns a single delegate
//   for a given column.  Dispatches once to the correct path (enum, JSON,
//   DateTime, DateTimeOffset, registered IDbCoercion, or full Coerce fallback)
//   so that subsequent per-row calls skip all dispatch logic.
// - GetJsonText() serializes objects to JSON strings for storage.
// - Handles provider-specific quirks:
//   * Some return TimeSpan as string
//   * Some return Guid as byte[]
//   * Some return DateTimeOffset as DateTime
// - Configurable via TypeCoercionOptions for fine-tuning behavior.
// - Logger property allows capturing coercion warnings/errors.
// - Performance: Fast path for assignable types, no conversion needed.
// =============================================================================

#region

using pengdows.crud.types.coercion;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.types;
using pengdows.crud.types.valueobjects;

#endregion

namespace pengdows.crud;

/// <summary>
/// Provides static utilities for type coercion between database and .NET types.
/// </summary>
/// <remarks>
/// <para>
/// This helper is used by <see cref="DataReaderMapper"/> and <see cref="TableGateway{TEntity,TRowID}"/>
/// to convert values read from the database to the appropriate .NET types.
/// </para>
/// <para>
/// <strong>Null Handling:</strong> Both null and <see cref="DBNull.Value"/> are converted to null.
/// </para>
/// <para>
/// <strong>Enum Handling:</strong> Enums can be stored as strings or numeric values.
/// The <see cref="EnumParseFailureMode"/> parameter controls behavior when parsing fails.
/// </para>
/// <para>
/// <strong>JSON Handling:</strong> Columns marked with <see cref="attributes.JsonAttribute"/>
/// are deserialized from JSON strings or <see cref="JsonDocument"/> to the target type.
/// </para>
/// </remarks>
/// <seealso cref="TypeCoercionOptions"/>
/// <seealso cref="EnumParseFailureMode"/>
internal static class TypeCoercionHelper
{




    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    /// <summary>
    /// Adopts <paramref name="logger"/> only if no logger has been set yet, atomically: the first
    /// <see cref="DatabaseContext"/> to initialize wins for every other context in the process too.
    /// A check-then-set here raced when contexts are created concurrently (e.g.
    /// <c>Task.WhenAll</c> over <c>DatabaseContext.CreateAsync</c>, BP-311).
    /// </summary>
    internal static void SetLoggerIfUnset(ILogger logger) => SetIfUnset(ref _logger, logger);

    /// <summary>The atomic adopt-if-unset step, on any field (tests use their own, not the global).</summary>
    internal static void SetIfUnset(ref ILogger field, ILogger logger)
    {
        System.Threading.Interlocked.CompareExchange(ref field, logger, NullLogger.Instance);
    }



    public static object? Coerce(
        object? value,
        Type sourceType,
        Type targetType,
        TypeCoercionOptions? options = null)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        options ??= TypeCoercionOptions.Default;

        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Don't take fast path for DateTime types as they may need UTC conversion
        if (targetType.IsAssignableFrom(sourceType) && underlyingTarget != typeof(DateTime) &&
            underlyingTarget != typeof(DateTimeOffset))
        {
            return value;
        }

        if (EnumCoercion.TryGetEnumType(targetType, out var enumType))
        {
            return EnumCoercion.CoerceEnum(value!, enumType, EnumParseFailureMode.Throw, targetType);
        }

        return CoerceCore(value!, sourceType, targetType, options);
    }

    private static object? CoerceCore(object value, Type sourceType, Type targetType, TypeCoercionOptions options)
    {
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // TYPE-003: text into a char is exactly one character (a space included) or a failure; it
        // never falls through to a null the caller would unbox.
        if (underlyingTarget == typeof(char) && value is string charText)
        {
            return charText.Length == 1
                ? charText[0]
                : throw new FormatException(
                    $"A char needs exactly one character; the stored text has {charText.Length}.");
        }

        // COR-002: blank text is not a number, Guid, date or flag. It used to read as
        // 0/Guid.Empty/default/false with no error (the silent wrong value TYPE-008 forbids), for
        // nullable targets too; an empty string is not NULL.
        if (value is string s && string.IsNullOrWhiteSpace(s) && underlyingTarget != typeof(string) &&
            underlyingTarget != typeof(object))
        {
            // COR-007: a JSON type reads blank text as the JSON literal null, as a [Json] column does.
            if (JsonCoercion.IsJsonValueType(underlyingTarget))
            {
                return JsonCoercion.CoerceJsonValue(s, targetType, options);
            }

            throw BlankText(underlyingTarget);
        }

        // Don't take fast path for DateTime types as they may need UTC conversion
        if (underlyingTarget.IsInstanceOfType(value) && underlyingTarget != typeof(DateTime) &&
            underlyingTarget != typeof(DateTimeOffset))
        {
            return value;
        }

        // TYPE-005: DuckDB.NET and FirebirdClient read HUGEINT/UHUGEINT/INT128 as BigInteger, which
        // isn't IConvertible. Wide integers convert with checked casts: exact or OverflowException.
        if (pengdows.crud.types.coercion.WideIntegerCoercion.TryCoerceWideInteger(value, underlyingTarget, out var wide))
        {
            return wide;
        }

        // Policy-aware type handling (DateTime/DateTimeOffset require policy context)
        if (underlyingTarget == typeof(DateTimeOffset))
        {
            return DateTimeCoercion.CoerceDateTimeOffset(value, options);
        }

        if (underlyingTarget == typeof(DateTime))
        {
            return DateTimeCoercion.CoerceDateTime(value, options);
        }

        if (underlyingTarget == typeof(Guid) && pengdows.crud.types.coercion.BinaryCoercion.TryDecodeGuidBytes(value, options.GuidBytesBigEndian, out var guidValue))
        {
            return guidValue;
        }

        // A Guid the driver returned for a 16-byte column reads back as those bytes, in the dialect's byte order.
        if (underlyingTarget == typeof(byte[]) && value is Guid guidSource)
        {
            return pengdows.crud.types.coercion.BinaryCoercion.GuidBytes(guidSource, options.GuidBytesBigEndian);
        }

        // Primary path: Use unified CoercionRegistry system for other types
        var dbValue = new types.coercion.DbValue(value, sourceType);
        if (types.coercion.CoercionRegistry.Shared.TryRead(dbValue, underlyingTarget, out var coercedValue,
                options.Provider))
        {
            return coercedValue;
        }

        // Fallback: char[] to string conversion (not in coercion registry)
        if (underlyingTarget == typeof(string) && TryFormatCanonicalText(value, out var canonical))
        {
            return canonical;
        }

        if (underlyingTarget == typeof(string) && sourceType == typeof(char[]))
        {
            return new string((char[])value);
        }

        // TYPE-016: a hierarchyid read as HierarchyId into a string property gets its text form.
        if (underlyingTarget == typeof(string) && value is HierarchyId hierarchyId)
        {
            return hierarchyId.ToString();
        }

        // Legacy path: Try advanced converter for backward compatibility
        var advancedConverter = AdvancedTypeRegistry.Shared.GetConverter(underlyingTarget);
        if (advancedConverter != null)
        {
            var converted = advancedConverter.FromProviderValue(value, options.Provider);
            if (converted != null)
            {
                return converted;
            }
        }

        // TYPE-005: DuckDB.NET reads a LIST as List<T>; an array or list property gets its elements
        // coerced one by one.
        if (SequenceCoercion.TryCoerceSequence(value, underlyingTarget, options, out var sequence))
        {
            return sequence;
        }

        // A fractional value into an integer must be whole (COR-009): Convert.ChangeType rounded it to
        // even (2.7 → 3) where the gateway truncated.
        WholeNumber.Check(value, underlyingTarget);
        NumericTruth.Check(value, underlyingTarget);


        // Npgsql's own value types for a native PostgreSQL column (a point, a tsquery, a log sequence number) are not
        // IConvertible: their text is the column's value.
        if (underlyingTarget == typeof(string) && ProviderNativeText.TryText(value, out var nativeText))
        {
            return nativeText;
        }

        // Final fallback: Use cached compiled converter for better performance
        try
        {
            return pengdows.crud.types.coercion.ConverterResolution.ConvertWithCache(value, underlyingTarget);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value of type {sourceType} to {targetType}.", ex);
        }
    }

    internal static FormatException BlankText(Type target) => new($"Blank text can't be read as {target.Name}.");







    internal static void TryLogWarning(string message, params object?[] args)
    {
        try
        {
            Logger.LogWarning(message, args);
        }
        catch
        {
            // Swallow logging failures to avoid breaking enum parse fallbacks.
        }
    }

    internal static void TryLogDebug(string message, params object?[] args)
    {
        try
        {
            Logger.LogDebug(message, args);
        }
        catch
        {
            // Ignore logging failures in debug-only fallbacks.
        }
    }

    internal static bool TryFormatCanonicalText(object value, out string text)
    {
        text = value switch
        {
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString(time.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH:mm:ss" : "HH:mm:ss.FFFFFFF",
                CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            _ => null!
        };
        return text != null;
    }







}

