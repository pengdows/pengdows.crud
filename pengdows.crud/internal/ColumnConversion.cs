using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud.@internal;

/// <summary>
/// One typed expression that converts a value read from a column into a property's type, shared by the gateway's
/// compiled mapper and the plan mapper: a native match, the numeric and date conversions, the typed coercion registry,
/// and last the general <c>TypeCoercionHelper.Coerce</c>. No boxing unless the general path is reached.
/// </summary>
internal static class ColumnConversion
{
    private static readonly MethodInfo Coerce = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.Coerce),
        BindingFlags.Public | BindingFlags.Static,
        null,
        new[] { typeof(object), typeof(Type), typeof(Type), typeof(TypeCoercionOptions) },
        null)!;

    internal static readonly MethodInfo NormalizeDateTime = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.NormalizeDateTime),
        BindingFlags.NonPublic | BindingFlags.Static,
        null,
        new[] { typeof(DateTime) },
        null)!;

    internal static Expression Build(Expression value, Type sourceType, Type targetType,
        TypeCoercionOptions? coercionOptions)
    {
        var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (sourceType == underlyingTargetType)
        {
            var result = value.Type != sourceType ? Expression.Convert(value, sourceType) : value;

            // Normalize DateTime: treat Unspecified as UTC, convert Local to UTC.
            // Many drivers (Snowflake TIMESTAMP_NTZ, SQL Server datetime, MySQL, Oracle)
            // return DateTimeKind.Unspecified for timezone-naive columns that store UTC values.
            // Without this, calling .ToUniversalTime() on the entity property would incorrectly
            // apply the host's local timezone offset (e.g. UTC-6 → +6 hour drift).
            if (underlyingTargetType == typeof(DateTime))
            {
                result = Expression.Call(NormalizeDateTime, result);
            }

            return targetType != underlyingTargetType
                ? Expression.Convert(result, targetType)
                : result;
        }

        // Numeric columns into numeric, enum and bool properties, as DataReaderMapper reads them.
        if (NumericExpressions.Handles(sourceType, underlyingTargetType))
        {
            var converted = NumericExpressions.Convert(value, sourceType, underlyingTargetType);
            return targetType != underlyingTargetType ? Expression.Convert(converted, targetType) : converted;
        }

        // Date and time conversions TypeCoercionHelper.Coerce would make, without boxing the value or
        // its result (PERF-021): DateTime into DateOnly, TimeSpan into TimeOnly, timestamp text (SQLite)
        // into DateTime/DateTimeOffset/DateOnly.
        if (TypedCoercions.Find(sourceType, underlyingTargetType) is { } typedCoercion)
        {
            var typedSource = value.Type != sourceType ? Expression.Convert(value, sourceType) : value;
            var converted = Expression.Call(typedCoercion, typedSource,
                Expression.Constant(coercionOptions ?? TypeCoercionOptions.Default, typeof(TypeCoercionOptions)));
            return targetType != underlyingTargetType ? Expression.Convert(converted, targetType) : converted;
        }

        // Fallback: TypeCoercionHelper.Coerce(object, fieldType, targetType)
        var boxedValue = Expression.Convert(value, typeof(object));
        var coerceCall = Expression.Call(
            Coerce,
            boxedValue,
            Expression.Constant(sourceType),
            Expression.Constant(targetType),
            Expression.Constant(coercionOptions, typeof(TypeCoercionOptions)));

        return Expression.Convert(coerceCall, targetType);
    }

}
