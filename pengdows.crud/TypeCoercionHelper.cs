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
// - ResolveCoercer() — plan-build-time factory that returns a single delegate
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

    /// <summary>
    /// Cache of compiled type conversion delegates for faster Convert.ChangeType operations.
    /// Key: (sourceType, targetType), Value: compiled converter function.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type source, Type target), Func<object, object>> _conversionCache =
        new();

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

    /// <summary>
    /// Converts a value to the specified target type using a cached compiled delegate.
    /// This is significantly faster than Convert.ChangeType for repeated conversions.
    /// </summary>
    /// <param name="value">The value to convert (must not be null).</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="InvalidCastException">Thrown when the conversion fails.</exception>
    public static object ConvertWithCache(object value, Type targetType)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value), "Value cannot be null for type conversion");
        }

        var sourceType = value.GetType();
        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Fast path: already correct type
        if (sourceType == actualTarget)
        {
            return value;
        }

        // Get or create compiled converter
        var converter = _conversionCache.GetOrAdd(
            (sourceType, actualTarget),
            static key => CompileConverter(key.source, key.target)
        );

        try
        {
            return converter(value);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert value from {sourceType.Name} to {actualTarget.Name}.",
                ex);
        }
    }

    /// <summary>
    /// Compiles a fast type converter delegate using expression trees.
    /// </summary>
    private static Func<object, object> CompileConverter(Type sourceType, Type targetType)
    {
        // Parameter: object value
        var param = Expression.Parameter(typeof(object), "value");

        // Cast input: (TSource)value
        var typedInput = Expression.Convert(param, sourceType);

        Expression conversion;

        // Special cases that Convert.ChangeType doesn't handle
        if (sourceType == typeof(string) && targetType == typeof(Guid))
        {
            // Guid.Parse(stringValue)
            var parseMethod = typeof(Guid).GetMethod(nameof(Guid.Parse), new[] { typeof(string) })!;
            conversion = Expression.Call(parseMethod, typedInput);
        }
        else if (sourceType == typeof(byte[]) && targetType == typeof(Guid))
        {
            // new Guid(bytes) — MySQL and similar providers store UUIDs as BINARY(16)
            var guidCtor = typeof(Guid).GetConstructor(new[] { typeof(byte[]) })!;
            conversion = Expression.New(guidCtor, typedInput);
        }
        else if (sourceType == typeof(DateTime) && targetType == typeof(DateTimeOffset))
        {
            // new DateTimeOffset(dateTimeValue)
            var constructor = typeof(DateTimeOffset).GetConstructor(new[] { typeof(DateTime) })!;
            conversion = Expression.New(constructor, typedInput);
        }
        else if (sourceType == typeof(DateTimeOffset) && targetType == typeof(DateTime))
        {
            // dateTimeOffsetValue.DateTime
            var property = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.DateTime))!;
            conversion = Expression.Property(typedInput, property);
        }
        else
        {
            // Use ChangeType method with InvariantCulture for standard conversions
            var changeTypeMethod = typeof(Convert).GetMethod(
                nameof(Convert.ChangeType),
                new[] { typeof(object), typeof(Type), typeof(IFormatProvider) })!;

            conversion = Expression.Call(
                changeTypeMethod,
                Expression.Convert(typedInput, typeof(object)),
                Expression.Constant(targetType),
                Expression.Constant(CultureInfo.InvariantCulture)
            );
        }

        // Cast result: (object)result
        var boxedResult = Expression.Convert(conversion, typeof(object));

        // Compile the lambda
        var lambda = Expression.Lambda<Func<object, object>>(boxedResult, param);
        return lambda.Compile();
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

        if (TryGetEnumType(targetType, out var enumType))
        {
            return CoerceEnum(value!, enumType, EnumParseFailureMode.Throw, targetType);
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
            if (IsJsonValueType(underlyingTarget))
            {
                return CoerceJsonValue(s, targetType, options);
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
        if (TryCoerceWideInteger(value, underlyingTarget, out var wide))
        {
            return wide;
        }

        // Policy-aware type handling (DateTime/DateTimeOffset require policy context)
        if (underlyingTarget == typeof(DateTimeOffset))
        {
            return CoerceDateTimeOffset(value, options);
        }

        if (underlyingTarget == typeof(DateTime))
        {
            return CoerceDateTime(value, options);
        }

        if (underlyingTarget == typeof(Guid) && TryDecodeGuidBytes(value, options.GuidBytesBigEndian, out var guidValue))
        {
            return guidValue;
        }

        // Primary path: Use unified CoercionRegistry system for other types
        var dbValue = new types.coercion.DbValue(value, sourceType);
        if (types.coercion.CoercionRegistry.Shared.TryRead(dbValue, underlyingTarget, out var coercedValue,
                options.Provider))
        {
            return coercedValue;
        }

        // Fallback: char[] to string conversion (not in coercion registry)
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
        if (TryCoerceSequence(value, underlyingTarget, options, out var sequence))
        {
            return sequence;
        }

        // A fractional value into an integer must be whole (COR-009): Convert.ChangeType rounded it to
        // even (2.7 → 3) where the gateway truncated.
        WholeNumber.Check(value, underlyingTarget);
        NumericTruth.Check(value, underlyingTarget);

        // Final fallback: Use cached compiled converter for better performance
        try
        {
            return ConvertWithCache(value, underlyingTarget);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value of type {sourceType} to {targetType}.", ex);
        }
    }

    internal static FormatException BlankText(Type target) => new($"Blank text can't be read as {target.Name}.");

    /// <summary>A Guid from its 16 stored bytes in the given byte order.</summary>
    public static Guid GuidFromBytes(byte[] bytes, bool bigEndian) => new(bytes, bigEndian);

    // A 16-byte value read into a Guid is decoded in the dialect's Guid byte order (TYPE-002);
    // the registered Guid coercion only knows .NET's mixed-endian order.
    private static bool TryDecodeGuidBytes(object value, bool bigEndian, out object guid)
    {
        switch (value)
        {
            case byte[] { Length: 16 } bytes:
                guid = new Guid(bytes, bigEndian);
                return true;
            case ReadOnlyMemory<byte> { Length: 16 } memory:
                guid = new Guid(memory.Span, bigEndian);
                return true;
            case ArraySegment<byte> { Count: 16 } segment:
                guid = new Guid(segment.AsSpan(), bigEndian);
                return true;
            default:
                guid = null!;
                return false;
        }
    }

    // A registered coercion (e.g. decimal's) that declines a value falls back here. Wide integers
    // (BigInteger, Int128, UInt128) aren't IConvertible, so they take the checked TYPE-005 casts:
    // exact or OverflowException, as through Coerce.
    // A sequence (DuckDB.NET's List<T> for a LIST column) takes the element-by-element conversion
    // too (TYPE-002).
    private static object ConvertRegisteredFallback(object value, Type target, TypeCoercionOptions? options = null)
    {
        if (TryCoerceWideInteger(value, target, out var wide))
        {
            return wide!;
        }

        if (TryCoerceSequence(value, target, options ?? TypeCoercionOptions.Default, out var sequence))
        {
            return sequence!;
        }

        return ConvertWithCache(value, target);
    }

    private static bool IsWideInteger(Type type) =>
        type == typeof(BigInteger) || type == typeof(Int128) || type == typeof(UInt128);

    private static bool TryCoerceWideInteger(object value, Type target, out object? result)
    {
        result = null;
        if (!IsWideInteger(value.GetType()) && !IsWideInteger(target))
        {
            return false;
        }

        BigInteger source;
        switch (value)
        {
            case BigInteger big: source = big; break;
            case Int128 i128: source = i128; break;
            case UInt128 u128: source = u128; break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                source = new BigInteger(Convert.ToDecimal(value, CultureInfo.InvariantCulture)); break;
            case decimal d when decimal.Truncate(d) == d: source = new BigInteger(d); break;
            case string text: source = BigInteger.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
            default: return false;
        }

        result = Type.GetTypeCode(target) switch
        {
            TypeCode.SByte => (object)(sbyte)source,
            TypeCode.Byte => (byte)source,
            TypeCode.Int16 => (short)source,
            TypeCode.UInt16 => (ushort)source,
            TypeCode.Int32 => (int)source,
            TypeCode.UInt32 => (uint)source,
            TypeCode.Int64 => (long)source,
            TypeCode.UInt64 => (ulong)source,
            TypeCode.Decimal => (decimal)source,
            // (double)BigInteger truncates; parsing the exact digits rounds to nearest, like
            // (double)decimal does.
            TypeCode.Double => double.Parse(source.ToString(CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            TypeCode.String => source.ToString(CultureInfo.InvariantCulture),
            _ when target == typeof(BigInteger) => source,
            _ when target == typeof(Int128) => (Int128)source,
            _ when target == typeof(UInt128) => (UInt128)source,
            _ => null
        };
        return result != null;
    }

    private static bool TryCoerceSequence(object value, Type target, TypeCoercionOptions options, out object? result)
    {
        result = null;
        Type? elementType = target.IsArray ? target.GetElementType()
            : target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>) ? target.GetGenericArguments()[0]
            : null;
        if (elementType == null)
        {
            return false;
        }

        // TYPE-015: vector columns. SQL Server's VECTOR reads as "[1.5000000e+000,...]" through
        // SqlClient 6.0 and as SqlVector<T> (Memory) through 6.1+; the Pgvector.Npgsql plugin reads
        // pgvector as Pgvector.Vector (ToArray()). Each becomes an array, coerced element-wise below.
        if (value is string literal && options.ReadsCollectionLiterals && target.IsArray)
        {
            // The dialect returns collections as literal text (Informix LIST/SET/MULTISET).
            try
            {
                result = CollectionLiteralParse.MakeGenericMethod(elementType).Invoke(null, new object[] { literal });
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Surface the parser's own FormatException, not the reflection wrapper.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            return true;
        }

        if (value is string text)
        {
            if (!NumericTypes.IsNumeric(Nullable.GetUnderlyingType(elementType) ?? elementType))
            {
                return false;
            }

            value = ParseNumericJsonArray(text);
        }
        else if (TryUnwrapProviderVector(value, out var unwrapped))
        {
            value = unwrapped;
        }

        if (value is not System.Collections.IEnumerable items)
        {
            return false;
        }

        result = SequenceBuilders.GetOrAdd(elementType, CreateSequenceBuilder)(items, options, target.IsArray);
        return true;
    }

    // PERF-011: one typed builder per element type. The general path built a List through Activator,
    // added boxed elements through IList, copied the list into an array and coerced elements already
    // of the target type (a 1536-dimension embedding: about 240 us and 133 KB).
    private delegate object SequenceBuilder(System.Collections.IEnumerable items, TypeCoercionOptions options, bool asArray);

    private static readonly ConcurrentDictionary<Type, SequenceBuilder> SequenceBuilders = new();

    private static SequenceBuilder CreateSequenceBuilder(Type elementType) =>
        (SequenceBuilder)typeof(TypeCoercionHelper)
            .GetMethod(nameof(BuildSequence), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(elementType)
            .CreateDelegate(typeof(SequenceBuilder));

    private static object BuildSequence<T>(System.Collections.IEnumerable items, TypeCoercionOptions options, bool asArray)
    {
        // Elements already of the target type are what Coerce would return, except DateTime and
        // DateTimeOffset (normalized) and object (DBNull becomes null).
        var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var copyAsIs = items is IEnumerable<T> && typeof(T) != typeof(object) &&
                       underlying != typeof(DateTime) && underlying != typeof(DateTimeOffset);

        if (asArray && items is System.Collections.ICollection collection)
        {
            var array = new T[collection.Count];
            var i = 0;
            if (copyAsIs)
            {
                foreach (var item in (IEnumerable<T>)items)
                {
                    array[i++] = item;
                }
            }
            else
            {
                foreach (var item in items)
                {
                    array[i++] = CoerceSequenceElement<T>(item, options);
                }
            }

            return array;
        }

        var list = items is System.Collections.ICollection sized ? new List<T>(sized.Count) : new List<T>();
        if (copyAsIs)
        {
            list.AddRange((IEnumerable<T>)items);
        }
        else
        {
            foreach (var item in items)
            {
                list.Add(CoerceSequenceElement<T>(item, options));
            }
        }

        return asArray ? list.ToArray() : list;
    }

    private static T CoerceSequenceElement<T>(object? item, TypeCoercionOptions options)
    {
        if (item is null || item is DBNull)
        {
            // As IList.Add(null) on a List of a non-nullable value type did.
            return default(T) is null ? default! : throw new ArgumentNullException(nameof(item));
        }

        return (T)Coerce(item, item.GetType(), typeof(T), options)!;
    }

    // Numbers kept as their invariant text, so each element parses straight into its target type
    // (a float gets the nearest float to the printed value, not a double rounded again).
    private static readonly System.Reflection.MethodInfo CollectionLiteralParse =
        typeof(CollectionLiteralFormat).GetMethod(nameof(CollectionLiteralFormat.Parse))!;

    private static string[] ParseNumericJsonArray(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            // A conversion failure like any other, so hydration reports DataMappingException (REV-034).
            throw new FormatException("Expected a JSON array of numbers.", ex);
        }

        using var _ = document;
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Expected a JSON array of numbers.");
        }

        var elements = new string[document.RootElement.GetArrayLength()];
        var i = 0;
        foreach (var element in document.RootElement.EnumerateArray())
        {
            elements[i++] = element.ValueKind == JsonValueKind.Number
                ? element.GetRawText()
                : throw new FormatException($"Expected a number but found {element.ValueKind}.");
        }

        return elements;
    }

    private static bool TryUnwrapProviderVector(object value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? array)
    {
        array = null;
        var type = value.GetType();
        object? memory = null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(ReadOnlyMemory<>) || definition == typeof(Memory<>)))
        {
            memory = value;
        }
        else if (type.Name == "SqlVector`1")
        {
            memory = type.GetProperty("Memory")?.GetValue(value);
        }
        else if (type.FullName is "Pgvector.Vector")
        {
            array = type.GetMethod("ToArray", Type.EmptyTypes)?.Invoke(value, null);
            return array != null;
        }

        array = memory?.GetType().GetMethod("ToArray", Type.EmptyTypes)?.Invoke(memory, null);
        return array != null;
    }

    private static object? CoerceEnum(object value, Type enumType, EnumParseFailureMode parseMode, Type targetType)
    {
        if (enumType.IsInstanceOfType(value))
        {
            return value;
        }

        var isNullable = Nullable.GetUnderlyingType(targetType) != null;
        var stringValue = value as string ?? (value is char c ? c.ToString() : null);

        if (!string.IsNullOrEmpty(stringValue))
        {
            if (ColumnInfo.TryParseEnumLiteral(enumType, stringValue, out var literalResult))
            {
                return literalResult!;
            }

            // Validated like a number: Enum.TryParse accepts "99" as an undefined value (DRY-003).
            if (Enum.TryParse(enumType, stringValue, true, out var parsed) && EnumMappingCache.IsValid(enumType, parsed!))
            {
                return parsed!;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }

        try
        {
            // A fractional number is no member: Convert.ToInt64 rounded 2.7 to 3 (COR-009).
            WholeNumber.Check(value, typeof(long));
            // An integer goes in as itself (Convert.ToInt64 overflowed a ulong above long.MaxValue,
            // REV-070); a whole decimal or floating value by its sign.
            var result = value is decimal or double or float
                ? NumericEnumValue(enumType, value)
                : Enum.ToObject(enumType, value);
            // Defined, or a combination of [Flags] members, as on the gateway (DRY-003).
            if (EnumMappingCache.IsValid(enumType, result))
            {
                return result;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
        catch
        {
            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
    }

    private static object NumericEnumValue(Type enumType, object wholeValue)
    {
        if (Convert.ToDecimal(wholeValue, CultureInfo.InvariantCulture) >= 0)
        {
            return Enum.ToObject(enumType, Convert.ToUInt64(wholeValue, CultureInfo.InvariantCulture));
        }

        return Enum.ToObject(enumType, Convert.ToInt64(wholeValue, CultureInfo.InvariantCulture));
    }

    private static object DefaultOf(Type enumType) =>
        Enum.ToObject(enumType, Activator.CreateInstance(Enum.GetUnderlyingType(enumType))!);

    /// <summary>The warning <see cref="EnumParseFailureMode.SetNullAndLog"/> logs on every read path.</summary>
    internal static void LogEnumFailure(Type enumType) =>
        TryLogWarning("Cannot convert value to enum {EnumType}.", enumType);

    private static object? HandleEnumFailure(object _, Type enumType, EnumParseFailureMode parseMode,
        bool targetNullable)
    {
        switch (parseMode)
        {
            case EnumParseFailureMode.Throw:
                throw new EnumValueException($"Cannot convert value to enum {enumType}");
            case EnumParseFailureMode.SetDefaultValue:
                return targetNullable ? null : DefaultOf(enumType);
            case EnumParseFailureMode.SetNullAndLog:
                // Null where the property can hold it; a non-nullable enum gets its default, as on the
                // gateway (null failed to unbox, DRY-007).
                LogEnumFailure(enumType);
                return targetNullable ? null : DefaultOf(enumType);
            default:
                return null;
        }
    }

    private static void TryLogWarning(string message, params object?[] args)
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

    private static void TryLogDebug(string message, params object?[] args)
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

    private static bool EvaluateCharBoolean(char lower)
    {
        switch (lower)
        {
            case 't':
            case 'y':
            case '1':
                return true;
            case 'f':
            case 'n':
            case '0':
                return false;
            default:
                throw new InvalidCastException("Cannot convert character to Boolean.");
        }
    }

    private static object CoerceDateTimeOffset(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case not null when FirebirdZonedDateTimeInterop.TryGetInstant(value, out var zoned):
                // Firebird TIMESTAMP WITH TIME ZONE columns are returned as FbZonedDateTime.
                return zoned;
            case not null when FirebirdZonedDateTimeInterop.TryGetTime(value, out var zonedTime):
                // Firebird TIME WITH TIME ZONE columns are returned as FbZonedTime (TYPE-002).
                return zonedTime;
            case DateTime dt:
                return DateTimeOffsetFromDateTime(dt, options);
            case string s when TryParseTimestampText(s, out var parsed):
                return parsed;
            default:
                throw new InvalidCastException("Cannot convert value to DateTimeOffset.");
        }
    }

    private static object CoerceDateTime(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTime dt:
                return DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
            case DateTimeOffset dto:
                return DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
            case not null when FirebirdZonedDateTimeInterop.TryGetInstant(value, out var zoned):
                // Firebird TIMESTAMP WITH TIME ZONE columns are returned as FbZonedDateTime.
                return DateTime.SpecifyKind(zoned.UtcDateTime, DateTimeKind.Utc);
            case string s when string.IsNullOrWhiteSpace(s):
                // Treat empty/whitespace strings as invalid for DateTime
                // This handles SQLite returning empty strings for TIMESTAMP columns
                throw new InvalidCastException("Cannot convert value to DateTime.");
            case string s when TryParseTimestampText(s, out var dto):
                return dto.UtcDateTime;
            default:
                throw new InvalidCastException("Cannot convert value to DateTime.");
        }
    }

    /// <summary>
    /// Typed DateTime parser for string source that avoids boxing the DateTime return value.
    /// Applies the same UTC normalization as <see cref="CoerceDateTime"/> for the string case.
    /// (<see cref="DataReaderMapper"/>'s compiled string→DateTime path uses
    /// <c>IDataRecord.GetDateTime</c> + <see cref="NormalizeDateTime"/> instead.)
    /// </summary>
    internal static DateTime CoerceDateTimeFromString(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new InvalidCastException("Cannot convert value to DateTime.");
        }

        if (TryParseTimestampText(s, out var dto))
        {
            return dto.UtcDateTime;
        }

        throw new InvalidCastException("Cannot convert value to DateTime.");
    }

    /// <summary>
    /// Typed DateTimeOffset parser for string source — used by compiled expression trees in
    /// <see cref="DataReaderMapper"/> to avoid boxing the DateTimeOffset return value.
    /// </summary>
    internal static DateTimeOffset CoerceDateTimeOffsetFromString(string s)
    {
        if (TryParseTimestampText(s, out var parsed))
        {
            return parsed;
        }

        throw new InvalidCastException("Cannot convert value to DateTimeOffset.");
    }

    /// <summary>
    /// A DateTime as a DateTimeOffset under <paramref name="options"/>' time policy, as
    /// <see cref="CoerceDateTimeOffset"/> converts it, without boxing (DRY-003).
    /// </summary>
    internal static DateTimeOffset DateTimeOffsetFromDateTime(DateTime dt, TypeCoercionOptions options) =>
        options.TimePolicy == TimeMappingPolicy.ForceUtcDateTime
            ? new DateTimeOffset(ConvertToUtc(dt), TimeSpan.Zero)
            : CreateFlexibleOffset(dt);

    /// <summary>
    /// Timestamp text: a stated offset keeps its instant; text without one is UTC, as an unspecified
    /// DateTime is everywhere else. DateTimeOffset.TryParse alone assumed the machine's local time,
    /// so the same text read differently on differently configured hosts (REV-059). One parse,
    /// where the old path tried DateTimeOffset then DateTime.
    /// </summary>
    private static bool TryParseTimestampText(string s, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);

    /// <summary>
    /// Normalizes a DateTime returned by a database driver to DateTimeKind.Utc.
    /// Many drivers (Snowflake, SQL Server, MySQL, Oracle) return TIMESTAMP/DATETIME columns
    /// as DateTimeKind.Unspecified. Treating Unspecified as UTC matches the convention that
    /// all datetime values stored in the database represent UTC instants.
    /// </summary>
    internal static DateTime NormalizeDateTime(DateTime dt) => ConvertToUtc(dt);

    private static DateTime ConvertToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    private static DateTimeOffset CreateFlexibleOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        if (dt.Kind == DateTimeKind.Local)
        {
            return new DateTimeOffset(dt);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero);
    }

    private static bool TryGetEnumType(Type targetType, out Type enumType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsEnum)
        {
            enumType = underlying;
            return true;
        }

        enumType = underlying;
        return false;
    }

    private static object? CoerceJsonValue(
        object value,
        Type targetType,
        TypeCoercionOptions options)
    {
        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget.IsInstanceOfType(value))
        {
            return value;
        }

        var serializerOptions = JsonSerializerOptions.Default;

        if (actualTarget == typeof(string) || actualTarget == typeof(ReadOnlyMemory<char>))
        {
            var text = ExtractJsonString(value, serializerOptions);
            return actualTarget == typeof(string) ? text : new ReadOnlyMemory<char>(text.ToCharArray());
        }

        if (actualTarget == typeof(JsonDocument))
        {
            return ToJsonDocument(value, serializerOptions);
        }

        if (actualTarget == typeof(JsonElement))
        {
            using var doc = ToJsonDocument(value, serializerOptions);
            return doc.RootElement.Clone();
        }

        if (actualTarget == typeof(types.valueobjects.JsonValue))
        {
            // The JSON text itself, never JsonSerializer.Deserialize into the struct (TYPE-019).
            return new types.valueobjects.JsonValue(JsonTextOrNullLiteral(ExtractJsonString(value, serializerOptions)));
        }

        if (actualTarget == typeof(JsonNode))
        {
            return JsonNode.Parse(JsonTextOrNullLiteral(ExtractJsonString(value, serializerOptions)), new JsonNodeOptions());
        }

        var jsonText = ExtractJsonString(value, serializerOptions);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            if (value is Stream)
            {
                throw new JsonException("JSON payload cannot be empty.");
            }

            jsonText = JsonTextOrNullLiteral(jsonText);
        }

        try
        {
            // Into the declared type, so JSON null reads as null for a Nullable<T> (COR-007).
            return JsonSerializer.Deserialize(jsonText, targetType, serializerOptions);
        }
        catch (JsonException ex) when (value is not Stream)
        {
            TryLogDebug("Failed to deserialize JSON payload into {TargetType}", actualTarget);
            throw new JsonException($"Failed to deserialize JSON payload into {actualTarget}.", ex);
        }
    }

    /// <summary>
    /// COR-007: blank text (empty or whitespace) in a JSON column is the JSON literal <c>null</c> on
    /// every read path, so each target reads it as it reads <c>null</c>: null for a reference or
    /// nullable type, a JSON-null document or value, and a failure for a non-nullable value type.
    /// </summary>
    private static bool IsJsonValueType(Type type) =>
        type == typeof(JsonDocument) || type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type) ||
        type == typeof(types.valueobjects.JsonValue);

    internal static string JsonTextOrNullLiteral(string? text) => string.IsNullOrWhiteSpace(text) ? "null" : text;

    // DRY-015: every JSON-carrying input goes through ExtractJsonString; blank is the JSON null (COR-007).
    private static JsonDocument ToJsonDocument(object value, JsonSerializerOptions options)
    {
        if (value is JsonDocument doc)
        {
            return doc;
        }

        try
        {
            return JsonDocument.Parse(JsonTextOrNullLiteral(ExtractJsonString(value, options)));
        }
        catch (JsonException ex) when (value is not Stream)
        {
            TryLogDebug("Failed to deserialize JSON payload into {TargetType}", typeof(JsonDocument));
            throw new JsonException($"Failed to deserialize JSON payload into {typeof(JsonDocument)}.", ex);
        }
    }

    /// <summary>
    /// The JSON text an input carries (string, UTF-8 bytes, segment, memory, stream, char[], JSON DOM),
    /// the one reader every JSON target uses (DRY-015). Empty binary input is empty text.
    /// </summary>
    internal static string ExtractJsonString(object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case string jsonText:
                return jsonText;
            case JsonElement element:
                return element.GetRawText();
            case JsonDocument document:
                return document.RootElement.GetRawText();
            case JsonNode node:
                return node.ToJsonString(options);
            case byte[] bytes:
                return bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
            case ArraySegment<byte> segment:
                return segment.Count == 0 ? string.Empty : Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);
            case ReadOnlyMemory<byte> memory:
                return Encoding.UTF8.GetString(memory.Span);
            case Stream stream:
                return StreamToString(stream);
            case char[] chars:
                return new string(chars);
            default:
                return JsonSerializer.Serialize(value, options);
        }
    }

    /// <summary>True for the inputs that carry JSON text (what <see cref="ExtractJsonString"/> reads as text).</summary>
    internal static bool CarriesJsonText(object value) =>
        value is string or byte[] or ArraySegment<byte> or ReadOnlyMemory<byte> or Stream or char[] or JsonNode;

    internal static string GetJsonText(object value, JsonSerializerOptions? options = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return ExtractJsonString(value, options ?? JsonSerializerOptions.Default);
    }

    private static string StreamToString(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Resolves a coercion delegate for a known source/target type pair once per plan (DataReaderMapper),
    /// converting with the reader's dialect options: its provider-specific coercions and Guid byte order (TYPE-002).
    /// </summary>
    internal static Func<object?, object?> ResolveCoercer(
        Type sourceType,
        Type targetType,
        EnumParseFailureMode parseMode,
        TypeCoercionOptions? options)
    {
        var provider = options?.Provider ?? SupportedDatabase.Unknown;
        var guidBytesBigEndian = options?.GuidBytesBigEndian ?? false;
        var runtimeTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (TryGetEnumType(targetType, out var enumType))
        {
            return value => Utils.IsNullOrDbNull(value) ? null : CoerceEnum(value!, enumType, parseMode, targetType);
        }

        // The caller's options, as Coerce uses them: the TimePolicy decides how a DateTime becomes a
        // DateTimeOffset (COR-011).
        var timeOptions = options ?? TypeCoercionOptions.Default;
        if (runtimeTarget == typeof(DateTimeOffset))
        {
            return value =>
                Utils.IsNullOrDbNull(value) ? null : CoerceDateTimeOffset(value!, timeOptions);
        }

        if (runtimeTarget == typeof(DateTime))
        {
            return value => Utils.IsNullOrDbNull(value) ? null : CoerceDateTime(value!, timeOptions);
        }

        if (runtimeTarget.IsAssignableFrom(sourceType) &&
            runtimeTarget != typeof(DateTime) &&
            runtimeTarget != typeof(DateTimeOffset))
        {
            return value => Utils.IsNullOrDbNull(value) ? null : value;
        }

        var coercion = types.coercion.CoercionRegistry.Shared.GetCoercion(runtimeTarget, provider);
        if (coercion != null)
        {
            return value =>
            {
                if (Utils.IsNullOrDbNull(value))
                {
                    return null;
                }

                if (runtimeTarget == typeof(Guid) && TryDecodeGuidBytes(value!, guidBytesBigEndian, out var guid))
                {
                    return guid;
                }

                var dbValue = new types.coercion.DbValue(value!, sourceType);
                if (coercion.TryRead(in dbValue, runtimeTarget, out var result))
                {
                    return result;
                }

                return ConvertRegisteredFallback(value!, runtimeTarget, options);
            };
        }

        var coerceOptions = options ?? TypeCoercionOptions.Default;
        return value => Utils.IsNullOrDbNull(value)
            ? null
            : Coerce(value!, sourceType, targetType, coerceOptions);
    }

    /// <summary>
    /// Byte reader for compiled mappers: the value is read straight into an array of the length the
    /// provider reports. GetBytes may return fewer bytes than requested, so it reads until the value
    /// is complete.
    /// </summary>
    public static byte[] ReadBytes(IDataRecord reader, int ordinal)
    {
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length <= 0)
        {
            return Array.Empty<byte>();
        }

        // The reported length sizes the result: read straight into it (a rented buffer for small
        // values cost a second copy and allocated the same array).
        var heapBuffer = new byte[length];
        var total = ReadAllBytes(reader, ordinal, heapBuffer, (int)length);
        if (total < heapBuffer.Length)
        {
            Array.Resize(ref heapBuffer, total);
        }

        return heapBuffer;
    }

    private static int ReadAllBytes(IDataRecord reader, int ordinal, byte[] buffer, int length)
    {
        var total = 0;
        while (total < length)
        {
            var read = (int)reader.GetBytes(ordinal, total, buffer, total, length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>
    /// Reads a GUID from a binary column without allocating a byte array.
    /// Optimized for SQLite and other providers that store GUIDs as BLOBs.
    /// </summary>
    public static Guid ReadGuidFromBytes(IDataRecord reader, int ordinal) => ReadGuidFromBytes(reader, ordinal, false);

    /// <summary>
    /// Reads a GUID from a binary column in the given byte order (RFC 4122 big-endian or .NET's
    /// mixed-endian order; <c>SqlDialect.StoresGuidBytesBigEndian</c>).
    /// </summary>
    private static exceptions.InvalidValueException NotAGuid(long length) =>
        new($"Binary column holds {length.ToString(CultureInfo.InvariantCulture)} bytes; a GUID needs exactly 16.");

    public static Guid ReadGuidFromBytes(IDataRecord reader, int ordinal, bool bigEndian)
    {
        // Exactly 16 bytes: a longer value was read as its first 16, a silently wrong Guid (COR-003).
        // The length comes from the provider; asking for a 17th byte to prove there is none threw on
        // MySql.Data, which refuses a read at the end of a value (COR-014).
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length != 16)
        {
            throw NotAGuid(length);
        }

        var temp = System.Buffers.ArrayPool<byte>.Shared.Rent(16);
        try
        {
            var read = ReadAllBytes(reader, ordinal, temp, 16);
            if (read != 16)
            {
                throw NotAGuid(read);
            }

            return new Guid(temp.AsSpan(0, 16), bigEndian);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(temp);
        }
    }
}

