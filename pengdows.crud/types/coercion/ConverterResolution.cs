// =============================================================================
// FILE: ConverterResolution.cs
// PURPOSE: Resolves how a value converts to a type: a cached compiled converter for a type pair (ConvertWithCache), and the
//          per-field delegate a mapper calls (ResolveCoercer).
//
// AI SUMMARY:
// - ConvertWithCache: converts a non-null value to a type, compiling the converter once per (source, target) pair.
// - ResolveCoercer: the delegate for one field: enum, DateTime/DateTimeOffset, assignable, registered coercion, else the
//   general TypeCoercionHelper.Coerce. A DbNull or null gives null.
// - ConvertRegisteredFallback: a registered coercion that declined a value, converted by the general path.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.@internal;

namespace pengdows.crud.types.coercion;

internal static class ConverterResolution
{
    /// <summary>
    /// Cache of compiled type conversion delegates for faster Convert.ChangeType operations.
    /// Key: (sourceType, targetType), Value: compiled converter function.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type source, Type target), Func<object, object>> _conversionCache =
        new();

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

    internal static Func<object?, object?> ResolveCoercer(
        Type sourceType,
        Type targetType,
        EnumParseFailureMode parseMode,
        TypeCoercionOptions? options)
    {
        var provider = options?.Provider ?? SupportedDatabase.Unknown;
        var guidBytesBigEndian = options?.GuidBytesBigEndian ?? false;
        var runtimeTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (EnumCoercion.TryGetEnumType(targetType, out var enumType))
        {
            return value => Utils.IsNullOrDbNull(value) ? null : EnumCoercion.CoerceEnum(value!, enumType, parseMode, targetType);
        }

        // The caller's options, as Coerce uses them: the TimePolicy decides how a DateTime becomes a
        // DateTimeOffset (COR-011).
        var timeOptions = options ?? TypeCoercionOptions.Default;
        if (runtimeTarget == typeof(DateTimeOffset))
        {
            return value =>
                Utils.IsNullOrDbNull(value) ? null : DateTimeCoercion.CoerceDateTimeOffset(value!, timeOptions);
        }

        if (runtimeTarget == typeof(DateTime))
        {
            return value => Utils.IsNullOrDbNull(value) ? null : DateTimeCoercion.CoerceDateTime(value!, timeOptions);
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

                if (runtimeTarget == typeof(Guid) && pengdows.crud.types.coercion.BinaryCoercion.TryDecodeGuidBytes(value!, guidBytesBigEndian, out var guid))
                {
                    return guid;
                }

                if (runtimeTarget == typeof(byte[]) && value is Guid guidSource)
                {
                    return pengdows.crud.types.coercion.BinaryCoercion.GuidBytes(guidSource, guidBytesBigEndian);
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
            : TypeCoercionHelper.Coerce(value!, sourceType, targetType, coerceOptions);
    }

    // A registered coercion (e.g. decimal's) that declines a value falls back here. Wide integers
    // (BigInteger, Int128, UInt128) aren't IConvertible, so they take the checked TYPE-005 casts:
    // exact or OverflowException, as through Coerce.
    // A sequence (DuckDB.NET's List<T> for a LIST column) takes the element-by-element conversion
    // too (TYPE-002).
    private static object ConvertRegisteredFallback(object value, Type target, TypeCoercionOptions? options = null)
    {
        if (pengdows.crud.types.coercion.WideIntegerCoercion.TryCoerceWideInteger(value, target, out var wide))
        {
            return wide!;
        }

        if (SequenceCoercion.TryCoerceSequence(value, target, options ?? TypeCoercionOptions.Default, out var sequence))
        {
            return sequence!;
        }

        return ConvertWithCache(value, target);
    }
}
